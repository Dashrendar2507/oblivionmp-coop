using Microsoft.Extensions.Logging;
using OblivionMP.SDK.Common.Archetypes;
using OblivionMP.SDK.Common.Archetypes.Mixins;
using OblivionMP.SDK.Common.Events;
using ReadyM.Api.Idents;
using ReadyM.SDK.Archetypes.Core;
using ReadyM.SDK.Attributes;
using ReadyM.SDK.Events;
using ReadyM.SDK.Server.Entities;
using ReadyM.SDK.Server.Events;

namespace CoopQuestSync.Server;

/// <summary>
/// Waiting and sleeping, the co-op way (setting <see cref="CoopSettings.Rest"/>).
///
/// OblivionMP 1.2 keeps one clock for the whole server, so one player's wait moves everyone's time. Out of the box it
/// turns the wait menu off. With "vote" (the default) the mod turns the menu back on in every game
/// (<see cref="CoopRpcContracts.RestRule"/>), and a player who confirms it starts a vote: every other player gets a note
/// and has <see cref="CoopSettings.RestVoteSeconds"/> to agree (the join key, or /coop yes). When all agree, the
/// world's clock moves on for everyone. One "no" (/coop no), or silence, and nothing happens. Alone on the server, the
/// wait happens at once. "anyone": any player's wait moves everyone's clock, no vote. "off": the mod stays out of it
/// and OblivionMP's own settings decide (AllowTimeSkip, HideWaitMenu).
///
/// The game's own wait doesn't run under OblivionMP: the menu sends <see cref="TimeSkipRequestEvent"/> and each game
/// follows the server's clock within a second. Refusing the event at Validate keeps OblivionMP's own skip
/// (AllowTimeSkip) from moving the clock as well.
/// </summary>
[Service]
public sealed partial class RestVote(IEntities entities, IServerEvents events, QuestTokenSync sync, CoopRpc rpc, ILogger logger)
{
    private const float TickSeconds = 2f;
    /// <summary>The rule is sent again this often, in case the first one came before the game was ready.</summary>
    private const float ResendSeconds = 30f;

    private sealed class Vote
    {
        public required PlayerId? From;        // null: Ghost (/coop test rest)
        public required string Name;
        public required int Hours;
        public required bool Sleeping;
        public required float Until;
        public required HashSet<PlayerId> Waiting;
        public required int Asked;
        public PlayerId? Ghosted;              // the tester, for /coop test rest
    }

    private Vote? _vote;
    private float _clock;
    private float _elapsed;
    private readonly Dictionary<PlayerId, (string Mode, float At)> _ruleSent = new();

    private void Start()
    {
        rpc.VoteTaken = AcceptByKey;
        events.OnPlayerDisconnected += OnPlayerDisconnected;
    }

    private void Stop()
    {
        rpc.VoteTaken = null;
        events.OnPlayerDisconnected -= OnPlayerDisconnected;
    }

    [EventHandler(Stage.Validate)]
    public void OnTimeSkipRequest(in TimeSkipRequestEvent e, EventContext ctx)
    {
        var mode = sync.Settings.Rest;
        if (mode == "off")
            return;
        ctx.Cancel();
        var from = ctx.Origin;
        var hours = e.Hours;
        if (hours is < 1 or > 24)
        {
            logger.LogWarning("[QuestSync] {Player} asked to wait {Hours} hours: dropped (the menu offers 1 to 24).", sync.Nickname(from), hours);
            return;
        }
        Ask(from, sync.Nickname(from), hours, e.Sleeping, null);
    }

    private void Ask(PlayerId? from, string name, int hours, bool sleeping, PlayerId? ghosted)
    {
        var what = $"{(sleeping ? "sleep" : "wait")} {hours} hour{(hours == 1 ? "" : "s")}";
        if (_vote is { } running)
        {
            if (from is { } f)
                rpc.SendNotice(f, $"{running.Name} already asked the party to {(running.Sleeping ? "sleep" : "wait")}: wait for that vote to end.");
            return;
        }
        var others = new HashSet<PlayerId>();
        foreach (var main in entities.Query<MainCharacter>())
            if (main.PlayerId != from)
                others.Add(main.PlayerId);
        if (sync.Settings.Rest == "anyone" || others.Count == 0)
        {
            Apply(name, hours, sleeping, from is null ? "" : others.Count == 0 ? " (alone on the server)" : " (no vote needed)");
            return;
        }
        var seconds = sync.Settings.RestVoteSeconds;
        _vote = new Vote
        {
            From = from, Name = name, Hours = hours, Sleeping = sleeping, Until = _clock + seconds,
            Waiting = others, Asked = others.Count, Ghosted = ghosted,
        };
        var key = sync.Settings.Key("join");
        foreach (var id in others)
            rpc.SendVote(id, $"{seconds}|{name} wants to {what}. {key} or /coop yes to agree, /coop no to refuse ({{s}} s).");
        if (from is { } asker)
            rpc.SendNotice(asker, others.Count == 1
                ? $"Asking {sync.Nickname(others.First())} to {what} too..."
                : $"Asking the party to {what}: all {others.Count} must agree within {seconds} s.");
        logger.LogInformation("[QuestSync] {Player} wants to {What}: asking {Others}.", name, what, string.Join(", ", others.Select(sync.Nickname)));
    }

    /// <summary>The join key agrees when this player has a vote waiting on them; else the key does its usual job.</summary>
    private bool AcceptByKey(PlayerId player)
    {
        if (_vote is not { } vote || !vote.Waiting.Contains(player))
            return false;
        Answer(player, true);
        return true;
    }

