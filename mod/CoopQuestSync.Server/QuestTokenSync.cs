using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OblivionMp.ChatMod.Common;
using OblivionMp.ChatMod.Serverside;
using OblivionMP.SDK.Common.Archetypes;
using OblivionMP.SDK.Common.Archetypes.Mixins;
using OblivionMP.SDK.Serverside.Items;
using ReadyM.Api.Idents;
using ReadyM.Relay.Common.Oblivion;
using ReadyM.Relay.Common.Oblivion.ECS.Values;
using ReadyM.Relay.Common.Oblivion.Rpc;
using ReadyM.SDK.Server.Events;
using ReadyM.SDK.Archetypes.Core;
using ReadyM.SDK.Attributes;
using ReadyM.SDK.Server.Entities;

namespace CoopQuestSync.Server;

/// <summary>
/// Reads the quest tokens the plugin puts in each player's inventory, keeps the shared progress, and pushes
/// tokens back down so the plugin sets the same stages in the other players' games.
///
/// Joining goes through a short handshake, because OblivionMP keeps the inventory between sessions but not the
/// game's quest progress, so the plugin replays the player's own stages on every join:
///   1. Handshake: the server sees the beacon and bumps CQSSeq. The plugin replays nothing until then, so the
///      inventory the server sees before that is the real one. The plugin answers with CQSAck.
///   2. Restoring: the plugin replays the player's own stages while holding CQSBusy. When CQSBusy goes away,
///      the server puts the inventory back the way it was just before, which undoes rewards and quest-item
///      removals that the replay did a second time.
///   3. Ready: the server pushes the shared stages the player lacks (with auto sync on). Rewards from these are kept,
///      because the player is getting those stages for the first time.
///
/// The plugin wraps each player effect of a stage script (teleport, combat, items, status, ranks, cut scene) in a
/// switch: the effect runs when the player reached the stage in their own game, or when the server holds that
/// category's CQSAllow item in their inventory. The server sets those items from the web page's switches, one set
/// for each of the three moments above (rejoin, join catch-up, live).
///
/// Everything here runs on the server's game loop. The web page talks to it through <see cref="Enqueue"/> and
/// reads <see cref="StateJson"/>, a snapshot rebuilt on every pass.
/// </summary>
[Service]
public sealed partial class QuestTokenSync(IEntities entities, IServerEvents events, Inventories inventories,
    IChat chat, QuestSyncConfig config, GameData data, ILogger logger)
{
    private const float BumpEverySeconds = 3f;
    private const int MaxBumps = 10;
    private const float WaitForReplaySeconds = 8f;
    private const float MaxRestoreSeconds = 180f;
    private const float CatchUpWaitSeconds = 10f;

    public TokenMap Map => data.Map;

    /// <summary>Shared progress: every stage a player has reported (or an admin pushed).</summary>
    public SortedDictionary<string, SortedSet<int>> Shared { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool AutoSync { get; set; }

    /// <summary>Every switch of the web page.</summary>
    public CoopSettings Settings { get; private set; } = new();

    /// <summary>
    /// Quests an admin reset. A player who joins with old tokens for one of these has them taken away, until someone
    /// reaches a stage of that quest again after the reset.
    /// </summary>
    public SortedSet<string> ResetQuests { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Where each player (by nickname) last stood outdoors. See <see cref="ReturnOnJoin"/>.</summary>
    public Dictionary<string, ReturnSpot> Returns { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Where each player last stood outdoors: the fallback when they can't be taken back inside.</summary>
    public Dictionary<string, ReturnSpot> Outdoors { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Someone has left the sewers on this server, so the prison intro has been played (or skipped) here: new players
    /// start at the sewer exit. See <see cref="NewStart"/>.
    /// </summary>
    public bool SewersLeft { get; set; }

    /// <summary>
    /// Players (by nickname) who typed /coop goto lock: others must ask them before travelling to them. See
    /// <see cref="TeleportService"/>.
    /// </summary>
    public SortedSet<string> GotoLocked { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// ReadyM accounts (N format) whose character is brand new on this server and still waits for its first-join
    /// setup (<see cref="FreshCharacter"/>). Kept in the state file, so a player who leaves before it ran gets it next time.
    /// </summary>
    public SortedSet<string> NewCharacters { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>ReadyM accounts (N format) that have joined this server since 0.7.0. See <see cref="PlayerSaves"/>.</summary>
    public SortedSet<string> KnownAccounts { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Stages a brand-new character already has when they first join: OblivionMP 1.1 starts everyone from the same
    /// world save, with a few quests under way. Everyone has them, so they are never shared or announced.
    /// </summary>
    public HashSet<QuestStage> PresetStages { get; } = new();

    private readonly Dictionary<PlayerId, string> _accounts = new();

    /// <summary>Saves the state file on the next pass.</summary>
    public void MarkDirty() => _dirty = true;

    /// <summary>The leader whose progress alone is shared, or null when anyone's progress counts.</summary>
    public string? SourcePlayer => Settings.ProgressFrom == "leader" && Settings.Leader.Length > 0 ? Settings.Leader : null;

    /// <summary>A JSON snapshot of players and progress for the web page, rebuilt on every pass.</summary>
    public string StateJson => _stateJson;

    /// <summary>Lets other services add their own fields to each player on the web page.</summary>
    public Func<PlayerId, object?>? PlayerExtras { get; set; }

    /// <summary>Each player's command access for the web page (see <see cref="CommandAccess"/>).</summary>
    public Func<PlayerId, object?>? PlayerAccess { get; set; }

    /// <summary>Where the admin list comes from, for the web page (see <see cref="CommandAccess"/>).</summary>
    public Func<object?>? AccessInfo { get; set; }

    /// <summary>Raised on the game loop after a pass read every inventory.</summary>
    public event Action? Passed;

    private volatile string _stateJson = "{}";
    private readonly ConcurrentQueue<Action> _queue = new();
    private readonly Dictionary<PlayerId, PlayerView> _players = new();
    private float _elapsed;
    private float _clock;
    private bool _dirty;

    public enum Phase { NoPlugin, Handshake, Restoring, Ready }

    public sealed class PlayerView
    {
        public Phase Phase = Phase.NoPlugin;
        public uint? ModIndex;
        public HashSet<QuestStage> Stages = new();
        public bool WarnedNoPlugin;
        public int AckAtFirstLook;
        public int Bumps;
        public float LastBumpAt;
        public float AckSeenAt;
        public float RestoreStartedAt;
        public Dictionary<uint, int>? Baseline;
        public Dictionary<uint, int> Previous = new();
        /// <summary>Quests an admin reset while this player was in game: their tokens are taken away until they rejoin.</summary>
        public HashSet<string> Suppressed = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Catching up on the party's stages after joining: the "join" switches apply.</summary>
        public bool CatchingUp;
        public bool CatchUpSawBusy;
        public float CatchUpStartedAt;
        /// <summary>Shared stages arrived while the player could not take them (in a conversation).</summary>
        public bool PendingPush;
        /// <summary>The next item comparison is skipped (the server itself just changed the inventory).</summary>
        public bool SkipItemDiff;

        /// <summary>Plugin items by local id, as last read (CQSAllow*, CQSFreeze...).</summary>
        public Dictionary<uint, int> Own = new();
        /// <summary>Set by the conversation relay: the player should stand still.</summary>
        public bool WantFrozen;

        public bool InDialogue;
        public bool InCombat;
        public AreaId? Area;

        /// <summary>
        /// The plugin's CQSIntro: 0 once the player has left the sewers (MQ01 88), 1 while they are in the prison intro,
        /// 2 while they never left the sewers but no intro runs (not the vanilla start). See <see cref="NewStart"/>.
        /// </summary>
        public int Intro;
        /// <summary>Shared stages wait until the player is out of the sewers: replaying MQ02 would stop their intro.</summary>
        public bool IntroHeld;
        /// <summary>The language the player's game runs in ("fr"), from their client; null until it says.</summary>
        public string? Language;
        /// <summary>
        /// A brand-new character waiting for the first-join setup (<see cref="FreshCharacter"/>). The party's progress
        /// waits until it ran, and every stage they report meanwhile is the preset's (<see cref="PresetStages"/>).
        /// </summary>
        public bool Fresh;
        /// <summary>Set up as a new character during this session: not taken back to where the nickname last stood.</summary>
        public bool StartedFresh;
    }

    /// <summary>The language <paramref name="id"/>'s game runs in, or null (then the page's text is used).</summary>
    public string? LanguageOf(PlayerId id) => _players.TryGetValue(id, out var view) ? view.Language : null;

    private readonly record struct Snapshot(uint? ModIndex, HashSet<QuestStage> Stages, int Busy, int Ack,
        Dictionary<uint, int> Items, List<(uint FormId, int Count)> Legacy, Dictionary<uint, int> Own);

    public IReadOnlyDictionary<PlayerId, PlayerView> Players => _players;

    private string StatePath => Path.GetFullPath(config.StateFile);

    /// <summary>Runs <paramref name="action"/> on the game loop, on the next tick. Safe to call from any thread.</summary>
    public void Enqueue(Action action) => _queue.Enqueue(action);

    public string Nickname(PlayerId id)
    {
        var name = chat.GetNickname(id);
        return string.IsNullOrEmpty(name) ? id.ToString() : name;
    }

    public PlayerView View(PlayerId id)
    {
        if (!_players.TryGetValue(id, out var view))
            _players[id] = view = new PlayerView();
        return view;
    }

    private void Start()
    {
        AutoSync = config.AutoSync;
        LoadState();
        events.OnPlayerConnected += OnPlayerConnected;
        events.OnPlayerDisconnected += OnPlayerDisconnected;
        events.OnPlayerJoinedArea += OnPlayerJoinedArea;
        events.OnPlayerLeftArea += OnPlayerLeftArea;
        logger.LogInformation("[QuestSync] Loaded {Quests} quests / {Stages} stages from the token map. Auto sync is {Auto}. Progress from: {Source}. State file: {Path}",
            Map.Quests.Count, Map.StageCount, AutoSync ? "ON" : "OFF", SourcePlayer ?? "anyone", StatePath);
        RebuildState();
    }

    private void Stop()
    {
        events.OnPlayerConnected -= OnPlayerConnected;
        events.OnPlayerDisconnected -= OnPlayerDisconnected;
        events.OnPlayerJoinedArea -= OnPlayerJoinedArea;
        events.OnPlayerLeftArea -= OnPlayerLeftArea;
        SaveState();
    }

    private void OnPlayerConnected(Player joined, Guid readyMId)
    {
        var view = _players[joined.PlayerId] = new PlayerView();
        if (readyMId == Guid.Empty)
            return;
        var account = readyMId.ToString("N");
        _accounts[joined.PlayerId] = account;
        var known = KnownAccounts.Contains(account);
        if (KnownAccounts.Add(account))
            _dirty = true;
        if (!Settings.NewCharacterSetup)
            return;
        if (!NewCharacters.Contains(account) && PlayerSaves.IsBrandNew(readyMId, known, logger))
        {
            NewCharacters.Add(account);
            _dirty = true;
        }
        view.Fresh = NewCharacters.Contains(account);
        if (view.Fresh)
            logger.LogInformation("[QuestSync] Player {Account}: a new character. Setting it up once they are in (menus, starter kit).", account);
    }

    private void OnPlayerDisconnected(Player gone, Guid _, LiteNetLib.DisconnectReason __)
    {
        _players.Remove(gone.PlayerId);
        _accounts.Remove(gone.PlayerId);
    }

    /// <summary>The first-join setup ran (or isn't wanted): the party's progress reaches the player from now on.</summary>
    public void CharacterSetUp(PlayerId id, bool done)
    {
        if (_players.TryGetValue(id, out var view))
        {
            view.Fresh = false;
            view.StartedFresh = done;
        }
        if (_accounts.TryGetValue(id, out var account) && NewCharacters.Remove(account))
            _dirty = true;
    }

    private void OnPlayerJoinedArea(PlayerId playerId, AreaId area) => View(playerId).Area = area;

    private void OnPlayerLeftArea(PlayerId playerId, AreaId area)
    {
        if (_players.TryGetValue(playerId, out var view) && view.Area is { } a && a.Equals(area))
            view.Area = null;
    }

    /// <summary>Whether two players are in the same area (an interior, or the same exterior world space).</summary>
    public bool SameArea(PlayerId a, PlayerId b)
        => _players.TryGetValue(a, out var va) && _players.TryGetValue(b, out var vb)
           && va.Area is { } x && vb.Area is { } y && x.Equals(y);

    private void Update()
    {
        while (_queue.TryDequeue(out var action))
        {
            try { action(); }
            catch (Exception e) { logger.LogWarning("[QuestSync] Web action failed: {Error}", e.Message); }
        }

        _clock += Time.DeltaTime;
        _elapsed += Time.DeltaTime;
        if (_elapsed < config.IntervalSeconds)
            return;
        _elapsed = 0;

        var newlyDone = new List<(PlayerId From, QuestStage Stage, bool Live)>();
        var nowReady = new List<MainCharacter>();
        var pickedUp = new List<(PlayerId From, uint FormId)>();
        foreach (var main in entities.Query<MainCharacter>())
        {
            var playerId = main.PlayerId;
            var view = View(playerId);
            view.InDialogue = main.InDialogue;
            view.InCombat = main.InCombat;

            var snap = Read(main, view.ModIndex);
            if (snap.ModIndex is null)
            {
                if (!view.WarnedNoPlugin)
                {
                    logger.LogInformation("[QuestSync] {Player}: no CoopQuestSync beacon in inventory yet (plugin not loaded, or still starting).", Nickname(playerId));
                    view.WarnedNoPlugin = true;
                }
                continue;
            }
            view.ModIndex = snap.ModIndex;
            view.Own = snap.Own;
            view.Intro = Map.IntroLocalId != 0 ? snap.Own.GetValueOrDefault(Map.IntroLocalId) : 0;
            var slot = snap.ModIndex.Value;

            switch (view.Phase)
            {
                case Phase.NoPlugin:
                    logger.LogInformation("[QuestSync] {Player}: plugin found in load order slot {Slot:X2}, {Count} stage token(s). Starting handshake.",
                        Nickname(playerId), slot, snap.Stages.Count);
                    if (snap.Busy > 0)
                        Take(main, slot << 24 | Map.BusyLocalId, snap.Busy);
                    foreach (var (formId, count) in snap.Legacy)
                    {
                        Take(main, formId, count);
                        logger.LogInformation("[QuestSync] {Player}: removed old test token {FormId:X8}", Nickname(playerId), formId);
                    }
                    foreach (var stage in snap.Stages.Where(st => ResetQuests.Contains(st.Quest)).ToList())
                    {
                        if (Map.TryGetToken(stage, out var local))
                            Take(main, slot << 24 | local, 1);
                        snap.Stages.Remove(stage);
                        logger.LogInformation("[QuestSync] {Player}: took old token for {Stage} (quest was reset)", Nickname(playerId), stage);
                    }
                    view.AckAtFirstLook = snap.Ack;
                    view.Phase = Phase.Handshake;
                    // The switches must be in place before the plugin starts replaying, which the bump allows.
                    ApplySwitches(main, view);
                    Bump(main, view);
                    break;

                case Phase.Handshake when snap.Ack > view.AckAtFirstLook:
                    view.Phase = Phase.Restoring;
                    view.AckSeenAt = _clock;
                    break;

                case Phase.Handshake when _clock - view.LastBumpAt > BumpEverySeconds:
                    // Quest scripts don't run on the loading screen: only count bumps once the player is in the world.
                    if (view.Area is null)
                        view.Bumps = 0;
                    if (view.Bumps < MaxBumps)
                        Bump(main, view);
                    else
                    {
                        logger.LogWarning("[QuestSync] {Player}: the plugin never answered the handshake. Syncing without restoring.", Nickname(playerId));
                        view.Phase = Phase.Ready;
                        nowReady.Add(main);
                    }
                    break;
            }

            if (view.Phase == Phase.Restoring)
            {
                if (snap.Busy > 0 && view.Baseline is null)
                {
                    view.Baseline = view.Previous;
                    view.RestoreStartedAt = _clock;
                    logger.LogInformation("[QuestSync] {Player}: restoring {Count} stage(s) after joining.", Nickname(playerId), snap.Stages.Count);
                }
                else if (view.Baseline is not null && (snap.Busy == 0 || _clock - view.RestoreStartedAt > MaxRestoreSeconds))
                {
                    if (snap.Busy > 0)
                        logger.LogWarning("[QuestSync] {Player}: restore is taking too long; finishing it now.", Nickname(playerId));
                    if (config.UndoReplayRewards)
                        Revert(main, view.Baseline, snap.Items);
                    view.SkipItemDiff = true;
                    view.Baseline = null;
                    view.Phase = Phase.Ready;
                    nowReady.Add(main);
                    logger.LogInformation("[QuestSync] {Player}: restore finished.", Nickname(playerId));
                }
                else if (view.Baseline is null && _clock - view.AckSeenAt > WaitForReplaySeconds)
                {
                    view.Phase = Phase.Ready;
                    nowReady.Add(main);
                    logger.LogInformation("[QuestSync] {Player}: nothing to restore.", Nickname(playerId));
                }
            }

            // The catch-up after joining ends when its batch is over (CQSBusy came and went), or when none came.
            if (view.CatchingUp)
            {
                if (snap.Busy > 0)
                    view.CatchUpSawBusy = true;
                else if (view.CatchUpSawBusy || _clock - view.CatchUpStartedAt > CatchUpWaitSeconds)
                {
                    view.CatchingUp = false;
                    view.SkipItemDiff = true;
                    logger.LogInformation("[QuestSync] {Player}: caught up with the party.", Nickname(playerId));
                }
            }

            // Quests an admin reset: keep taking those tokens away until the player rejoins with a fresh game.
            if (view.Suppressed.Count > 0)
            {
                foreach (var stage in snap.Stages.Where(s => view.Suppressed.Contains(s.Quest)).ToList())
                {
                    if (Map.TryGetToken(stage, out var local))
                        Take(main, slot << 24 | local, 1);
                    snap.Stages.Remove(stage);
                }
            }

            foreach (var stage in snap.Stages)
                if (!view.Stages.Contains(stage))
                {
                    logger.LogInformation("[QuestSync] {Player}: has {Stage}{Note}", Nickname(playerId), stage, view.Phase == Phase.Ready ? " (new)" : "");
                    newlyDone.Add((playerId, stage, view.Phase == Phase.Ready && snap.Busy == 0));
                }
            foreach (var stage in view.Stages)
                if (!snap.Stages.Contains(stage))
                    logger.LogInformation("[QuestSync] {Player}: token for {Stage} is GONE", Nickname(playerId), stage);
            view.Stages = snap.Stages;

            // Quest items someone picked up in their own game (never during a replay).
            if (view.Phase == Phase.Ready && snap.Busy == 0 && !view.CatchingUp && !view.SkipItemDiff && Settings.ShareQuestItems)
                foreach (var (formId, count) in snap.Items)
                    if (count > view.Previous.GetValueOrDefault(formId) && data.FromGame(formId) is { } id && Map.QuestItems.Contains(id))
                        pickedUp.Add((playerId, formId));
            view.SkipItemDiff = false;
            view.Previous = snap.Items;

            ApplySwitches(main, view);
        }

        var sharedChanged = false;
        foreach (var (from, stage, live) in newlyDone)
        {
            if (_players.TryGetValue(from, out var fresh) && fresh.Fresh)
            {
                // The party's progress waits for a fresh character, so whatever they have came with the preset start.
                if (Map.IsShared(stage.Quest) && PresetStages.Add(stage))
                {
                    _dirty = true;
                    logger.LogInformation("[QuestSync] {Stage} comes with OblivionMP's start (a new character has it): never shared.", stage);
                }
                if (Shared.TryGetValue(stage.Quest, out var had) && had.Remove(stage.Stage))
                {
                    if (had.Count == 0)
                        Shared.Remove(stage.Quest);
                    _dirty = true;
                }
                continue;
            }
            if (SourcePlayer is not null && !string.Equals(Nickname(from), SourcePlayer, StringComparison.OrdinalIgnoreCase))
                continue;
            if (AddShared(stage))
            {
                sharedChanged = true;
                logger.LogInformation("[QuestSync] Shared progress: {Stage} (first reported by {Player})", stage, Nickname(from));
                if (live && Settings.QuestMessages)
                    Announce(from, stage);
            }
        }

        foreach (var (from, formId) in pickedUp)
            ShareItem(from, formId, 1, onlyIfMissing: true);

        if (AutoSync)
        {
            foreach (var main in entities.Query<MainCharacter>())
            {
                if (!_players.TryGetValue(main.PlayerId, out var view) || view.Phase != Phase.Ready)
                    continue;
                var joining = nowReady.Any(m => m.PlayerId == main.PlayerId);
                // Still in the intro (or about to choose a start): no shared stages yet. MQ02 25 stops the intro, and
                // the party's progress would land in the prison. The catch-up runs once they are out of the sewers.
                if (HoldsForIntro(view))
                {
                    if ((joining || sharedChanged || view.PendingPush) && !view.IntroHeld)
                    {
                        view.IntroHeld = true;
                        logger.LogInformation(view.Fresh
                            ? "[QuestSync] {Player}: a new character; the party's progress waits until it is set up."
                            : "[QuestSync] {Player}: still in the intro; the party's progress waits until they leave the sewers.", Nickname(main.PlayerId));
                    }
                    view.PendingPush = false;
                    continue;
                }
                if (view.IntroHeld)
                {
                    view.IntroHeld = false;
                    joining = true;
                    logger.LogInformation("[QuestSync] {Player}: catching up with the party.", Nickname(main.PlayerId));
                }
                if (!(joining || sharedChanged || view.PendingPush))
                    continue;
                if (Settings.HoldDuringDialogue && view.InDialogue && !joining)
                {
                    view.PendingPush = true;
                    continue;
                }
                view.PendingPush = false;
                if (joining)
                {
                    view.CatchingUp = true;
                    view.CatchUpSawBusy = false;
                    view.CatchUpStartedAt = _clock;
                    ApplySwitches(main, view);
                }
                var pushed = PushMissing(main, view);
                if (pushed > 0)
                    logger.LogInformation("[QuestSync] {Player}: pushed {Count} shared stage(s).", Nickname(main.PlayerId), pushed);
            }
        }

        Passed?.Invoke();

        if (_dirty)
            SaveState();
        RebuildState();
    }

    /// <summary>
    /// Whether the party's progress waits for this player: they are in the prison intro, or never left the sewers and
    /// are choosing or taking a start (<see cref="NewStart"/>), or are a new character not set up yet
    /// (<see cref="FreshCharacter"/>). A player who never left the sewers while the mod leaves
    /// the start to the game ("off") and no intro runs is not held: nothing would ever end the wait.
    /// </summary>
    public bool HoldsForIntro(PlayerView view)
    {
        if (view.Fresh || view.Intro == 1)
            return true;
        if (view.Intro != 2)
            return false;
        return Settings.NewStart != "off"
               || view.Own.GetValueOrDefault(Map.StartDoneLocalId) > 0 || view.Own.GetValueOrDefault(Map.StartPrisonLocalId) > 0;
    }

    /// <summary>"Alice advanced Quest: journal text" in the other players' chat.</summary>
    private void Announce(PlayerId from, QuestStage stage)
    {
        foreach (var (id, view) in _players)
        {
            if (id == from)
                continue;
            // In each player's own language, where the server has the text in it.
            var text = data.StageText(stage, view.Language);
            var line = $"{Nickname(from)} advanced {data.QuestName(stage.Quest, view.Language)}" + (text is null ? $" (stage {stage.Stage})." : $": {text}");
            chat.SendTo(id, line, ChatLineKind.System);
        }
    }

    /// <summary>
    /// The CQSAllow items that should be in this player's inventory now, from the phase they are in, and the
    /// CQSFreeze item. Reads the last snapshot, so it is cheap to call often.
    /// </summary>
    public void ApplySwitches(MainCharacter main, PlayerView view)
    {
        if (view.ModIndex is not { } slot)
            return;
        var phase = view.Phase != Phase.Ready ? "rejoin" : view.CatchingUp ? "join" : "live";
        foreach (var (category, local) in Map.AllowLocalIds)
            Ensure(main, view, slot, local, Settings.Allowed(phase, category));
        if (Map.FreezeLocalId != 0)
            Ensure(main, view, slot, Map.FreezeLocalId, view.WantFrozen);
    }

    private void Ensure(MainCharacter main, PlayerView view, uint slot, uint local, bool want)
    {
        var have = view.Own.GetValueOrDefault(local);
        if (want && have <= 0)
        {
            inventories.Add(main.As<Equipment>(), new InventoryEntry(new FormId(slot << 24 | local), 1, 0));
            view.Own[local] = 1;
        }
        else if (!want && have > 0)
        {
            Take(main, slot << 24 | local, have);
            view.Own[local] = 0;
        }
    }

    /// <summary>Gives an item (game form id) to every other ready player. onlyIfMissing: only those who have none.</summary>
    public int ShareItem(PlayerId from, uint formId, int count, bool onlyIfMissing)
    {
        var given = 0;
        foreach (var main in entities.Query<MainCharacter>())
        {
            if (main.PlayerId == from || !_players.TryGetValue(main.PlayerId, out var view) || view.Phase != Phase.Ready)
                continue;
            if (onlyIfMissing && view.Previous.GetValueOrDefault(formId) > 0)
                continue;
            inventories.Add(main.As<Equipment>(), new InventoryEntry(new FormId(formId), count, 0));
            view.Previous[formId] = view.Previous.GetValueOrDefault(formId) + count;
            given++;
            logger.LogInformation("[QuestSync] {Player}: shared {FormId:X8} x{Count} from {From}", Nickname(main.PlayerId), formId, count, Nickname(from));
        }
        return given;
    }

    /// <summary>One pass over the inventory: the plugin's slot, stage tokens, control items, and every other item.</summary>
    private Snapshot Read(MainCharacter main, uint? knownSlot)
    {
        uint? slot = knownSlot;
        var raw = new Dictionary<uint, int>();
        for (var i = 0; i < main.ItemsCount; i++)
        {
            var entry = main.GetItems(i);
            uint formId = entry.FormId.FormId;
            if (entry.Count <= 0 || formId >> 24 == 0xFF)
                continue;
            raw[formId] = raw.GetValueOrDefault(formId) + entry.Count;
            if (slot is null && (formId & 0x00FFFFFF) == Map.BeaconLocalId && formId >> 24 != 0x00)
                slot = formId >> 24;
        }
        var stages = new HashSet<QuestStage>();
        var items = new Dictionary<uint, int>();
        var legacy = new List<(uint, int)>();
        var own = new Dictionary<uint, int>();
        int busy = 0, ack = 0;
        var dialogueBeacon = data.DialogueBeacon;
        foreach (var (formId, count) in raw)
        {
            var local = formId & 0x00FFFFFF;
            if (slot is not null && formId >> 24 == slot)
            {
                if (local == Map.BusyLocalId) busy = count;
                else if (local == Map.AckLocalId) ack = count;
                else if (Map.TryGetStage(local, out var stage)) stages.Add(stage);
                else if (local >= Map.Legacy.Start && local < Map.Legacy.End) legacy.Add((formId, count));
                else own[local] = count;
                continue;
            }
            // The dialogue plugin's items (its beacon, line and menu tokens) come and go on their own.
            if (formId >> 24 != 0 && (local == dialogueBeacon || data.TryGetLine(local, out _) || data.MenuOf(local) is not null))
                continue;
            items[formId] = count;
        }
        return new Snapshot(slot, stages, busy, ack, items, legacy, own);
    }

    /// <summary>Puts the inventory back to <paramref name="before"/>, leaving the plugin's own items alone.</summary>
    private void Revert(MainCharacter main, Dictionary<uint, int> before, Dictionary<uint, int> after)
    {
        foreach (var formId in before.Keys.Union(after.Keys))
        {
            var diff = after.GetValueOrDefault(formId) - before.GetValueOrDefault(formId);
            if (diff > 0)
            {
                Take(main, formId, diff);
                logger.LogInformation("[QuestSync] {Player}: undo replay reward {FormId:X8} x{Count}", Nickname(main.PlayerId), formId, diff);
            }
            else if (diff < 0)
            {
                inventories.Add(main.As<Equipment>(), new InventoryEntry(new FormId(formId), -diff, 0));
                logger.LogInformation("[QuestSync] {Player}: give back {FormId:X8} x{Count} that the replay took", Nickname(main.PlayerId), formId, -diff);
            }
        }
    }

    public void Take(MainCharacter main, uint formId, int count)
    {
        var stack = new ItemStackData
        {
            BaseId = new FormId(formId),
            Count = count,
            Durability = InventoryEntry.NoInstanceOverride,
            Charge = InventoryEntry.NoInstanceOverride,
        };
        if (!inventories.TryTake(main.As<Equipment>(), stack))
            logger.LogWarning("[QuestSync] {Player}: could not take {FormId:X8} x{Count}", Nickname(main.PlayerId), formId, count);
    }

    /// <summary>
    /// Adds a stage to the shared progress. Stages of local-only quests (the tutorial) and the stages every character
    /// starts with (<see cref="PresetStages"/>) are never shared.
    /// </summary>
    public bool AddShared(QuestStage stage)
    {
        if (!Map.IsShared(stage.Quest) || PresetStages.Contains(stage))
            return false;
        if (!Shared.TryGetValue(stage.Quest, out var set))
            Shared[stage.Quest] = set = new SortedSet<int>();
        if (!set.Add(stage.Stage))
            return false;
        ResetQuests.Remove(stage.Quest);
        _dirty = true;
        return true;
    }

    /// <summary>Gives the player every shared stage they lack. Returns how many tokens were added.</summary>
    public int PushMissing(MainCharacter main, PlayerView view)
    {
        var count = 0;
        foreach (var (quest, set) in Shared)
        {
            if (view.Suppressed.Contains(quest))
                continue;
            foreach (var s in set)
            {
                var stage = new QuestStage(quest, s);
                if (!view.Stages.Contains(stage) && Give(main, view, stage))
                    count++;
            }
        }
        if (count > 0)
            Bump(main, view);
        return count;
    }

    /// <summary>Gives one stage token to one player and wakes the plugin. Returns false unless the player is ready.</summary>
    public bool PushOne(MainCharacter main, QuestStage stage)
    {
        if (!_players.TryGetValue(main.PlayerId, out var view) || view.Phase != Phase.Ready)
            return false;
        if (view.Stages.Contains(stage))
            return true;
        // Reset while they play: the server takes this quest's tokens until they rejoin (their game still has its
        // stages), so a token given now would be taken back at once (2026-11-13, MS05).
        if (view.Suppressed.Contains(stage.Quest))
            return false;
        if (!Give(main, view, stage))
            return false;
        Bump(main, view);
        return true;
    }

    /// <summary>
    /// Admin: marks a stage as shared progress and gives it to every ready player (or just <paramref name="only"/>).
    /// Returns how many players got it.
    /// </summary>
    public int AdminPush(QuestStage stage, PlayerId? only = null)
    {
        if (only is null && AddShared(stage))
            logger.LogInformation("[QuestSync] Shared progress: {Stage} (added by an admin)", stage);
        var count = 0;
        foreach (var main in entities.Query<MainCharacter>())
            if ((only is null || main.PlayerId == only) && PushOne(main, stage))
                count++;
        if (_dirty)
            SaveState();
        return count;
    }

    /// <summary>
    /// Admin: forgets a quest's shared progress and takes its tokens from everyone in game. The game cannot undo a
    /// stage while someone plays, so each player keeps the quest until they rejoin; then it is gone.
    /// </summary>
    public void ResetQuest(string quest)
    {
        Shared.Remove(quest);
        ResetQuests.Add(quest);
        foreach (var main in entities.Query<MainCharacter>())
        {
            if (!_players.TryGetValue(main.PlayerId, out var view) || view.ModIndex is not { } slot)
                continue;
            view.Suppressed.Add(quest);
            foreach (var stage in view.Stages.Where(s => s.Quest.Equals(quest, StringComparison.OrdinalIgnoreCase)).ToList())
            {
                if (Map.TryGetToken(stage, out var local))
                    Take(main, slot << 24 | local, 1);
                view.Stages.Remove(stage);
            }
        }
        logger.LogInformation("[QuestSync] {Quest} reset by an admin. Players lose it when they rejoin.", quest);
        RebuildState();
        SaveState();
    }

    private bool Give(MainCharacter main, PlayerView view, QuestStage stage)
    {
        if (view.ModIndex is not { } slot || !Map.TryGetToken(stage, out var local))
            return false;
        inventories.Add(main.As<Equipment>(), new InventoryEntry(new FormId(slot << 24 | local), 1, 0));
        view.Stages.Add(stage);
        logger.LogInformation("[QuestSync] {Player}: server gave token for {Stage}", Nickname(main.PlayerId), stage);
        return true;
    }

    /// <summary>The plugin's quest scripts rescan when CQSSeq's count changes. The first bump also starts the replay.</summary>
    private void Bump(MainCharacter main, PlayerView view)
    {
        if (view.ModIndex is not { } slot)
            return;
        inventories.Add(main.As<Equipment>(), new InventoryEntry(new FormId(slot << 24 | Map.SeqLocalId), 1, 0));
        view.Bumps++;
        view.LastBumpAt = _clock;
    }

    public void ClearShared()
    {
        foreach (var quest in Shared.Keys.ToList())
            ResetQuest(quest);
        Shared.Clear();
        _dirty = true;
        SaveState();
    }

    public void SetAuto(bool on)
    {
        AutoSync = on;
        _dirty = true;
    }

    /// <summary>Only <paramref name="nickname"/>'s progress is shared (they become the leader), or anyone's when null.</summary>
    public void SetSource(string? nickname)
    {
        if (string.IsNullOrWhiteSpace(nickname))
            Settings.ProgressFrom = "anyone";
        else
        {
            Settings.ProgressFrom = "leader";
            Settings.Leader = nickname.Trim();
        }
        _dirty = true;
        logger.LogInformation("[QuestSync] Progress counts from: {Source}", SourcePlayer ?? "anyone");
    }

    /// <summary>Changes one setting (from the web page). Returns an error or null.</summary>
    public string? ChangeSetting(string key, JsonElement value)
    {
        var error = Settings.Set(key, value);
        if (error is null)
        {
            _dirty = true;
            logger.LogInformation("[QuestSync] Setting {Key} = {Value}", key, value.ToString());
            SettingsChanged?.Invoke();
            foreach (var main in entities.Query<MainCharacter>())
                if (_players.TryGetValue(main.PlayerId, out var view))
                    ApplySwitches(main, view);
            RebuildState();
        }
        return error;
    }

    /// <summary>Raised on the game loop after a setting changed.</summary>
    public event Action? SettingsChanged;

    private void RebuildState()
    {
        var players = new List<object>();
        foreach (var (id, view) in _players)
            players.Add(new
            {
                id = id.ToString(),
                name = Nickname(id),
                phase = view.ModIndex is null ? "NoPlugin" : view.Fresh ? "Fresh" : view.CatchingUp ? "CatchingUp" : view.Phase == Phase.Ready && HoldsForIntro(view) ? "Intro" : view.Phase.ToString(),
                intro = view.Intro,
                stages = view.Stages.GroupBy(s => s.Quest).ToDictionary(g => g.Key, g => g.Select(s => s.Stage).Order().ToArray()),
                area = view.Area is { } a ? GameData.AreaKey(a) : null,
                talking = view.InDialogue,
                combat = view.InCombat,
                frozen = view.WantFrozen,
                extra = PlayerExtras?.Invoke(id),
                access = PlayerAccess?.Invoke(id),
            });
        _stateJson = JsonSerializer.Serialize(new
        {
            auto = AutoSync,
            source = SourcePlayer,
            undoRewards = config.UndoReplayRewards,
            settings = Settings,
            categories = CoopSettings.Categories,
            hasText = data.HasText,
            players,
            shared = Shared.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray()),
            reset = ResetQuests.ToArray(),
            sewersLeft = SewersLeft,
            preset = PresetStages.GroupBy(s => s.Quest).ToDictionary(g => g.Key, g => g.Select(s => s.Stage).Order().ToArray()),
            access = AccessInfo?.Invoke(),
        }, CoopSettings.Json);
    }

    private sealed class StateFile
    {
        public Dictionary<string, int[]> Shared { get; set; } = new();
        public bool? AutoSync { get; set; }
        /// <summary>0.4: the one player whose progress was shared.</summary>
        public string? SourcePlayer { get; set; }
        public string[]? ResetQuests { get; set; }
        public CoopSettings? Settings { get; set; }
        public Dictionary<string, ReturnSpot>? Returns { get; set; }
        public Dictionary<string, ReturnSpot>? Outdoors { get; set; }
        public bool? SewersLeft { get; set; }
        public string[]? GotoLocked { get; set; }
        public string[]? NewCharacters { get; set; }
        public string[]? KnownAccounts { get; set; }
        public Dictionary<string, int[]>? PresetStages { get; set; }
    }

    private void LoadState()
    {
        try
        {
            if (!File.Exists(StatePath))
                return;
            var text = File.ReadAllText(StatePath);
            using var doc = JsonDocument.Parse(text);
            StateFile state;
            if (doc.RootElement.TryGetProperty("Shared", out _))
                state = JsonSerializer.Deserialize<StateFile>(text) ?? new();
            else // 0.2 and 0.3 saved only the shared stages
                state = new StateFile { Shared = JsonSerializer.Deserialize<Dictionary<string, int[]>>(text) ?? new() };
            foreach (var (quest, stages) in state.PresetStages ?? new())
                if (Map.TryNormalizeQuest(quest, out var name))
                    foreach (var s in stages)
                        PresetStages.Add(new QuestStage(name, s));
            foreach (var account in state.NewCharacters ?? [])
                NewCharacters.Add(account);
            foreach (var account in state.KnownAccounts ?? [])
                KnownAccounts.Add(account);
            foreach (var (quest, stages) in state.Shared)
                if (Map.TryNormalizeQuest(quest, out var name))
                    foreach (var s in stages)
                        AddShared(new QuestStage(name, s));
            foreach (var quest in state.ResetQuests ?? [])
                ResetQuests.Add(quest);
            foreach (var (player, spot) in state.Returns ?? new())
                Returns[player] = spot;
            foreach (var (player, spot) in state.Outdoors ?? new())
                Outdoors[player] = spot;
            foreach (var player in state.GotoLocked ?? [])
                GotoLocked.Add(player);
            if (state.AutoSync is { } auto)
                AutoSync = auto;
            SewersLeft = state.SewersLeft ?? false;
            if (state.Settings is not null)
                Settings = state.Settings;
            else if (!string.IsNullOrWhiteSpace(state.SourcePlayer))
            {
                Settings.ProgressFrom = "leader";
                Settings.Leader = state.SourcePlayer;
            }
            Settings.Normalize();
            _dirty = false;
            logger.LogInformation("[QuestSync] Loaded shared progress: {Quests} quest(s).", Shared.Count);
        }
        catch (Exception e)
        {
            logger.LogWarning("[QuestSync] Could not read {Path}: {Error}", StatePath, e.Message);
        }
    }

    private void SaveState()
    {
        try
        {
            var state = new StateFile
            {
                Shared = Shared.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray()),
                AutoSync = AutoSync,
                ResetQuests = ResetQuests.ToArray(),
                Settings = Settings,
                Returns = Returns,
                Outdoors = Outdoors,
                SewersLeft = SewersLeft,
                GotoLocked = GotoLocked.ToArray(),
                NewCharacters = NewCharacters.ToArray(),
                KnownAccounts = KnownAccounts.ToArray(),
                PresetStages = PresetStages.GroupBy(s => s.Quest).ToDictionary(g => g.Key, g => g.Select(s => s.Stage).Order().ToArray()),
            };
            var tmp = StatePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, StatePath, overwrite: true);
            _dirty = false;
        }
        catch (Exception e)
        {
            logger.LogWarning("[QuestSync] Could not write {Path}: {Error}", StatePath, e.Message);
        }
    }
}
