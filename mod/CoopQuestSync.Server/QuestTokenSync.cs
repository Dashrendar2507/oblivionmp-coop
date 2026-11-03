using System.Text.Json;
using Microsoft.Extensions.Logging;
using OblivionMp.Sdk.Common.Archetypes;
using OblivionMp.Sdk.Common.Archetypes.Mixins;
using OblivionMp.Sdk.Serverside.Items;
using ReadyM.Api.Idents;
using ReadyM.Relay.Common.Oblivion;
using ReadyM.Relay.Common.Oblivion.ECS.Values;
using ReadyM.Relay.Common.Oblivion.Rpc;
using ReadyM.Relay.Server.Sdk.Ecs;
using ReadyM.Relay.Server.Sdk.Events;
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
/// </summary>
[Service]
public sealed partial class QuestTokenSync(IEntities entities, IServerEvents events, Inventories inventories,
    QuestSyncConfig config, ILogger logger)
{
    private const float BumpEverySeconds = 3f;
    private const int MaxBumps = 10;
    private const float WaitForReplaySeconds = 6f;

    public TokenMap Map { get; } = TokenMap.LoadEmbedded();

    /// <summary>Shared progress: every stage any player has reported (or an admin pushed).</summary>
    public SortedDictionary<string, SortedSet<int>> Shared { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool AutoSync { get; set; }

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
        public Dictionary<uint, int>? Baseline;
        public Dictionary<uint, int> Previous = new();
    }

    private readonly record struct Snapshot(uint? ModIndex, HashSet<QuestStage> Stages, int Busy, int Ack,
        Dictionary<uint, int> Items, List<(uint FormId, int Count)> Legacy);

    public IReadOnlyDictionary<PlayerId, PlayerView> Players => _players;

    private string StatePath => Path.GetFullPath(config.StateFile);

    private void Start()
    {
        AutoSync = config.AutoSync;
        LoadState();
        events.OnPlayerConnected += OnPlayerConnected;
        events.OnPlayerDisconnected += OnPlayerDisconnected;
        logger.LogInformation("[QuestSync] Loaded {Quests} quests / {Stages} stages from the token map. Auto sync is {Auto}. State file: {Path}",
            Map.Quests.Count, Map.StageCount, AutoSync ? "ON" : "OFF", StatePath);
    }

    private void Stop()
    {
        events.OnPlayerConnected -= OnPlayerConnected;
        events.OnPlayerDisconnected -= OnPlayerDisconnected;
        SaveState();
    }

    private void OnPlayerConnected(PlayerId playerId, Entity _) => _players[playerId] = new PlayerView();

    private void OnPlayerDisconnected(PlayerId playerId, Entity _, LiteNetLib.DisconnectReason __) => _players.Remove(playerId);

    private void Update()
    {
        _clock += Time.DeltaTime;
        _elapsed += Time.DeltaTime;
        if (_elapsed < config.IntervalSeconds)
            return;
        _elapsed = 0;

        var newlyDone = new List<(PlayerId From, QuestStage Stage)>();
        var nowReady = new List<MainCharacter>();
        foreach (var main in entities.Query<MainCharacter>())
        {
            var playerId = main.PlayerId;
            if (!_players.TryGetValue(playerId, out var view))
                _players[playerId] = view = new PlayerView();

            var snap = Read(main, view.ModIndex);
            if (snap.ModIndex is null)
            {
                if (!view.WarnedNoPlugin)
                {
                    logger.LogInformation("[QuestSync] {Player}: no CoopQuestSync beacon in inventory yet (plugin not loaded, or still starting).", playerId);
                    view.WarnedNoPlugin = true;
                }
                continue;
            }
            if (view.ModIndex != snap.ModIndex)
            {
                view.ModIndex = snap.ModIndex;
                snap = Read(main, view.ModIndex);
            }

            switch (view.Phase)
            {
                case Phase.NoPlugin:
                    logger.LogInformation("[QuestSync] {Player}: plugin found in load order slot {Slot:X2}, {Count} stage token(s). Starting handshake.",
                        playerId, view.ModIndex, snap.Stages.Count);
                    if (snap.Busy > 0)
                        Take(main, view.ModIndex!.Value << 24 | Map.BusyLocalId, snap.Busy);
                    foreach (var (formId, count) in snap.Legacy)
                    {
                        Take(main, formId, count);
                        logger.LogInformation("[QuestSync] {Player}: removed old test token {FormId:X8}", playerId, formId);
                    }
                    view.AckAtFirstLook = snap.Ack;
                    view.Phase = Phase.Handshake;
                    Bump(main, view);
                    break;

                case Phase.Handshake when snap.Ack > view.AckAtFirstLook:
                    view.Phase = Phase.Restoring;
                    view.AckSeenAt = _clock;
                    break;

                case Phase.Handshake when _clock - view.LastBumpAt > BumpEverySeconds:
                    if (view.Bumps < MaxBumps)
                        Bump(main, view);
                    else
                    {
                        logger.LogWarning("[QuestSync] {Player}: the plugin never answered the handshake. Syncing without restoring.", playerId);
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
                    logger.LogInformation("[QuestSync] {Player}: restoring {Count} stage(s) after joining.", playerId, snap.Stages.Count);
                }
                else if (view.Baseline is not null && snap.Busy == 0)
                {
                    if (config.UndoReplayRewards)
                        Revert(main, view.Baseline, snap.Items);
                    view.Baseline = null;
                    view.Phase = Phase.Ready;
                    nowReady.Add(main);
                    logger.LogInformation("[QuestSync] {Player}: restore finished.", playerId);
                }
                else if (view.Baseline is null && _clock - view.AckSeenAt > WaitForReplaySeconds)
                {
                    view.Phase = Phase.Ready;
                    nowReady.Add(main);
                    logger.LogInformation("[QuestSync] {Player}: nothing to restore.", playerId);
                }
            }

            foreach (var stage in snap.Stages)
                if (!view.Stages.Contains(stage))
                {
                    logger.LogInformation("[QuestSync] {Player}: has {Stage}{Note}", playerId, stage, view.Phase == Phase.Ready ? " (new)" : "");
                    newlyDone.Add((playerId, stage));
                }
            foreach (var stage in view.Stages)
                if (!snap.Stages.Contains(stage))
                    logger.LogInformation("[QuestSync] {Player}: token for {Stage} is GONE", playerId, stage);
            view.Stages = snap.Stages;
            view.Previous = snap.Items;
        }

        foreach (var (from, stage) in newlyDone)
            if (AddShared(stage))
                logger.LogInformation("[QuestSync] Shared progress: {Stage} (first reported by {Player})", stage, from);

        if (AutoSync)
        {
            var targets = nowReady;
            if (newlyDone.Count > 0)
            {
                targets = new List<MainCharacter>();
                foreach (var m in entities.Query<MainCharacter>())
                    if (_players.TryGetValue(m.PlayerId, out var v) && v.Phase == Phase.Ready)
                        targets.Add(m);
            }
            foreach (var main in targets)
            {
                var pushed = PushMissing(main, _players[main.PlayerId]);
                if (pushed > 0)
                    logger.LogInformation("[QuestSync] {Player}: pushed {Count} shared stage(s).", main.PlayerId, pushed);
            }
        }

        if (_dirty)
            SaveState();
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
        int busy = 0, ack = 0;
        var legacy = new List<(uint, int)>();
        foreach (var (formId, count) in raw)
        {
            if (slot is not null && formId >> 24 == slot)
            {
                var local = formId & 0x00FFFFFF;
                if (local == Map.BusyLocalId) busy = count;
                else if (local == Map.AckLocalId) ack = count;
                else if (Map.TryGetStage(local, out var stage)) stages.Add(stage);
                else if (local >= Map.Legacy.Start && local < Map.Legacy.End) legacy.Add((formId, count));
                continue;
            }
            items[formId] = count;
        }
        return new Snapshot(slot, stages, busy, ack, items, legacy);
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
                logger.LogInformation("[QuestSync] {Player}: undo replay reward {FormId:X8} x{Count}", main.PlayerId, formId, diff);
            }
            else if (diff < 0)
            {
                inventories.Add(main.As<Equipment>(), new InventoryEntry(new FormId(formId), -diff, 0));
                logger.LogInformation("[QuestSync] {Player}: give back {FormId:X8} x{Count} that the replay took", main.PlayerId, formId, -diff);
            }
        }
    }

    private void Take(MainCharacter main, uint formId, int count)
    {
        var stack = new ItemStackData
        {
            BaseId = new FormId(formId),
            Count = count,
            Durability = InventoryEntry.NoInstanceOverride,
            Charge = InventoryEntry.NoInstanceOverride,
        };
        if (!inventories.TryTake(main.As<Equipment>(), stack))
            logger.LogWarning("[QuestSync] {Player}: could not take {FormId:X8} x{Count}", main.PlayerId, formId, count);
    }

    public bool AddShared(QuestStage stage)
    {
        if (!Shared.TryGetValue(stage.Quest, out var set))
            Shared[stage.Quest] = set = new SortedSet<int>();
        if (!set.Add(stage.Stage))
            return false;
        _dirty = true;
        return true;
    }

    /// <summary>Gives the player every shared stage they lack. Returns how many tokens were added.</summary>
    public int PushMissing(MainCharacter main, PlayerView view)
    {
        var count = 0;
        foreach (var (quest, set) in Shared)
            foreach (var s in set)
            {
                var stage = new QuestStage(quest, s);
                if (!view.Stages.Contains(stage) && Give(main, view, stage))
                    count++;
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
        if (!Give(main, view, stage))
            return false;
        Bump(main, view);
        return true;
    }

    private bool Give(MainCharacter main, PlayerView view, QuestStage stage)
    {
        if (view.ModIndex is not { } slot || !Map.TryGetToken(stage, out var local))
            return false;
        inventories.Add(main.As<Equipment>(), new InventoryEntry(new FormId(slot << 24 | local), 1, 0));
        view.Stages.Add(stage);
        logger.LogInformation("[QuestSync] {Player}: server gave token for {Stage}", main.PlayerId, stage);
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
        Shared.Clear();
        _dirty = true;
    }

    private void LoadState()
    {
        try
        {
            if (!File.Exists(StatePath))
                return;
            var data = JsonSerializer.Deserialize<Dictionary<string, int[]>>(File.ReadAllText(StatePath)) ?? new();
            foreach (var (quest, stages) in data)
                foreach (var s in stages)
                    AddShared(new QuestStage(quest, s));
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
            var data = Shared.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray());
            File.WriteAllText(StatePath, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
            _dirty = false;
        }
        catch (Exception e)
        {
            logger.LogWarning("[QuestSync] Could not write {Path}: {Error}", StatePath, e.Message);
        }
    }
}
