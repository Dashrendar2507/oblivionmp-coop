using Microsoft.Extensions.Logging;
using OblivionMp.Sdk.Common.Archetypes;
using ReadyM.Api.Idents;
using ReadyM.Relay.Server.Sdk.Ecs;
using ReadyM.Relay.Server.Sdk.Events;
using ReadyM.SDK.Attributes;
using ReadyM.SDK.Server.Entities;

namespace CoopQuestSync.Server;

/// <summary>
/// Every two seconds, reads each player's inventory as the server sees it and logs any plugin item that
/// appeared, changed count or disappeared (every item except runtime-made ones). This answers the first Phase 0 question: does an item that a
/// game script adds to the player reach the server?
/// </summary>
[Service]
public sealed partial class InventoryProbe(IEntities entities, IServerEvents events, ILogger logger)
{
    private const float IntervalSeconds = 2f;

    private readonly Dictionary<PlayerId, Dictionary<uint, int>> _lastSeen = new();
    private float _elapsed;

    private void Start()
    {
        events.OnPlayerConnected += OnPlayerConnected;
        events.OnPlayerDisconnected += OnPlayerDisconnected;
    }

    private void Stop()
    {
        events.OnPlayerConnected -= OnPlayerConnected;
        events.OnPlayerDisconnected -= OnPlayerDisconnected;
    }

    private void OnPlayerConnected(PlayerId playerId, Entity _)
        => logger.LogInformation("[QuestSyncInv] Player {Player} connected.", playerId);

    private void OnPlayerDisconnected(PlayerId playerId, Entity _, LiteNetLib.DisconnectReason __)
    {
        _lastSeen.Remove(playerId);
        logger.LogInformation("[QuestSyncInv] Player {Player} left.", playerId);
    }

    private void Update()
    {
        _elapsed += Time.DeltaTime;
        if (_elapsed < IntervalSeconds)
            return;
        _elapsed = 0;

        foreach (var main in entities.Query<MainCharacter>())
        {
            var now = ReadItems(main, PluginItems.IsPluginForm);
            var playerId = main.PlayerId;

            if (!_lastSeen.TryGetValue(playerId, out var before))
            {
                logger.LogInformation("[QuestSyncInv] {Player}: first look, {Total} stack(s) in inventory, {Plugin} logged item kind(s).",
                    playerId, main.ItemsCount, now.Count);
                foreach (var (formId, count) in now)
                    logger.LogInformation("[QuestSyncInv] {Player}: has {FormId} x{Count}", playerId, PluginItems.Hex(formId), count);
                _lastSeen[playerId] = now;
                continue;
            }

            foreach (var (formId, count) in now)
            {
                if (!before.TryGetValue(formId, out var old))
                    logger.LogInformation("[QuestSyncInv] {Player}: NEW {FormId} x{Count}", playerId, PluginItems.Hex(formId), count);
                else if (old != count)
                    logger.LogInformation("[QuestSyncInv] {Player}: {FormId} x{Old} -> x{Count}", playerId, PluginItems.Hex(formId), old, count);
            }

            foreach (var (formId, old) in before)
                if (!now.ContainsKey(formId))
                    logger.LogInformation("[QuestSyncInv] {Player}: GONE {FormId} (was x{Old})", playerId, PluginItems.Hex(formId), old);

            _lastSeen[playerId] = now;
        }
    }

    internal static Dictionary<uint, int> ReadItems(MainCharacter main, Func<uint, bool> include)
    {
        var items = new Dictionary<uint, int>();
        for (var i = 0; i < main.ItemsCount; i++)
        {
            var entry = main.GetItems(i);
            uint formId = entry.FormId.FormId;
            if (!include(formId))
                continue;
            items[formId] = items.GetValueOrDefault(formId) + entry.Count;
        }
        return items;
    }
}