    /// <summary>/coop yes and /coop no. Returns a reply for the chat, or null when the answer was taken.</summary>
    public string? Answer(PlayerId player, bool yes)
    {
        if (_vote is not { } vote || !vote.Waiting.Contains(player))
            return "Nobody asked the party to wait or sleep.";
        var name = sync.Nickname(player);
        rpc.SendVote(player, "");
        if (!yes)
        {
            End($"{name} said no: no {(vote.Sleeping ? "sleep" : "wait")}.");
            logger.LogInformation("[QuestSync] {Player} refused {Asker}'s {Hours} h.", name, vote.Name, vote.Hours);
            return null;
        }
        vote.Waiting.Remove(player);
        logger.LogInformation("[QuestSync] {Player} agrees to {Asker}'s {Hours} h ({Left} left).", name, vote.Name, vote.Hours, vote.Waiting.Count);
        if (vote.Waiting.Count == 0)
        {
            _vote = null;
            Apply(vote.Name, vote.Hours, vote.Sleeping, vote.Asked == 1 ? $" ({name} agreed)" : " (everyone agreed)");
            return null;
        }
        rpc.SendNotice(player, $"You agreed. Waiting for {string.Join(", ", vote.Waiting.Select(sync.Nickname))}.");
        if (vote.From is { } asker)
            rpc.SendNotice(asker, $"{name} agrees ({vote.Asked - vote.Waiting.Count} of {vote.Asked}).");
        return null;
    }

    private void Update()
    {
        _clock += Time.DeltaTime;
        _elapsed += Time.DeltaTime;
        if (_elapsed < TickSeconds)
            return;
        _elapsed = 0;

        // Every game shows the wait menu (or leaves it to OblivionMP's own setting) as the page says.
        var mode = sync.Settings.Rest;
        foreach (var main in entities.Query<MainCharacter>())
        {
            var id = main.PlayerId;
            if (_ruleSent.TryGetValue(id, out var sent) && sent.Mode == mode && _clock - sent.At < ResendSeconds)
                continue;
            rpc.SendRestRule(id, mode);
            _ruleSent[id] = (mode, _clock);
        }

        if (_vote is { } vote && vote.Until < _clock)
        {
            var silent = string.Join(", ", vote.Waiting.Select(sync.Nickname));
            End($"Not everyone agreed in time ({silent}): no {(vote.Sleeping ? "sleep" : "wait")}.");
            logger.LogInformation("[QuestSync] {Asker}'s {Hours} h: no answer from {Silent}.", vote.Name, vote.Hours, silent);
        }
    }

    private void OnPlayerDisconnected(Player gone, Guid _, LiteNetLib.DisconnectReason __)
    {
        var player = gone.PlayerId;
        _ruleSent.Remove(player);
        if (_vote is not { } vote)
            return;
        if (vote.From == player || vote.Ghosted == player)
        {
            End($"{vote.Name} left: no {(vote.Sleeping ? "sleep" : "wait")}.");
            return;
        }
        if (vote.Waiting.Remove(player) && vote.Waiting.Count == 0)
        {
            _vote = null;
            Apply(vote.Name, vote.Hours, vote.Sleeping, " (everyone still here agreed)");
        }
    }

    /// <summary>Calls the vote off: the notes go, and everyone asked (and the asker) is told why.</summary>
    private void End(string why)
    {
        if (_vote is not { } vote)
            return;
        _vote = null;
        foreach (var id in vote.Waiting)
            rpc.SendVote(id, "");
        foreach (var main in entities.Query<MainCharacter>())
            rpc.SendNotice(main.PlayerId, why);
    }

    private void Apply(string name, int hours, bool sleeping, string how)
    {
        if (!entities.World.IsValid)
            return;
        var time = entities.World.As<GameTime>();
        time.Advance(TimeSpan.FromHours(hours));
        var now = time.TimeOfDay;
        var line = $"{name} {(sleeping ? "slept" : "waited")} {hours} hour{(hours == 1 ? "" : "s")}{how}: it's {now:hh\\:mm} for everyone.";
        foreach (var main in entities.Query<MainCharacter>())
            rpc.SendNotice(main.PlayerId, line);
        logger.LogInformation("[QuestSync] {Line} Day {Day}.", line, time.GameDay);
    }

    // ---- Test ----

    /// <summary>/coop test rest [hours]: Ghost asks the party to sleep, so one player can see the vote.</summary>
    public string TestAsk(PlayerId player, int hours)
    {
        if (sync.Settings.Rest == "off")
            return "\"Waiting and sleeping\" is \"OblivionMP's own\" on the page; pick \"Vote\" first.";
        if (_vote is not null)
            return "A vote is already running.";
        var others = new HashSet<PlayerId> { player };
        var seconds = sync.Settings.RestVoteSeconds;
        _vote = new Vote
        {
            From = null, Name = "Ghost", Hours = hours, Sleeping = true, Until = _clock + seconds,
            Waiting = others, Asked = 1, Ghosted = player,
        };
        rpc.SendVote(player, $"{seconds}|Ghost wants to sleep {hours} hours. {sync.Settings.Key("join")} or /coop yes to agree, /coop no to refuse ({{s}} s).");
        return $"Ghost asks you to sleep {hours} hours. Answer with {sync.Settings.Key("join")}, /coop yes or /coop no within {seconds} s, or let it run out. Yes moves the clock for everyone.";
    }

    public string Describe()
        => $"Waiting and sleeping: {sync.Settings.Rest}{(_vote is { } v ? $", vote running ({v.Name}, {v.Hours} h, waiting on {string.Join(", ", v.Waiting.Select(sync.Nickname))})" : "")}.";
}
