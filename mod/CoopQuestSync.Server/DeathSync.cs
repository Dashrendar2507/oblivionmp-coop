using Microsoft.Extensions.Logging;
using OblivionMP.SDK.Common.Archetypes;
using ReadyM.SDK.Attributes;
using ReadyM.SDK.Server.Entities;

namespace CoopQuestSync.Server;

/// <summary>
/// World state sync, part 1: deaths by script. Vanilla scripts kill many NPCs with the Kill command (Captain Renault and
/// Glenroy in the prison, quest victims). Under OblivionMP that death stays in the game that ran the script: the server
/// keeps the NPC alive and brings it back in every game (2026-11-17). The plugin watches every actor a vanilla script
/// kills (the generator lists them, tokens.json "deaths") and gives its CQSDead token when that actor dies in the
/// player's game. This service kills the actor on the server, so it is dead for everyone, and takes the token back.
/// </summary>
[Service]
public sealed partial class DeathSync(IEntities entities, QuestTokenSync sync, GameData data, ILogger logger)
{
    private float _elapsed;

    private TokenMap Map => sync.Map;

    private void Update()
    {
        _elapsed += Time.DeltaTime;
        if (_elapsed < 1f || Map.Deaths.Count == 0)
            return;
        _elapsed = 0;
        Dictionary<uint, Npc>? npcs = null;
        foreach (var main in entities.Query<MainCharacter>())
        {
            var id = main.PlayerId;
            if (!sync.Players.TryGetValue(id, out var view) || view.ModIndex is not { } slot)
                continue;
            var reported = view.Own.Where(kv => kv.Value > 0 && Map.Deaths.ContainsKey(kv.Key)).ToList();
            foreach (var (local, count) in reported)
            {
                sync.Take(main, slot << 24 | local, count);
                view.Own[local] = 0;
                var (refId, editorId) = Map.Deaths[local];
                if (npcs is null)
                {
                    npcs = new Dictionary<uint, Npc>();
                    foreach (var npc in entities.Query<Npc>())
                        npcs[npc.RefId] = npc;
                }
                var name = editorId ?? $"{refId:X8}";
                if (!npcs.TryGetValue(refId, out var actor))
                {
                    logger.LogInformation("[QuestSync] {Npc} died in {Player}'s game, but the server doesn't have it (its cell was never loaded here).", name, sync.Nickname(id));
                    continue;
                }
                if (actor.IsDead)
                    continue;
                actor.Hp = 0;
                logger.LogInformation("[QuestSync] {Npc} ({Name}) died in {Player}'s game by script: dead on the server too, for everyone.",
                    name, data.NpcName(actor.BaseId.FormId) ?? "?", sync.Nickname(id));
            }
        }
    }
}
