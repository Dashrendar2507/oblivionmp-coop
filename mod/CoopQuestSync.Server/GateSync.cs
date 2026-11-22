using Microsoft.Extensions.Logging;
using OblivionMP.SDK.Common.Archetypes;
using ReadyM.SDK.Attributes;
using ReadyM.SDK.Server.Entities;

namespace CoopQuestSync.Server;

/// <summary>
/// World state sync, part 2: Oblivion gates. A gate closes only in the game of the player who takes its sigil stone:
/// under OblivionMP it stayed open for the others, a player still inside had to walk out, and the quests that wait on
/// the gate went on only for the closer (Kvatch, 2026-11-16). The plugin watches every gate a player can go through
/// (tokens.json "gates") and gives its CQSGate token when the gate is closed in that player's game. This service
/// remembers the gate as closed and gives its token to every player, now and whenever they join. With the token, the
/// plugin closes the gate in each game: a player inside its realm is sent out through it, the way the closer was, and
/// a player near it in Tamriel sees it close.
/// </summary>
[Service]
public sealed partial class GateSync(IEntities entities, QuestTokenSync sync, ILogger logger)
{
    private float _elapsed;

    private TokenMap Map => sync.Map;

    private void Update()
    {
        _elapsed += Time.DeltaTime;
        if (_elapsed < 1f || Map.Gates.Count == 0)
            return;
        _elapsed = 0;
        var players = new List<(MainCharacter Main, QuestTokenSync.PlayerView View, uint Slot)>();
        foreach (var main in entities.Query<MainCharacter>())
            if (sync.Players.TryGetValue(main.PlayerId, out var view) && view.Phase == QuestTokenSync.Phase.Ready
                && view.ModIndex is { } slot)
                players.Add((main, view, slot));
        foreach (var (main, view, _) in players)
            foreach (var (local, count) in view.Own)
            {
                if (count <= 0 || !Map.Gates.TryGetValue(local, out var gate) || !sync.ClosedGates.Add($"{gate.Ref:X8}"))
                    continue;
                sync.MarkDirty();
                logger.LogInformation("[QuestSync] {Player} closed the Oblivion gate {Gate}: it closes for everyone, and sends out whoever is inside.",
                    sync.Nickname(main.PlayerId), gate.EditorId ?? $"{gate.Ref:X8}");
            }
        if (sync.ClosedGates.Count == 0)
            return;
        foreach (var (local, gate) in Map.Gates)
        {
            if (!sync.ClosedGates.Contains($"{gate.Ref:X8}"))
                continue;
            foreach (var (main, view, slot) in players)
            {
                if (view.Own.GetValueOrDefault(local) > 0)
                    continue;
                sync.GiveOwn(main, view, slot, local);
                logger.LogInformation("[QuestSync] {Player}: the Oblivion gate {Gate} is closed.", sync.Nickname(main.PlayerId),
                    gate.EditorId ?? $"{gate.Ref:X8}");
            }
        }
    }
}
