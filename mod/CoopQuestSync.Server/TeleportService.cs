using Microsoft.Extensions.Logging;
using OblivionMP.SDK.Common.Archetypes;
using ReadyM.Api.Idents;
using ReadyM.SDK.Server.Events;
using ReadyM.SDK.Archetypes.Core;
using ReadyM.SDK.Attributes;
using ReadyM.SDK.Server.Entities;

namespace CoopQuestSync.Server;

/// <summary>
/// Travel to a teammate at any time, not only to a conversation:
/// <list type="bullet">
/// <item><c>/coop goto &lt;name&gt;</c> (the start of a name is enough) travels next to them, into their building or
/// world space too, with <see cref="ReturnOnJoin.Bring(PlayerId, PlayerId)"/>.</item>
/// <item>The goto key (Alt+O by default), or <c>/coop goto</c> alone: to the last teammate picked, else the leader,
/// else the only other player.</item>
/// <item><c>/coop goto lock</c> / <c>unlock</c>: others must ask first. They get "Alice wants to come to you" and
/// accept with the join key (Alt+Y) or <c>/coop accept</c> within <see cref="RequestSeconds"/>. Kept across restarts.</item>
/// </list>
/// Not while down, in a conversation, or (setting) in a fight, and once per <see cref="CoopSettings.TeleportCooldown"/>.
/// </summary>
[Service]
public sealed partial class TeleportService(IEntities entities, IServerEvents events, QuestTokenSync sync, ReturnOnJoin travel,
    CoopRpc rpc, ILogger logger)
{
    private const float RequestSeconds = 20f;

    private readonly Dictionary<PlayerId, PlayerId> _lastPick = new();
    private readonly Dictionary<PlayerId, float> _lastTrip = new();
    /// <summary>Who asked to come to whom (key: the one asked), and until when, oldest first.</summary>
    private readonly Dictionary<PlayerId, List<(PlayerId From, float Until)>> _requests = new();
    /// <summary>Test: players whom Ghost asked to come to (/coop test askme), until when.</summary>
    private readonly Dictionary<PlayerId, float> _ghostAsks = new();
    private float _clock;

    private void Start()
    {
        rpc.GotoRequested += OnGotoKey;
        rpc.JoinTaken = AcceptByKey;
        events.OnPlayerDisconnected += OnPlayerDisconnected;
    }

    private void Stop()
    {
        rpc.GotoRequested -= OnGotoKey;
        rpc.JoinTaken = null;
        events.OnPlayerDisconnected -= OnPlayerDisconnected;
    }

    private void Update()
    {
        _clock += Time.DeltaTime;
        foreach (var (who, until) in _ghostAsks.ToList())
            if (until < _clock)
            {
                _ghostAsks.Remove(who);
                rpc.SendNotice(who, "Test: you didn't answer Ghost (that's a no).");
            }
        foreach (var (to, list) in _requests)
            for (var i = list.Count - 1; i >= 0; i--)
                if (list[i].Until < _clock)
                {
                    rpc.SendNotice(list[i].From, $"{sync.Nickname(to)} didn't answer.");
                    list.RemoveAt(i);
                }
    }

    private void OnPlayerDisconnected(Player gone, Guid _, LiteNetLib.DisconnectReason __)
    {
        var player = gone.PlayerId;
        _lastTrip.Remove(player);
        _lastPick.Remove(player);
        _ghostAsks.Remove(player);
        _requests.Remove(player);
        foreach (var list in _requests.Values)
            list.RemoveAll(r => r.From == player);
        foreach (var (who, to) in _lastPick.ToList())
            if (to == player)
                _lastPick.Remove(who);
    }

    private void OnGotoKey(PlayerId player) => Goto(player, "");

    /// <summary>/coop goto [name | lock | unlock].</summary>
    public void Goto(PlayerId player, string argument)
    {
        var arg = argument.Trim();
        if (arg.Equals("lock", StringComparison.OrdinalIgnoreCase) || arg.Equals("unlock", StringComparison.OrdinalIgnoreCase))
        {
            SetLock(player, arg.Equals("lock", StringComparison.OrdinalIgnoreCase));
            return;
        }
        if (!sync.Settings.TeleportAnytime)
        {
            rpc.SendNotice(player, "Travelling to teammates is turned off on the server's page.");
            return;
        }
        if (!entities.TryLookup(player, out MainCharacter me))
            return;
        if (Pick(player, arg) is not { } to)
            return;
        if (Refusal(player, me) is { } why)
        {
            rpc.SendNotice(player, why);
            return;
        }
        _lastPick[player] = to;
        var them = sync.Nickname(to);
        if (sync.GotoLocked.Contains(them))
        {
            Ask(player, to);
            return;
        }
        Travel(player, to);
    }

    /// <summary>/coop accept [name]: let a teammate who asked come to you.</summary>
    public void Accept(PlayerId player, string name)
    {
        if ((name.Trim().Length == 0 || name.Trim().Equals("Ghost", StringComparison.OrdinalIgnoreCase)) && AcceptGhost(player))
            return;
        Expire(player);
        if (!_requests.TryGetValue(player, out var list) || list.Count == 0)
        {
            rpc.SendNotice(player, "Nobody asked to come to you.");
            return;
        }
        var i = list.Count - 1;
        if (name.Trim().Length > 0)
        {
            i = list.FindLastIndex(r => sync.Nickname(r.From).StartsWith(name.Trim(), StringComparison.OrdinalIgnoreCase));
            if (i < 0)
            {
                rpc.SendNotice(player, $"{name.Trim()} didn't ask to come to you.");
                return;
            }
        }
        var from = list[i].From;
        list.RemoveAt(i);
        logger.LogInformation("[QuestSync] {Player} lets {From} come to them.", sync.Nickname(player), sync.Nickname(from));
        // Checked again: the traveller may have started a fight or a conversation since.
        if (!sync.Settings.TeleportAnytime || !entities.TryLookup(from, out MainCharacter mover))
            return;
        if (Refusal(from, mover) is { } why)
        {
            rpc.SendNotice(from, why);
            rpc.SendNotice(player, $"{sync.Nickname(from)} can't travel right now.");
            return;
        }
        Travel(from, player);
    }

    /// <summary>The join key accepts a request when there is one waiting; else it is the conversation join.</summary>
    private bool AcceptByKey(PlayerId player)
    {
        if (AcceptGhost(player))
            return true;
        Expire(player);
        if (!_requests.TryGetValue(player, out var list) || list.Count == 0)
            return false;
        Accept(player, "");
        return true;
    }

    private void Expire(PlayerId player)
    {
        if (_requests.TryGetValue(player, out var list))
            list.RemoveAll(r => r.Until < _clock);
    }

    private void Ask(PlayerId player, PlayerId to)
    {
        Expire(to);
        if (!_requests.TryGetValue(to, out var list))
            _requests[to] = list = new();
        list.RemoveAll(r => r.From == player);
        list.Add((player, _clock + RequestSeconds));
        var me = sync.Nickname(player);
        var them = sync.Nickname(to);
        rpc.SendNotice(to, $"{me} wants to come to you: {sync.Settings.Key("join")} or /coop accept to let them.");
        rpc.SendNotice(player, $"{them} asks to be asked first: waiting for them to accept.");
        logger.LogInformation("[QuestSync] {Player} asks to travel to {To}.", me, them);
    }

    private void Travel(PlayerId player, PlayerId to)
    {
        var me = sync.Nickname(player);
        var them = sync.Nickname(to);
        var message = travel.Bring(player, to, out var started);
        if (message is not null)
            rpc.SendNotice(player, message);
        if (!started)
            return;
        _lastTrip[player] = _clock;
        rpc.SendNotice(to, $"{me} is coming to you.");
        logger.LogInformation("[QuestSync] {Player} travels to {To} (goto).", me, them);
    }

    /// <summary>Why this player can't travel now, or null.</summary>
    private string? Refusal(PlayerId player, MainCharacter me)
    {
        if (me.IsDead)
            return "You can't travel while you are down.";
        if (me.InDialogue)
            return "Finish your conversation first.";
        if (sync.Settings.TeleportNotInCombat && me.InCombat)
            return "You can't travel during a fight.";
        var cooldown = sync.Settings.TeleportCooldown;
        if (cooldown > 0 && _lastTrip.TryGetValue(player, out var last) && _clock - last < cooldown)
            return $"You can travel again in {Math.Ceiling(cooldown - (_clock - last)):0} s.";
        return null;
    }

    /// <summary>
    /// The teammate to travel to: by name (the start of it is enough), else the last one picked, the leader, or the
    /// only other player. Tells the player when there is none, and returns null.
    /// </summary>
    private PlayerId? Pick(PlayerId player, string name)
    {
        var others = new List<(PlayerId Id, string Name)>();
        foreach (var m in entities.Query<MainCharacter>())
            if (m.PlayerId != player)
                others.Add((m.PlayerId, sync.Nickname(m.PlayerId)));
        others.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));
        if (others.Count == 0)
        {
            rpc.SendNotice(player, "Nobody else is playing right now.");
            return null;
        }
        var list = string.Join(", ", others.Select(o => o.Name));
        if (name.Length > 0)
        {
            var exact = others.Where(o => o.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
            var found = exact.Count > 0 ? exact : others.Where(o => o.Name.StartsWith(name, StringComparison.OrdinalIgnoreCase)).ToList();
            if (found.Count == 1)
                return found[0].Id;
            if (sync.Nickname(player).StartsWith(name, StringComparison.OrdinalIgnoreCase) && found.Count == 0)
                rpc.SendNotice(player, "That's you. Teammates: " + list + ".");
            else
                rpc.SendNotice(player, (found.Count == 0 ? $"Nobody called {name} is playing." : $"More than one name starts with {name}.")
                    + " Teammates: " + list + ".");
            return null;
        }
        if (_lastPick.TryGetValue(player, out var last) && others.Any(o => o.Id == last))
            return last;
        var leader = sync.Settings.Leader;
        if (leader.Length > 0 && others.FirstOrDefault(o => o.Name.Equals(leader, StringComparison.OrdinalIgnoreCase)) is { Name: not null } l)
            return l.Id;
        if (others.Count == 1)
            return others[0].Id;
        rpc.SendNotice(player, $"Who to? Type /coop goto <name> once ({list}); then {sync.Settings.Key("goto")} goes to them.");
        return null;
    }

    private void SetLock(PlayerId player, bool locked)
    {
        var name = sync.Nickname(player);
        var changed = locked ? sync.GotoLocked.Add(name) : sync.GotoLocked.Remove(name);
        if (changed)
            sync.MarkDirty();
        rpc.SendNotice(player, locked
            ? $"Teammates now ask before travelling to you; {sync.Settings.Key("join")} or /coop accept lets them come. /coop goto unlock undoes it."
            : "Teammates can travel to you without asking.");
        if (changed)
            logger.LogInformation("[QuestSync] {Player} {What} travel to them.", name, locked ? "locked" : "unlocked");
    }

    private bool AcceptGhost(PlayerId player)
    {
        if (!_ghostAsks.Remove(player))
            return false;
        rpc.SendNotice(player, "Test: you let Ghost come. A real teammate would now travel to you.");
        return true;
    }

    /// <summary>
    /// Test: /coop test goto. The same checks and cooldown as /coop goto, then the trip to the spot pinned with
    /// /coop test pin, as if Ghost stood there.
    /// </summary>
    public string TestGoto(PlayerId player)
    {
        if (!sync.Settings.TeleportAnytime)
            return "Travelling to teammates is turned off on the server's page.";
        if (!entities.TryLookup(player, out MainCharacter me))
            return "The server doesn't know you yet.";
        if (Refusal(player, me) is { } why)
            return why;
        _lastTrip[player] = _clock;
        return "Travelling to Ghost (your pinned spot). " + travel.TestBack(player);
    }

    /// <summary>Test: /coop test askme. Ghost asks to come to you, as if you had typed /coop goto lock.</summary>
    public string TestAskMe(PlayerId player)
    {
        _ghostAsks[player] = _clock + RequestSeconds;
        rpc.SendNotice(player, $"Ghost wants to come to you: {sync.Settings.Key("join")} or /coop accept to let them.");
        return $"Ghost asked to come to you. Press {sync.Settings.Key("join")} or type /coop accept within {RequestSeconds:0} s, or ignore it.";
    }

    /// <summary>For /coop test status.</summary>
    public string Describe(PlayerId id)
        => $"Goto: {(sync.GotoLocked.Contains(sync.Nickname(id)) ? "locked (others ask)" : "open")}"
           + (_lastTrip.TryGetValue(id, out var last) ? $", last trip {_clock - last:0} s ago." : ".");
}
