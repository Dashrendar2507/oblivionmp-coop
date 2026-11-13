using System.Numerics;
using Microsoft.Extensions.Logging;
using OblivionMp.Sdk.Common.Archetypes;
using OblivionMp.Sdk.Common.Archetypes.Mixins;
using ReadyM.Api.Idents;
using ReadyM.Relay.Common.Oblivion.ECS.Values;
using ReadyM.Relay.Server.Sdk.Ecs;
using ReadyM.Relay.Server.Sdk.Events;
using ReadyM.SDK.Attributes;
using ReadyM.SDK.Server.Entities;
using CellScope = ReadyM.SDK.Archetypes.Core.Cell;

namespace CoopQuestSync.Server;

/// <summary>
/// Fights and the way out of them.
///
/// <para><b>Watching.</b> NPCs run in the game of their cell's master. The server sees each NPC's AI step (combat or
/// not) and each player's "in combat" flag, and writes who starts and stops fighting where to the server log, with
/// the master, so a test shows what OblivionMP already shares between the players' games.</para>
///
/// <para><b>Calming.</b> <c>/coop calm</c> (and the web page) stops fights that went wrong, such as a quest giver
/// turned hostile: every player's game in the area runs <c>&lt;ref&gt;.StopCombat</c> on the NPCs that fight there
/// and <c>player.scaonactor</c> (guards stop chasing), through the CoopQuestSyncUI add-on. Calming a player also
/// clears their bounty.</para>
/// </summary>
[Service]
public sealed partial class CombatService(IEntities entities, IServerEvents events, QuestTokenSync sync, GameData data,
    CoopRpc rpc, ILogger logger)
{
    private const float ScanSeconds = 1f;
    /// <summary>"/coop calm near": the NPCs fighting within this distance (cm), and the closest one.</summary>
    private const float NearRange = 1500f;
    /// <summary>
    /// NPCs the server can't place in an area (AreaId.Invalid, as in dungeons, 2026-11-07 and 11-12) count as being in a
    /// player's area when they are this close to that player (cm).
    /// </summary>
    private const float UnplacedRange = 4000f;
    /// <summary>At most this many NPCs per message to one game (the add-on runs one command per NPC).</summary>
    private const int MaxRefs = 64;

    private readonly Dictionary<uint, string> _fighting = new();
    private readonly Dictionary<PlayerId, bool> _playerCombat = new();
    private float _elapsed;

    private void Start() => events.OnPlayerDisconnected += OnPlayerDisconnected;

    private void Stop() => events.OnPlayerDisconnected -= OnPlayerDisconnected;

    private void OnPlayerDisconnected(PlayerId player, Entity _, LiteNetLib.DisconnectReason __) => _playerCombat.Remove(player);

    /// <summary>An NPC is fighting: its AI is on its combat step, or it stands in a combat stance.</summary>
    private static bool Fighting(Npc npc)
        => !npc.IsDead && (npc.Procedure == AiProcedure.Combat || npc.PackageType == AiPackageType.Combat || npc.InCombatStance);

    private void Update()
    {
        _elapsed += Time.DeltaTime;
        if (_elapsed < ScanSeconds)
            return;
        _elapsed = 0;

        foreach (var main in entities.Query<MainCharacter>())
        {
            var id = main.PlayerId;
            var now = main.InCombat;
            var known = _playerCombat.TryGetValue(id, out var was);
            if (known && was == now)
                continue;
            _playerCombat[id] = now;
            if (known || now)
                logger.LogInformation("[QuestSync] Combat: {Player} is {State}.", sync.Nickname(id), now ? "in combat" : "out of combat");
        }

        Dictionary<FullCellId, PlayerId>? masters = null;
        var seen = new HashSet<uint>();
        foreach (var npc in entities.Query<Npc>())
        {
            if (!Fighting(npc))
                continue;
            uint refId = npc.RefId;
            seen.Add(refId);
            if (_fighting.ContainsKey(refId))
                continue;
            masters ??= Masters();
            var name = data.NpcName(npc.BaseId.FormId) ?? $"NPC {(uint)npc.BaseId.FormId:X8}";
            _fighting[refId] = name;
            var master = masters.TryGetValue(npc.Cell, out var m) ? sync.Nickname(m) : "nobody";
            logger.LogInformation("[QuestSync] Combat: {Npc} ({Ref:X8}) fights in {Area}, run by {Master}'s game (AI {Procedure}, aggression {Aggression}).",
                name, refId, GameData.AreaKey(npc.Cell.AreaId), master, npc.Procedure, Aggression(npc));
        }
        foreach (var (refId, name) in _fighting.ToList())
            if (!seen.Contains(refId))
            {
                _fighting.Remove(refId);
                logger.LogInformation("[QuestSync] Combat: {Npc} ({Ref:X8}) stopped fighting.", name, refId);
            }
    }

    private static string Aggression(Npc npc)
    {
        try
        {
            if (!npc.Is<SocialAttributes>())
                return "unknown";
            var key = (byte)CharacterSocialAttribute.Aggression;
            return npc.As<SocialAttributes>().TryGetAttributeValue(in key, out var value) ? value.ToString("0") : "unknown";
        }
        catch
        {
            return "unknown";
        }
    }

    private Dictionary<FullCellId, PlayerId> Masters()
    {
        var map = new Dictionary<FullCellId, PlayerId>();
        foreach (var cell in entities.Query<CellScope>())
            map[cell.FullCellId] = cell.MasterClient;
        return map;
    }

    /// <summary>
    /// /coop calm and the web page. <paramref name="target"/>: null or "" for the area <paramref name="sender"/> is in,
    /// "all" for every area with a player, "near" for the NPC closest to the sender, or a player's name (their area,
    /// and their bounty). Returns what was done, for the chat or the log.
    /// </summary>
    public string Calm(PlayerId? sender, string? target)
    {
        target = target?.Trim();
        if (string.IsNullOrEmpty(target))
        {
            if (sender is not { } me || !sync.Players.TryGetValue(me, out var view) || view.Area is not { } area)
                return "The server doesn't know where you are yet.";
            return CalmAreas([area], null);
        }
        if (target.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            var areas = sync.Players.Values.Select(v => v.Area).OfType<AreaId>().Distinct().ToList();
            return areas.Count == 0 ? "Nobody is in game." : CalmAreas(areas, null);
        }
        if (target.Equals("near", StringComparison.OrdinalIgnoreCase))
            return sender is { } me ? CalmNearest(me) : "Only a player can calm the NPC near them.";
        if (FindPlayer(target) is { } found)
            return sync.Players.TryGetValue(found, out var view) && view.Area is { } area
                ? CalmAreas([area], found)
                : $"The server doesn't know where {sync.Nickname(found)} is yet.";
        return $"No player called {target}. Use a name, \"all\", \"near\", or nothing for your area.";
    }

    /// <summary>
    /// The player a typed name means: the same name ignoring case and a trailing dot or other mark (ReadyM names can
    /// end in one: "dashrendar2507."), else the only player whose name starts with it.
    /// </summary>
    private PlayerId? FindPlayer(string typed)
    {
        static string Plain(string n) => n.Trim().TrimEnd('.', ',', '!', '?', ';', ':').ToLowerInvariant();
        var want = Plain(typed);
        if (want.Length == 0)
            return null;
        var players = sync.Players.Keys.Select(id => (Id: id, Name: Plain(sync.Nickname(id)))).ToList();
        foreach (var (id, name) in players)
            if (name == want)
                return id;
        var starts = players.Where(p => p.Name.StartsWith(want, StringComparison.Ordinal)).ToList();
        return starts.Count == 1 ? starts[0].Id : null;
    }

    /// <summary>Whether <paramref name="npc"/> is in one of <paramref name="areas"/>, or unplaced but close to a player there.</summary>
    private bool InAreas(Npc npc, IReadOnlyCollection<AreaId> areas, List<Vector3> playersThere)
    {
        if (areas.Contains(npc.Cell.AreaId))
            return true;
        if (!npc.Cell.AreaId.Equals(AreaId.Invalid))
            return false;
        var at = npc.Position;
        return playersThere.Any(p => Vector3.Distance(p, at) <= UnplacedRange);
    }

    /// <summary>Stops the fights in these areas in every game there, and the guards' chase of the players there.</summary>
    private string CalmAreas(IReadOnlyCollection<AreaId> areas, PlayerId? clearBountyOf)
    {
        var refs = new List<uint>();
        var players = sync.Players.Where(p => p.Value.Area is { } a && areas.Contains(a)).Select(p => p.Key).ToList();
        var positions = players.Select(id => entities.TryLookup(id, out MainCharacter m) ? m.Position : (Vector3?)null)
            .OfType<Vector3>().ToList();
        // NPCs in dungeons often have no area on the server (AreaId.Invalid): those close to a player there count too.
        // (They were left out after a crash on 2026-11-07, but that crash came from running a console command just as
        // a menu closed, which the add-on no longer does; on 2026-11-12 leaving them out meant /coop calm did nothing.)
        var unplaced = 0;
        foreach (var npc in entities.Query<Npc>())
        {
            if (!Fighting(npc) || !InAreas(npc, areas, positions))
                continue;
            refs.Add(npc.RefId);
            if (npc.Cell.AreaId.Equals(AreaId.Invalid))
                unplaced++;
        }
        if (unplaced > 0)
            logger.LogInformation("[QuestSync] Calm: {Count} of them have no area on the server (a dungeon); taken by distance to the players there.", unplaced);
        foreach (var id in players)
        {
            if (refs.Count > 0)
                rpc.SendStopCombat(id, string.Join(' ', refs.Take(MaxRefs).Select(r => r.ToString("X8"))));
            // The bounty before scaonactor: on 2026-11-13 the game crashed on the SetCrimeGold that came right after
            // scaonactor had dropped the target of an NPC nobody had calmed.
            if (clearBountyOf == id)
                rpc.SendClearBounty(id);
            rpc.SendStopChase(id);
        }
        var bounty = "";
        if (clearBountyOf is { } who)
        {
            bounty = ClearBounty(who) ? $" {sync.Nickname(who)}'s bounty is cleared." : $" {sync.Nickname(who)}'s game clears their bounty.";
            rpc.SendNotice(who, "Your bounty is cleared, and the fighting around you was stopped.");
        }
        var names = string.Join(", ", areas.Select(GameData.AreaKey));
        logger.LogInformation("[QuestSync] Calm: {Count} fighting NPC(s) in {Areas} stopped in {Players} game(s).{Bounty}",
            refs.Count, names, players.Count, bounty);
        return $"Calmed {refs.Count} fighting NPC(s) in {names}; guards stop chasing the {players.Count} player(s) there.{bounty}";
    }

    /// <summary>Stops the closest NPC's fight, whether it fights now or not.</summary>
    private string CalmNearest(PlayerId sender)
    {
        if (!entities.TryLookup(sender, out MainCharacter me) || !sync.Players.TryGetValue(sender, out var view) || view.Area is not { } area)
            return "The server doesn't know where you are yet.";
        // Every NPC fighting within 15 m, plus the closest one even if the server doesn't see it fighting. Calming only
        // the closest wasn't enough (2026-11-12): with two attackers, the other one kept fighting and the calmed one
        // joined back in to help.
        Npc? closest = null;
        var closestDistance = NearRange;
        var picked = new List<Npc>();
        foreach (var npc in entities.Query<Npc>())
        {
            // In the player's area, or with no area at all (dungeon NPCs often have none on the server): the distance decides.
            if (npc.IsDead || !(npc.Cell.AreaId.Equals(area) || npc.Cell.AreaId.Equals(AreaId.Invalid)))
                continue;
            var d = Vector3.Distance(npc.Position, me.Position);
            if (d > NearRange)
                continue;
            if (Fighting(npc))
                picked.Add(npc);
            if (d < closestDistance)
            {
                closest = npc;
                closestDistance = d;
            }
        }
        if (closest is { } c && !picked.Any(n => n.RefId == c.RefId))
            picked.Add(c);
        if (picked.Count == 0)
            return "No NPC within 15 m of you.";
        var refs = string.Join(' ', picked.Take(MaxRefs).Select(n => n.RefId.ToString("X8")));
        foreach (var (id, v) in sync.Players)
            if (v.Area is { } a && a.Equals(area))
                rpc.SendStopCombat(id, refs);
        // StopCombat alone only paused them (2026-11-13: they fought again within seconds): scaonactor makes them
        // stop treating the player as an enemy, as the area and name calms do.
        rpc.SendStopChase(sender);
        var names = picked.Select(n => data.NpcName(n.BaseId.FormId) ?? $"the NPC {n.RefId:X8}").ToList();
        logger.LogInformation("[QuestSync] Calm: {Npcs} ({Refs}) near {Player}.", string.Join(", ", names), refs, sync.Nickname(sender));
        return $"Calmed {string.Join(", ", names)}.";
    }

    /// <summary>Writes a bounty of 0 on the player's character; the player's game also runs player.SetCrimeGold 0.</summary>
    private bool ClearBounty(PlayerId id)
    {
        try
        {
            if (!entities.TryLookup(id, out MainCharacter main) || !main.Is<SocialAttributes>())
                return false;
            var key = (byte)CharacterSocialAttribute.Bounty;
            var zero = 0f;
            main.As<SocialAttributes>().SetAttribute(in key, in zero);
            return true;
        }
        catch (Exception e)
        {
            logger.LogWarning("[QuestSync] Could not clear {Player}'s bounty on the server: {Error}", sync.Nickname(id), e.Message);
            return false;
        }
    }

    /// <summary>For /coop test status: the NPCs fighting now.</summary>
    public string Describe() => _fighting.Count == 0 ? "No NPC is fighting." : "Fighting: " + string.Join(", ", _fighting.Values.Take(8)) + (_fighting.Count > 8 ? "..." : "") + ".";
}
