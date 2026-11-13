using System.Numerics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OblivionMp.ChatMod.Common;
using OblivionMp.ChatMod.Serverside;
using OblivionMP.SDK.Common.Archetypes;
using OblivionMP.SDK.Serverside;
using ReadyM.Api.Idents;
using ReadyM.Relay.Common.Oblivion.ECS.Values;
using ReadyM.SDK.Server.Events;
using ReadyM.SDK.Archetypes.Core;
using ReadyM.SDK.Attributes;
using ReadyM.SDK.Server.Entities;

namespace CoopQuestSync.Server;

/// <summary>
/// Party rules: what happens when a player dies, the tags over players' heads, and level scaling and difficulty.
///
/// Death, with the default "revive": the player stays down. A teammate standing close presses the revive key to bring them
/// back next to them. After ReviveSeconds (or when the downed player presses the respawn key) they come back next to the
/// leader, or, when the leader is the one down or is not around, next to the first other player by name, in any area;
/// with nobody else up, on their last safe spot.
/// Coming back to life is OblivionMP's "rebirth", which reloads the player's own save: <see cref="ReturnOnJoin.AfterRebirth"/>
/// takes them where they belong once that reload is over, behind the travel screen.
/// </summary>
[Service]
public sealed partial class PartyService(IEntities entities, IServerEvents events, QuestTokenSync sync, ConversationRelay talks,
    CoopRpc rpc, IChat chat, SessionScaling scaling, SessionDifficulty difficulty, ReturnOnJoin travel, ILogger logger)
{
    private const float TickSeconds = 0.25f;

    private sealed class Downed
    {
        public float Until;
        public float LastReminder;
    }

    private readonly Dictionary<PlayerId, Downed> _downed = new();
    private readonly Dictionary<PlayerId, bool> _wasDead = new();
    private readonly HashSet<PlayerId> _downNoted = new();

    /// <summary>
    /// Where each player recently stood on solid ground, in their current area, so a respawn can avoid the place
    /// they died (the bottom of a cliff, under the map, in deep water).
    /// </summary>
    private sealed class Trail
    {
        public AreaId? Area;
        public Vector3? Entry;
        public readonly List<(float At, Vector3 Pos)> Samples = new();
        public float LastSample;
    }

    private const float TrailEverySeconds = 1f;
    private const float TrailKeepSeconds = 120f;
    /// <summary>A respawn uses a spot from at least this long before the death, so the fall itself is skipped.</summary>
    private const float SafeLookbackSeconds = 6f;
    /// <summary>Faster than this up or down (cm/s) counts as falling or jumping, not standing.</summary>
    private const float GroundedSpeed = 80f;

    private readonly Dictionary<PlayerId, Trail> _trails = new();
    private readonly Dictionary<PlayerId, float> _diedAt = new();
    private string _lastStatus = "";
    private readonly Dictionary<PlayerId, (string Json, float At)> _keysSent = new();
    private float _clock;
    private float _elapsed;

    private void Start()
    {
        rpc.ReviveRequested += OnReviveRequested;
        rpc.GiveUpRequested += OnGiveUp;
        sync.SettingsChanged += ApplySessionRules;
        sync.PlayerExtras = id =>
        {
            var doing = talks.Activity(id);
            if (_downed.TryGetValue(id, out var d))
                return new { downed = true, secondsLeft = (int)Math.Max(0, d.Until - _clock), activity = doing };
            return doing is null ? null : new { downed = false, secondsLeft = 0, activity = doing };
        };
        events.OnPlayerDisconnected += OnPlayerDisconnected;
        // The session's rules live on the world entity, which exists once the server has made it.
        events.OnWorldCreated += OnWorldCreated;
    }

    private void OnWorldCreated(World _) => ApplySessionRules();

    private void Stop()
    {
        rpc.ReviveRequested -= OnReviveRequested;
        rpc.GiveUpRequested -= OnGiveUp;
        events.OnPlayerDisconnected -= OnPlayerDisconnected;
        events.OnWorldCreated -= OnWorldCreated;
    }

    private void OnPlayerDisconnected(Player gone, Guid _, LiteNetLib.DisconnectReason __)
    {
        var player = gone.PlayerId;
        _downed.Remove(player);
        _wasDead.Remove(player);
        _downNoted.Remove(player);
        _trails.Remove(player);
        _diedAt.Remove(player);
        _keysSent.Remove(player);
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
        var keys = JsonSerializer.Serialize(new { follow = settings.KeyFollow, join = settings.KeyJoin, revive = settings.KeyRevive, respawn = settings.KeyRespawn, @goto = settings.KeyGoto });
        foreach (var main in entities.Query<MainCharacter>())
        {
            var id = main.PlayerId;
            // Each game binds the keys the page sets; sent again now and then, in case the first one came too early.
            if (!_keysSent.TryGetValue(id, out var sent) || sent.Json != keys || _clock - sent.At > 30f)
            {
                rpc.SendKeys(id, keys);
                _keysSent[id] = (keys, _clock);
            }
            var dead = main.IsDead;
            var was = _wasDead.GetValueOrDefault(id);
            _wasDead[id] = dead;

            if (!dead && !_downed.ContainsKey(id))
                Remember(main);
            if (dead && !was)
            {
                _diedAt[id] = _clock;
                OnDied(id, settings);
            }
            else if (!dead && was)
            {
                if (_downed.Remove(id))
                {
                    // Up while still down for us: OblivionMP's own rebirth (R, or on its own after a while, 2026-11-13),
                    // which reloads the player's save. Take them back as a respawn would, once that reload is over.
                    var anchor = Anchor(id);
                    var area = _trails.TryGetValue(id, out var trail) ? trail.Area : null;
                    travel.AfterRebirth(id, anchor, area, SafeSpot(id),
                        anchor is { } a ? $"Back next to {sync.Nickname(a)}..." : "Back where you fell...");
                    logger.LogInformation("[QuestSync] {Player} came back through OblivionMP's own rebirth; taking them back after its reload.", sync.Nickname(id));
                }
            }

            // The down note on the player's screen: up while down, gone once back up (or brought back).
            var noted = _downNoted.Contains(id);
            if (_downed.ContainsKey(id) != noted)
            {
                if (noted)
                {
                    _downNoted.Remove(id);
                    rpc.SendDown(id, "");
                }
                else if (_downed.TryGetValue(id, out var d))
                {
                    _downNoted.Add(id);
                    rpc.SendDown(id, $"{(int)Math.Max(0, d.Until - _clock)}|You are down. A teammate next to you can revive you ({settings.Key("revive")}). "
                        + $"{settings.Key("respawn")}: back with the party now, or in {{s}} s. Don't press R, even in the chat: that's OblivionMP's own rebirth.");
                }
            }

            if (_downed.TryGetValue(id, out var down))
            {
                if (_clock >= down.Until)
                    Respawn(id, "time is up");
            }
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
                rpc.SendNotice(id, $"You are down. A teammate can revive you ({settings.Key("revive")} next to you). Back in {settings.ReviveSeconds} s, or {settings.Key("respawn")} to respawn now. Don't press R: that's OblivionMP's own rebirth, which reloads your save.");
                foreach (var (other, _) in sync.Players)
                    if (other != id)
                        chat.SendTo(other, $"{name} is down! Stand next to them and press {settings.Key("revive")} to revive.", ChatLineKind.System);
                return;
        }
    }

    /// <summary>/coop revive: the same as the revive key.</summary>
    public void ReviveNearest(PlayerId helper) => OnReviveRequested(helper);

    /// <summary>/coop giveup: the same as the respawn key.</summary>
    public void GiveUp(PlayerId id) => OnGiveUp(id);

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
        // OblivionMP's rebirth reloads the player's save: they come back next to whoever revived them.
        travel.AfterRebirth(target, helper, null, null, $"{sync.Nickname(helper)} revived you...");
        rpc.SendResurrect(target);
        rpc.SendNotice(target, $"{sync.Nickname(helper)} revived you.");
        rpc.SendNotice(helper, $"You revived {sync.Nickname(target)}.");
        logger.LogInformation("[QuestSync] {Helper} revived {Player}.", sync.Nickname(helper), sync.Nickname(target));
    }

    /// <summary>Test: a made-up teammate revives the player.</summary>
    public bool TestRevive(PlayerId id, string helper)
    {
        if (!_downed.Remove(id))
            return false;
        // Ghost stands where you fell: back there after the rebirth's reload.
        var area = sync.Players.TryGetValue(id, out var view) ? view.Area : null;
        Vector3? fell = entities.TryLookup(id, out MainCharacter me) ? me.Position : null;
        travel.AfterRebirth(id, null, area, fell, $"{helper} revived you...");
        rpc.SendResurrect(id);
        rpc.SendNotice(id, $"{helper} revived you.");
        logger.LogInformation("[QuestSync] Test: {Helper} revived {Player}.", helper, sync.Nickname(id));
        return true;
    }

    /// <summary>Test: the "Talking"/"Down" tag data as the other players would receive it, shown to the player.</summary>
    public string StatusJson => _lastStatus;

    private void OnGiveUp(PlayerId id)
    {
        if (_downed.ContainsKey(id))
            Respawn(id, "gave up");
    }

    /// <summary>
    /// Brings the player back, then next to the leader (or the first other player by name), in any area; with nobody
    /// else up, on their last safe spot where they died. OblivionMP's rebirth reloads the player's save first, so the
    /// trip happens after that reload (see <see cref="ReturnOnJoin.AfterRebirth"/>).
    /// </summary>
    private void Respawn(PlayerId id, string why)
    {
        _downed.Remove(id);
        var anchor = Anchor(id);
        var area = _trails.TryGetValue(id, out var trail) ? trail.Area : null;
        travel.AfterRebirth(id, anchor, area, SafeSpot(id),
            anchor is { } a ? $"Respawning next to {sync.Nickname(a)}..." : "Respawning...");
        rpc.SendResurrect(id);
        logger.LogInformation("[QuestSync] {Player} respawns ({Why}).", sync.Nickname(id), why);
    }

    /// <summary>Records where the player stands, every second, when they are on the ground in their area.</summary>
    private void Remember(MainCharacter main)
    {
        var id = main.PlayerId;
        if (!_trails.TryGetValue(id, out var trail))
            _trails[id] = trail = new Trail();
        var area = sync.Players.TryGetValue(id, out var view) ? view.Area : null;
        if (area is null)
            return;
        if (trail.Area is not { } known || !known.Equals(area.Value))
        {
            // A new area (through a door, or fast travel): start over; the first good spot is the way in.
            trail.Area = area;
            trail.Entry = null;
            trail.Samples.Clear();
        }
        if (_clock - trail.LastSample < TrailEverySeconds)
            return;
        if (MathF.Abs(main.VerticalSpeed) > GroundedSpeed || main.Hp <= 0)
            return;
        trail.LastSample = _clock;
        var pos = main.Position;
        trail.Entry ??= pos;
        trail.Samples.Add((_clock, pos));
        trail.Samples.RemoveAll(s => _clock - s.At > TrailKeepSeconds);
    }

    /// <summary>The last spot the player stood on safely, from a few seconds before they died; else where they entered the area.</summary>
    private Vector3? SafeSpot(PlayerId id)
    {
        if (!_trails.TryGetValue(id, out var trail))
            return null;
        var died = _diedAt.GetValueOrDefault(id, _clock);
        for (var i = trail.Samples.Count - 1; i >= 0; i--)
            if (trail.Samples[i].At <= died - SafeLookbackSeconds)
                return trail.Samples[i].Pos;
        return trail.Entry;
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

    /// <summary>Tags over heads: who is talking (and in which menu), who is down. Sent to everyone when it changes.</summary>
    private void SendStatus(CoopSettings settings)
    {
        var list = new List<object>();
        if (settings.StatusTags)
            foreach (var (id, _) in sync.Players)
            {
                var talking = talks.IsTalking(id);
                var down = _downed.ContainsKey(id);
                var activity = talks.Activity(id);
                if (talking || down || activity is not null)
                    list.Add(new { id = id.ToString(), name = sync.Nickname(id), talking, downed = down, activity });
            }
        var json = JsonSerializer.Serialize(list);
        if (json == _lastStatus)
            return;
        _lastStatus = json;
        foreach (var (id, _) in sync.Players)
            rpc.SendPartyStatus(id, json);
    }
}
