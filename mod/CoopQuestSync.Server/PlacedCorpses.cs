using Microsoft.Extensions.Logging;
using OblivionMP.SDK.Common.Archetypes;
using OblivionMP.SDK.Common.Events;
using ReadyM.Relay.Common.Oblivion.Rpc;
using ReadyM.SDK.Attributes;
using ReadyM.SDK.Events;
using ReadyM.SDK.Server.Entities;

namespace CoopQuestSync.Server;

/// <summary>
/// World state sync, part 3: bodies placed dead. Oblivion.esm puts many actors in the world already dead (their base
/// NPC or creature has 0 health): skeletons, bodies, the dead goblin with the lockpicks and the key in the prison's
/// cave. A player's game reports them like any other actor, and the server takes them as alive, so it refuses to let
/// anyone loot them ("Container transfer ... it is not a container or a corpse", 2026-11-17). This service marks
/// them dead on the server once they show up there (the generator lists them, tokens.json "corpses").
/// <para>
/// The server's corpse test is "base health above 0, health 0 or less". These bodies' base health is 0 in the game,
/// and that is what the owner's game reports. Marked once (0.9.2), the body was set back by the next report and the
/// loot was still refused (2026-11-17). So a listed body with no base health is marked again every second, and right
/// before each loot: the server tests for a corpse when it applies the move, after every mod's Validate.
/// </para>
/// </summary>
[Service]
public sealed partial class PlacedCorpses(IEntities entities, QuestTokenSync sync, ILogger logger)
{
    /// <summary>Refs already looked at since the server started (a body a script brought back is left alone).</summary>
    private readonly HashSet<uint> _seen = new();
    /// <summary>Refs a game set back to no base health at least once (logged once each).</summary>
    private readonly HashSet<uint> _reverted = new();
    private float _elapsed;

    [EventHandler(Stage.Validate)]
    public void BeforeLoot(in ItemTransferEvent e, EventContext ctx)
    {
        if (sync.Map.Corpses.Count == 0)
            return;
        MarkBody(e.From);
        MarkBody(e.To);
    }

    private void MarkBody(ItemHolder holder)
    {
        if (holder.Kind != ItemHolderKind.Actor || !sync.Map.Corpses.ContainsKey(holder.RefId))
            return;
        var found = new List<string>();
        var mark = new List<Npc>();
        foreach (var npc in entities.Query<Npc>())
        {
            if (npc.RefId != holder.RefId)
                continue;
            found.Add($"hp {npc.Hp}/{npc.HpBase}{(npc.IsDead ? " dead" : "")}");
            if (!npc.IsDead && (npc.HpBase <= 0 || !_seen.Contains(holder.RefId)))
                mark.Add(npc);
        }
        foreach (var npc in mark)
        {
            var actor = npc;
            if (actor.HpBase <= 0)
                actor.HpBase = 1;
            actor.Hp = 0;
        }
        _seen.Add(holder.RefId);
        // Once per body: what the server had when someone first looted it (a test shows why a loot is refused).
        if (_looted.Add(holder.RefId))
            logger.LogInformation("[QuestSync] Looting body {Ref:X8}: the server had {Count} entit(ies) for it ({States}){Marked}.",
                holder.RefId, found.Count, string.Join("; ", found), mark.Count > 0 ? $", marked {mark.Count} dead now" : "");
    }

    /// <summary>Bodies someone tried to loot since the server started (logged once each).</summary>
    private readonly HashSet<uint> _looted = new();

    private void Update()
    {
        _elapsed += Time.DeltaTime;
        if (_elapsed < 1f || sync.Map.Corpses.Count == 0)
            return;
        _elapsed = 0;
        List<Npc>? mark = null;
        List<string>? named = null;
        foreach (var npc in entities.Query<Npc>())
        {
            uint refId = npc.RefId;
            if (!sync.Map.Corpses.TryGetValue(refId, out var editorId) || npc.IsDead)
                continue;
            var name = editorId ?? $"{refId:X8}";
            if (_seen.Add(refId))
                (named ??= []).Add(name);
            // After the first look, only a body with no base health again: a game's report of the dead body. A living
            // actor (one a script brought back) has a base health.
            else if (npc.HpBase > 0)
                continue;
            else if (_reverted.Add(refId))
                logger.LogInformation("[QuestSync] {Body}: a game reported it with no health at all again. Marking it dead again, each time.", name);
            (mark ??= []).Add(npc);
        }
        if (mark is null)
            return;
        foreach (var npc in mark)
        {
            var actor = npc;
            if (actor.HpBase <= 0)
                actor.HpBase = 1;
            actor.Hp = 0;
        }
        if (named is not null)
            logger.LogInformation("[QuestSync] Bodies placed dead in the game, now dead on the server too so they can be looted: {Names}",
                string.Join(", ", named.Take(12)) + (named.Count > 12 ? $", ... ({named.Count})" : ""));
    }
}
