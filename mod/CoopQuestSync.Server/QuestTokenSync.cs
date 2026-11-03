using System.Text.Json;
using Microsoft.Extensions.Logging;
using OblivionMp.Sdk.Common.Archetypes;
using OblivionMp.Sdk.Common.Archetypes.Mixins;
using OblivionMp.Sdk.Serverside.Items;
using ReadyM.Api.Idents;
using ReadyM.Relay.Common.Oblivion;
using ReadyM.Relay.Common.Oblivion.ECS.Values;
using ReadyM.Relay.Server.Sdk.Ecs;
using ReadyM.Relay.Server.Sdk.Events;
using ReadyM.SDK.Attributes;
using ReadyM.SDK.Server.Entities;

namespace CoopQuestSync.Server;

/// <summary>
/// Reads the quest tokens the plugin puts in each player's inventory, keeps the shared progress, and pushes
/// tokens back down so the plugin sets the same stages in the other players' games.
/// </summary>
[Service]
public sealed partial class QuestTokenSync(IEntities entities, IServerEvents events, Inventories inventories,
    QuestSyncConfig config, ILogger logger)
{
    public TokenMap Map { get; } = TokenMap.LoadEmbedded();

    /// <summary>Shared progress: every stage any player has reported (or an admin pushed).</summary>
    public SortedDictionary<string, SortedSet<int>> Shared { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool AutoSync { get; set; }

    private readonly Dictionary<PlayerId, PlayerView> _players = new();
    private float _elapsed;
    private bool _dirty;

    public sealed class PlayerView
    {
        public uint? ModIndex;
        public HashSet<QuestStage> Stages = new();
        public bool Greeted;
        public bool WarnedNoPlugin;
    }

    public IReadOnlyDictionary<PlayerId, PlayerView> Players => _players;

    private string StatePath => Path.IsPathRooted(config.StateFile)
        ? config.StateFile
        : Path.Combine(AppContext.BaseDirectory, config.StateFile);

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
        _elapsed += Time.DeltaTime;
        if (_elapsed < config.IntervalSeconds)
            return;
        _elapsed = 0;

        var newlyDone = new List<(PlayerId From, QuestStage Stage)>();
        foreach (var main in entities.Query<MainCharacter>())
        {
            var playerId = main.PlayerId;
            if (!_players.TryGetValue(playerId, out var view))
                _players[playerId] = view = new PlayerView();

            var (modIndex, stages) = ReadTokens(main);
            if (modIndex is null)
            {
                if (!view.WarnedNoPlugin)
                {
                    logger.LogInformation("[QuestSync] {Player}: no CoopQuestSync beacon in inventory yet (plugin not loaded, or still starting).", playerId);
                    view.WarnedNoPlugin = true;
                }
                continue;
            }
            if (view.ModIndex != modIndex)
            {
                logger.LogInformation("[QuestSync] {Player}: plugin found in load order slot {Slot:X2}, {Count} stage token(s).",
                    playerId, modIndex, stages.Count);
                view.ModIndex = modIndex;
            }

            foreach (var stage in stages)
                if (!view.Stages.Contains(stage))
                {
                    logger.LogInformation("[QuestSync] {Player}: has {Stage}{Note}", playerId, stage, view.Greeted ? " (new)" : "");
                    newlyDone.Add((playerId, stage));
                }
            foreach (var stage in view.Stages)
                if (!stages.Contains(stage))
                    logger.LogInformation("[QuestSync] {Player}: token for {Stage} is GONE", playerId, stage);
            view.Stages = stages;

            if (!view.Greeted)
            {
                view.Greeted = true;
                if (AutoSync)
                {
                    var pushed = PushMissing(main, view);
                    if (pushed > 0)
                        logger.LogInformation("[QuestSync] {Player}: joined, pushed {Count} shared stage(s) to catch up.", playerId, pushed);
                }
            }
        }

        foreach (var (from, stage) in newlyDone)
            if (AddShared(stage))
                logger.LogInformation("[QuestSync] Shared progress: {Stage} (first reported by {Player})", stage, from);

        if (AutoSync && newlyDone.Count > 0)
            foreach (var main in entities.Query<MainCharacter>())
                if (_players.TryGetValue(main.PlayerId, out var view) && view.ModIndex is not null)
                {
                    var pushed = PushMissing(main, view);
                    if (pushed > 0)
                        logger.LogInformation("[QuestSync] {Player}: pushed {Count} stage(s).", main.PlayerId, pushed);
                }

        if (_dirty)
            SaveState();
    }

    /// <summary>Finds the plugin's slot from the beacon, then every stage token the player carries.</summary>
    private (uint? ModIndex, HashSet<QuestStage> Stages) ReadTokens(MainCharacter main)
    {
        uint? modIndex = null;
        var ids = new List<uint>(main.ItemsCount);
        for (var i = 0; i < main.ItemsCount; i++)
        {
            var entry = main.GetItems(i);
            uint formId = entry.FormId.FormId;
            if (entry.Count <= 0)
                continue;
            ids.Add(formId);
            if ((formId & 0x00FFFFFF) == Map.BeaconLocalId && (formId >> 24) is not 0x00 and not 0xFF)
                modIndex = formId >> 24;
        }
        var stages = new HashSet<QuestStage>();
        if (modIndex is null)
            return (null, stages);
        foreach (var formId in ids)
            if (formId >> 24 == modIndex && Map.TryGetStage(formId & 0x00FFFFFF, out var stage))
                stages.Add(stage);
        return (modIndex, stages);
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
            BumpSeq(main, view);
        return count;
    }

    /// <summary>Gives one stage token to one player and wakes the plugin. Returns false if the player has no plugin.</summary>
    public bool PushOne(MainCharacter main, QuestStage stage)
    {
        if (!_players.TryGetValue(main.PlayerId, out var view) || view.ModIndex is null)
            return false;
        if (!Give(main, view, stage))
            return false;
        BumpSeq(main, view);
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

    /// <summary>The plugin's quest scripts rescan when this count changes, so pushed tokens are noticed at once.</summary>
    private void BumpSeq(MainCharacter main, PlayerView view)
    {
        if (view.ModIndex is { } slot)
            inventories.Add(main.As<Equipment>(), new InventoryEntry(new FormId(slot << 24 | Map.SeqLocalId), 1, 0));
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
