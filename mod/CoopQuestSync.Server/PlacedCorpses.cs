using Microsoft.Extensions.Logging;
using OblivionMP.SDK.Common.Archetypes;
using ReadyM.SDK.Attributes;
using ReadyM.SDK.Server.Entities;

namespace CoopQuestSync.Server;

/// <summary>
/// World state sync, part 3: bodies placed dead. Oblivion.esm puts many actors in the world already dead (their base
/// NPC or creature has 0 health): skeletons, bodies, the dead goblin with the lockpicks and the key in the prison's
/// cave. A player's game reports them like any other actor, and the server takes them as alive, so it refuses to let
/// anyone loot them ("Container transfer ... it is not a container or a corpse", 2026-11-17). This service marks
/// them dead on the server once they show up there (the generator lists them, tokens.json "corpses").
/// </summary>
[Service]
public sealed partial class PlacedCorpses(IEntities entities, QuestTokenSync sync, ILogger logger)
{
    /// <summary>Refs already looked at since the server started (a body a script brought back is left alone).</summary>
    private readonly HashSet<uint> _seen = new();
    private float _elapsed;

    private void Update()
    {
        _elapsed += Time.DeltaTime;
        if (_elapsed < 2f || sync.Map.Corpses.Count == 0)
            return;
        _elapsed = 0;
        List<(Npc Actor, string Name)>? marked = null;
        foreach (var npc in entities.Query<Npc>())
        {
            uint refId = npc.RefId;
            if (!sync.Map.Corpses.TryGetValue(refId, out var editorId) || !_seen.Add(refId) || npc.IsDead)
                continue;
            (marked ??= []).Add((npc, editorId ?? $"{refId:X8}"));
        }
        if (marked is null)
            return;
        foreach (var (npc, _) in marked)
        {
            // Dead means a known base health and none left (an actor no game has given health to yet has neither).
            var actor = npc;
            if (actor.HpBase <= 0)
                actor.HpBase = 1;
            actor.Hp = 0;
        }
            logger.LogInformation("[QuestSync] Bodies placed dead in the game, now dead on the server too so they can be looted: {Names}",
                string.Join(", ", marked.Take(12).Select(m => m.Name)) + (marked.Count > 12 ? $", ... ({marked.Count})" : ""));
    }
}
