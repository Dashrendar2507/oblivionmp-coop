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
    /// <summary>"/coop calm near": the closest NPC within this distance (cm).</summary>
    private const float NearRange = 1500f;
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
        foreach (var (id, view) in sync.Players)
            if (sync.Nickname(id).Equals(target, StringComparison.OrdinalIgnoreCase))
                return view.Area is { } area ? CalmAreas([area], id) : $"The server doesn't know where {sync.Nickname(id)} is yet.";
        return $"No player called {target}. Use a name, \"all\", \"near\", or nothing for your area.";
    }

    /// <summary>Stops the fights in these areas in every game there, and the guards' chase of the players there.</summary>
    private string CalmAreas(IReadOnlyCollection<AreaId> areas, PlayerId? clearBountyOf)
    {
        var refs = new List<uint>();
        // NPCs the server can't place (AreaId.Invalid, seen in a dungeon on 2026-11-07) are stopped too: in a game
        // that doesn't have them, the command just fails.
        foreach (var npc in entities.Query<Npc>())
            if (Fighting(npc) && (areas.Contains(npc.Cell.AreaId) || npc.Cell.AreaId.Equals(AreaId.Invalid)))
                refs.Add(npc.RefId);
        var players = sync.Players.Where(p => p.Value.Area is { } a && areas.Contains(a)).Select(p => p.Key).ToList();
        foreach (var id in players)
        {
            if (refs.Count > 0)
                rpc.SendStopCombat(id, string.Join(' ', refs.Take(MaxRefs).Select(r => r.ToString("X8"))));
            rpc.SendStopChase(id);
            if (clearBountyOf == id)
                rpc.SendClearBounty(id);
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
        Npc? best = null;
        var bestDistance = NearRange;
        foreach (var npc in entities.Query<Npc>())
        {
            if (npc.IsDead || !npc.Cell.AreaId.Equals(area))
                continue;
            var d = Vector3.Distance(npc.Position, me.Position);
            if (d < bestDistance)
            {
                best = npc;
                bestDistance = d;
            }
        }
        if (best is not { } target)
            return "No NPC within 15 m of you.";
        uint refId = target.RefId;
        var name = data.NpcName(target.BaseId.FormId) ?? $"the NPC {refId:X8}";
        foreach (var (id, v) in sync.Players)
            if (v.Area is { } a && a.Equals(area))
                rpc.SendStopCombat(id, refId.ToString("X8"));
        logger.LogInformation("[QuestSync] Calm: {Npc} ({Ref:X8}) near {Player}.", name, refId, sync.Nickname(sender));
        return $"Calmed {name}.";
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
