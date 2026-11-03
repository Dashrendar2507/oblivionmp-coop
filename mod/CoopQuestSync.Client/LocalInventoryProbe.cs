using Microsoft.Extensions.Logging;
using OblivionMp.Sdk;
using ReadyM.SDK.Attributes;

namespace CoopQuestSync.Client;

/// <summary>
/// Every two seconds, logs plugin items in the local player's inventory as this client's SDK sees it.
/// Compared with the server log, it shows where a token gets lost if it does: in the game, or on the way up.
/// </summary>
[Service]
public sealed partial class LocalInventoryProbe(IPlayers players, ILogger logger)
{
    private const float IntervalSeconds = 2f;

    private Dictionary<uint, int>? _lastSeen;
    private float _elapsed;

    private void Update()
    {
        _elapsed += Time.DeltaTime;
        if (_elapsed < IntervalSeconds)
            return;
        _elapsed = 0;

        if (players.LocalMainCharacter is not { } me)
            return;

        var now = new Dictionary<uint, int>();
        for (var i = 0; i < me.ItemsCount; i++)
        {
            var entry = me.GetItems(i);
            uint formId = entry.FormId.FormId;
            var modIndex = formId >> 24;
            if (modIndex == 0xFF || modIndex == 0x00)
                continue;
            now[formId] = now.GetValueOrDefault(formId) + entry.Count;
        }

        if (_lastSeen is null)
        {
            logger.LogInformation("[QuestSyncInv] Local inventory: {Plugin} item kind(s).", now.Count);
            foreach (var (formId, count) in now)
                logger.LogInformation("[QuestSyncInv] Local has {FormId:X8} x{Count}", formId, count);
        }
        else
        {
            foreach (var (formId, count) in now)
                if (!_lastSeen.TryGetValue(formId, out var old) || old != count)
                    logger.LogInformation("[QuestSyncInv] Local {FormId:X8} -> x{Count}", formId, count);
            foreach (var (formId, _) in _lastSeen)
                if (!now.ContainsKey(formId))
                    logger.LogInformation("[QuestSyncInv] Local GONE {FormId:X8}", formId);
        }

        _lastSeen = now;
    }
}
