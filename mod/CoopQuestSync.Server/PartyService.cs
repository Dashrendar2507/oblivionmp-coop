using System.Numerics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OblivionMp.ChatMod.Common;
using OblivionMp.ChatMod.Serverside;
using OblivionMp.Sdk.Common.Archetypes;
using OblivionMp.Sdk.Serverside;
using ReadyM.Api.Idents;
using ReadyM.Relay.Common.Oblivion.ECS.Values;
using ReadyM.Relay.Server.Sdk.Ecs;
using ReadyM.Relay.Server.Sdk.Events;
using ReadyM.SDK.Attributes;
using ReadyM.SDK.Server.Entities;

namespace CoopQuestSync.Server;

/// <summary>
/// Party rules: what happens when a player dies, the tags over players' heads, and level scaling and difficulty.
///
/// Death, with the default "revive": the player stays down. A teammate standing close presses Alt+R to bring them
/// back where they fell. After ReviveSeconds (or when the downed player presses Alt+G) they come back next to the
/// leader, or, when the leader is the one down or is not around, next to the first other player by name.
/// The server can only move a player within their current area, so a player whose party is in another area comes
/// back where they fell.
/// </summary>
[Service]
public sealed partial class PartyService(IEntities entities, IServerEvents events, QuestTokenSync sync, ConversationRelay talks,
    CoopRpc rpc, IChat chat, SessionScaling scaling, SessionDifficulty difficulty, ILogger logger)
{
    private const float TickSeconds = 0.25f;
    private const float MoveAfterResurrectSeconds = 1.5f;
    private const float SpawnOffset = 120f;

    private sealed class Downed
    {
        public float Until;
        public float LastReminder;
    }

    private readonly Dictionary<PlayerId, Downed> _downed = new();
    private readonly Dictionary<PlayerId, bool> _wasDead = new();
    /// <summary>Players just resurrected who should be put next to someone: (when, who to stand by).</summary>
    private readonly Dictionary<PlayerId, (float At, PlayerId Anchor)> _moves = new();
    private string _lastStatus = "";
    private float _clock;
    private float _elapsed;

    private void Start()
    {
        rpc.ReviveRequested += OnReviveRequested;
        rpc.GiveUpRequested += OnGiveUp;
        sync.SettingsChanged += ApplySessionRules;
        sync.PlayerExtras = id => _downed.TryGetValue(id, out var d)
            ? new { downed = true, secondsLeft = (int)Math.Max(0, d.Until - _clock) }
            : null;
        events.OnPlayerDisconnected += OnPlayerDisconnected;
        // The session's rules live on the world entity, which exists once the server has made it.
        events.OnWorldEntityCreated += OnWorldCreated;
    }

    private void OnWorldCreated(Entity _) => ApplySessionRules();

    private void Stop()
    {
        rpc.ReviveRequested -= OnReviveRequested;
        rpc.GiveUpRequested -= OnGiveUp;
        events.OnPlayerDisconnected -= OnPlayerDisconnected;
        events.OnWorldEntityCreated -= OnWorldCreated;
    }

    private void OnPlayerDisconnected(PlayerId player, Entity _, LiteNetLib.DisconnectReason __)
    {
        _downed.Remove(player);
        _wasDead.Remove(player);
        _moves.Remove(player);
    }

    /// <summary>Level scaling and difficulty, from the web page's settings.</summary>
    private void ApplySessionRules()
    {
        var s = sync.Settings;
        try
        {
            switch (s.Scaling)
            {
                case "lowest": scaling.SetToLowestLevel(); break;
                case "highest": scaling.SetToHighestLevel(); break;
                default: scaling.SetToMidLevel(); break; // "default" is the server's own policy, halfway
            }
            var dealt = Enum.TryParse<DifficultyTier>(s.DifficultyDealt, true, out var d) ? d : DifficultyTier.Unset;
            var taken = Enum.TryParse<DifficultyTier>(s.DifficultyTaken, true, out var t) ? t : DifficultyTier.Unset;
            difficulty.Set(dealt, taken);
            logger.LogInformation("[QuestSync] Scaling: {Scaling}. Difficulty dealt/taken: {Dealt}/{Taken}.", s.Scaling, dealt, taken);
        }
        catch (Exception e)
        {
            logger.LogWarning("[QuestSync] Could not apply scaling or difficulty yet: {Error}", e.Message);
        }
    }

    private void Update()
    {
        _clock += Time.DeltaTime;
        _elapsed += Time.DeltaTime;
        if (_elapsed < TickSeconds)
            return;
        _elapsed = 0;

        var settings = sync.Settings;
        foreach (var main in entities.Query<MainCharacter>())
        {
            var id = main.PlayerId;
            var dead = main.IsDead;
            var was = _wasDead.GetValueOrDefault(id);
            _wasDead[id] = dead;

            if (dead && !was)
                OnDied(id, settings);
            else if (!dead && was)
            {
                if (_downed.Remove(id))
                    logger.LogInformation("[QuestSync] {Player} is back on their feet.", sync.Nickname(id));
            }

            if (_downed.TryGetValue(id, out var down))
            {
                if (_clock >= down.Until)
                    Respawn(id, "time is up");
                else if (_clock - down.LastReminder >= 15f)
                {
                    down.LastReminder = _clock;
                    rpc.SendNotice(id, $"You are down. A teammate can revive you (Alt+R next to you). Back in {(int)(down.Until - _clock)} s, or Alt+G to respawn now.");
                }
            }
        }

        // Respawned players: put them next to their anchor once the game has brought them back.
        foreach (var (id, (at, anchor)) in _moves.ToList())
        {
            if (_clock < at)
                continue;
            _moves.Remove(id);
            if (!entities.TryLookup(id, out MainCharacter me) || !entities.TryLookup(anchor, out MainCharacter by))
                continue;
            if (!sync.SameArea(id, anchor))
            {
                rpc.SendNotice(id, $"{sync.Nickname(anchor)} is somewhere else; you are back where you fell.");
                continue;
            }
            me.Position = by.Position + new Vector3(SpawnOffset, 0, 0);
            logger.LogInformation("[QuestSync] {Player} respawned next to {Anchor}.", sync.Nickname(id), sync.Nickname(anchor));
        }

        SendStatus(settings);
    }

    private void OnDied(PlayerId id, CoopSettings settings)
    {
        var name = sync.Nickname(id);
        switch (settings.Death)
        {
            case "off":
                return;
            case "respawn":
                logger.LogInformation("[QuestSync] {Player} died: respawning next to the party.", name);
                Respawn(id, "respawn rule");
                return;
            default:
                _downed[id] = new Downed { Until = _clock + settings.ReviveSeconds, LastReminder = _clock };
                logger.LogInformation("[QuestSync] {Player} is down ({Seconds} s to revive).", name, settings.ReviveSeconds);
                rpc.SendNotice(id, $"You are down. A teammate can revive you (Alt+R next to you). Back in {settings.ReviveSeconds} s, or Alt+G to respawn now.");
                foreach (var (other, _) in sync.Players)
                    if (other != id)
                        chat.SendTo(other, $"{name} is down! Stand next to them and press Alt+R to revive.", ChatLineKind.System);
                return;
        }
    }

    private void OnReviveRequested(PlayerId helper)
    {
        if (!entities.TryLookup(helper, out MainCharacter me) || me.IsDead)
            return;
        var range = sync.Settings.ReviveRange;
        PlayerId? best = null;
        var bestDistance = float.MaxValue;
        foreach (var (id, _) in _downed)
        {
            if (!sync.SameArea(helper, id) || !entities.TryLookup(id, out MainCharacter them))
                continue;
            var d = Vector3.Distance(me.Position, them.Position);
            if (d <= range && d < bestDistance)
            {
                best = id;
                bestDistance = d;
            }
        }
        if (best is not { } target)
        {
            rpc.SendNotice(helper, _downed.Count == 0 ? "Nobody is down." : "Get closer to the downed player to revive them.");
            return;
        }
        _downed.Remove(target);
        rpc.SendResurrect(target);
        rpc.SendNotice(target, $"{sync.Nickname(helper)} revived you.");
        rpc.SendNotice(helper, $"You revived {sync.Nickname(target)}.");
        logger.LogInformation("[QuestSync] {Helper} revived {Player}.", sync.Nickname(helper), sync.Nickname(target));
    }

    private void OnGiveUp(PlayerId id)
    {
        if (_downed.ContainsKey(id))
            Respawn(id, "gave up");
    }

    /// <summary>Brings the player back, then moves them next to the leader (or the first other player by name).</summary>
    private void Respawn(PlayerId id, string why)
    {
        _downed.Remove(id);
        rpc.SendResurrect(id);
        var anchor = Anchor(id);
        if (anchor is { } a)
        {
            _moves[id] = (_clock + MoveAfterResurrectSeconds, a);
            rpc.SendNotice(id, $"Respawning next to {sync.Nickname(a)}.");
        }
        logger.LogInformation("[QuestSync] {Player} respawns ({Why}).", sync.Nickname(id), why);
    }

    /// <summary>The leader when they are alive and not the one respawning, else the first living player by name.</summary>
    private PlayerId? Anchor(PlayerId except)
    {
        var alive = new List<(PlayerId Id, string Name)>();
        foreach (var main in entities.Query<MainCharacter>())
            if (main.PlayerId != except && !main.IsDead && !_downed.ContainsKey(main.PlayerId))
                alive.Add((main.PlayerId, sync.Nickname(main.PlayerId)));
        var leader = sync.Settings.Leader;
        foreach (var (pid, name) in alive)
            if (leader.Length > 0 && name.Equals(leader, StringComparison.OrdinalIgnoreCase))
                return pid;
        return alive.Count == 0 ? null : alive.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).First().Id;
    }

    /// <summary>Tags over heads: who is talking, who is down. Sent to everyone when it changes.</summary>
    private void SendStatus(CoopSettings settings)
    {
        var list = new List<object>();
        if (settings.StatusTags)
            foreach (var (id, _) in sync.Players)
            {
                var talking = talks.IsTalking(id);
                var down = _downed.ContainsKey(id);
                if (talking || down)
                    list.Add(new { id = id.ToString(), name = sync.Nickname(id), talking, downed = down });
            }
        var json = JsonSerializer.Serialize(list);
        if (json == _lastStatus)
            return;
        _lastStatus = json;
        foreach (var (id, _) in sync.Players)
            rpc.SendPartyStatus(id, json);
    }
}
