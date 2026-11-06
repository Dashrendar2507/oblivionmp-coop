using System.Numerics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OblivionMp.Sdk.Common.Archetypes;
using ReadyM.Api.Idents;
using ReadyM.Relay.Server.Sdk.Ecs;
using ReadyM.Relay.Server.Sdk.Events;
using ReadyM.SDK.Attributes;
using ReadyM.SDK.Server.Entities;

namespace CoopQuestSync.Server;

/// <summary>
/// Lets the other players follow a conversation.
///
/// OblivionMP says when a player is in a conversation (InDialogue). CoopQuestSyncDialogue.esp adds a line token to
/// the talker's inventory each time a topic line is spoken; the server takes it back at once and sends the line to
/// the witnesses, whose game shows it in an overlay styled like the dialogue screen. Witnesses only watch: the
/// overlay takes no input, and the topic list is never theirs.
///
/// Witnesses in the same area are frozen (the plugin turns their controls off while the server holds CQSFreeze in
/// their inventory), so nobody fights or wanders off beside a conversation that the game thinks is private.
/// A witness who is in combat is never frozen.
///
/// With "Follow conversations" set to "ask", nobody is pulled in: players who could watch get a prompt and press
/// the follow key (Alt+L by default) to listen in. Either way, that key while watching leaves the conversation.
///
/// The dialogue plugin also keeps a token in the inventory while a menu is open (barter, persuasion, training...,
/// lockpicking): the witnesses' overlay then says "Alice is bartering" and waits, and the tag over the talker says so.
/// </summary>
[Service]
public sealed partial class ConversationRelay(IEntities entities, IServerEvents events, QuestTokenSync sync, GameData data,
    CoopRpc rpc, ILogger logger)
{
    private const float ScanSeconds = 0.25f;
    private const float NpcSearchRange = 600f;

    private sealed class Talk
    {
        public string Npc = "";
        public readonly HashSet<PlayerId> Witnesses = new();
        public readonly HashSet<PlayerId> Frozen = new();
        /// <summary>Players who may listen in with the follow key (asked, or who left and may come back).</summary>
        public readonly HashSet<PlayerId> Invited = new();
        /// <summary>The last few lines, for someone who starts listening halfway.</summary>
        public readonly List<string> Recent = new();
    }

    private const int RecentLines = 3;

    private readonly Dictionary<PlayerId, Talk> _talks = new();
    private readonly Dictionary<PlayerId, uint> _dialogueSlot = new();
    private readonly Dictionary<PlayerId, string> _activity = new();

    /// <summary>What a player is doing in each menu the dialogue plugin reports, as the others read it.</summary>
    private static readonly Dictionary<string, string> Activities = new()
    {
        ["barter"] = "bartering", ["persuade"] = "persuading", ["training"] = "training", ["repair"] = "repairing",
        ["spells"] = "buying spells", ["recharge"] = "recharging", ["spellmaking"] = "making a spell",
        ["enchanting"] = "enchanting", ["lockpick"] = "picking a lock",
    };
    private float _elapsed;
    private float _clock;

    /// <summary>A pretend conversation shown to one player, for testing alone (/coop test talk).</summary>
    private sealed class Ghost
    {
        public string Name = "";
        public string Npc = "";
        public bool Frozen;
        public bool Watching;
        public readonly List<string> Recent = new();
        public string? Activity;
        /// <summary>Lines, and menus the ghost "opens" (Activity true), in order.</summary>
        public readonly Queue<(float At, bool Activity, string Json)> Lines = new();
        public float EndsAt;
    }

    private readonly Dictionary<PlayerId, Ghost> _ghosts = new();

    private void Start()
    {
        events.OnPlayerDisconnected += OnPlayerDisconnected;
        rpc.FollowToggled += ToggleFollow;
        sync.SettingsChanged += () => { foreach (var talker in _talks.Keys.ToList()) End(talker); };
    }

    private void Stop()
    {
        events.OnPlayerDisconnected -= OnPlayerDisconnected;
        rpc.FollowToggled -= ToggleFollow;
    }

    private void OnPlayerDisconnected(PlayerId player, Entity _, LiteNetLib.DisconnectReason __)
    {
        _dialogueSlot.Remove(player);
        _activity.Remove(player);
        _ghosts.Remove(player);
        if (_talks.ContainsKey(player))
            End(player);
        foreach (var talk in _talks.Values)
        {
            talk.Witnesses.Remove(player);
            talk.Frozen.Remove(player);
            talk.Invited.Remove(player);
        }
    }

    private void Update()
    {
        _clock += Time.DeltaTime;
        RunGhosts();
        _elapsed += Time.DeltaTime;
        if (_elapsed < ScanSeconds)
            return;
        _elapsed = 0;

        var mains = new List<MainCharacter>();
        foreach (var m in entities.Query<MainCharacter>())
            mains.Add(m);
        foreach (var main in mains)
        {
            var id = main.PlayerId;
            var talking = main.InDialogue;
            if (talking && !_talks.ContainsKey(id))
                Begin(main, mains);
            else if (!talking && _talks.ContainsKey(id))
                End(id);
            ReadLines(main);
        }
        UpdateFrozen(mains);
    }

    private bool Overlay => sync.Settings.ConversationOverlay != "off";

    /// <summary>"Alt+L": shown on the overlay ("Alt+L to leave").</summary>
    private string FollowKey => sync.Settings.Key("follow");

    private void Begin(MainCharacter talker, List<MainCharacter> mains)
    {
        var id = talker.PlayerId;
        var talk = new Talk { Npc = NearestNpc(talker) ?? "" };
        _talks[id] = talk;

        // Someone who starts talking stops watching anyone else (and is no longer offered their conversation).
        foreach (var (other, t) in _talks)
            if (other != id && (t.Witnesses.Remove(id) | t.Invited.Remove(id)))
            {
                t.Frozen.Remove(id);
                rpc.SendConversationEnded(id);
            }

        if (!Overlay)
            return;
        var settings = sync.Settings;
        var ask = settings.ConversationFollow == "ask";
        foreach (var m in mains)
        {
            var w = m.PlayerId;
            if (w == id || m.InDialogue || _talks.Values.Any(t => t.Witnesses.Contains(w)))
                continue;
            if (settings.ConversationOverlay == "area" && !sync.SameArea(id, w))
                continue;
            if (ask)
                Offer(id, talk, w);
            else
                Watch(id, talk, m);
        }
        logger.LogInformation("[QuestSync] {Player} is talking to {Npc}; {Count} witness(es), {Frozen} frozen, {Asked} asked.",
            sync.Nickname(id), talk.Npc.Length > 0 ? talk.Npc : "someone", talk.Witnesses.Count, talk.Frozen.Count, talk.Invited.Count);
    }

    /// <summary>
    /// Offers the conversation to <paramref name="w"/>: a small banner ("Alice talks with Ysabel · Alt+L to listen
    /// in") that stays up until the conversation ends or they listen in.
    /// </summary>
    private void Offer(PlayerId talker, Talk talk, PlayerId w)
    {
        talk.Invited.Add(w);
        rpc.SendConversationStarted(w, JsonSerializer.Serialize(new { key = FollowKey, talker = sync.Nickname(talker), npc = talk.Npc, offer = true }));
    }

    /// <summary>Whether a witness stands still, per the settings: never in combat.</summary>
    private bool ShouldFreeze(MainCharacter witness, bool near)
    {
        var mode = sync.Settings.ConversationFreeze;
        return !witness.InCombat && (mode == "everyone" || mode == "area" && near);
    }

    /// <summary>Makes <paramref name="m"/> a witness of the conversation: the overlay opens on what was said so far.</summary>
    private void Watch(PlayerId talker, Talk talk, MainCharacter m)
    {
        var w = m.PlayerId;
        talk.Invited.Remove(w);
        talk.Witnesses.Add(w);
        var freeze = ShouldFreeze(m, sync.SameArea(talker, w));
        if (freeze)
            talk.Frozen.Add(w);
        rpc.SendConversationStarted(w, JsonSerializer.Serialize(new { key = FollowKey, talker = sync.Nickname(talker), npc = talk.Npc, frozen = freeze }));
        foreach (var json in talk.Recent)
            rpc.SendConversationLine(w, json);
        if (_activity.TryGetValue(talker, out var doing))
            rpc.SendConversationActivity(w, ActivityJson(doing));
    }

    /// <summary>
    /// The follow key: leave the conversation you are watching (you can come back), or listen in on one you were offered.
    /// </summary>
    public void ToggleFollow(PlayerId player)
    {
        foreach (var (talker, talk) in _talks)
            if (talk.Witnesses.Remove(player))
            {
                talk.Frozen.Remove(player);
                Offer(talker, talk, player);
                logger.LogInformation("[QuestSync] {Player} stopped watching {Talker}.", sync.Nickname(player), sync.Nickname(talker));
                return;
            }
        if (_ghosts.TryGetValue(player, out var ghost))
        {
            ToggleGhost(player, ghost);
            return;
        }
        if (!entities.TryLookup(player, out MainCharacter me) || me.InDialogue)
            return;
        foreach (var (talker, talk) in _talks)
            if (talk.Invited.Contains(player))
            {
                Watch(talker, talk, me);
                logger.LogInformation("[QuestSync] {Player} listens in on {Talker}.", sync.Nickname(player), sync.Nickname(talker));
                return;
            }
        rpc.SendNotice(player, "Nobody is talking near you.");
    }

    private void End(PlayerId talker)
    {
        if (!_talks.Remove(talker, out var talk))
            return;
        foreach (var w in talk.Witnesses.Concat(talk.Invited))
            rpc.SendConversationEnded(w);
    }

    /// <summary>Takes the dialogue plugin's line tokens out of the player's inventory and passes the lines on.</summary>
    private void ReadLines(MainCharacter main)
    {
        var id = main.PlayerId;
        var beacon = data.DialogueBeacon;
        List<(uint FormId, int Count)>? found = null;
        string? menu = null;
        _dialogueSlot.TryGetValue(id, out var slot);
        for (var i = 0; i < main.ItemsCount; i++)
        {
            var entry = main.GetItems(i);
            uint formId = entry.FormId.FormId;
            var top = formId >> 24;
            if (entry.Count <= 0 || top == 0 || top == 0xFF)
                continue;
            var local = formId & 0x00FFFFFF;
            if (local == beacon)
            {
                slot = top;
                _dialogueSlot[id] = top;
                continue;
            }
            if (slot != 0 && top == slot && data.MenuOf(local) is { } m)
                menu = m;
            else if (slot != 0 && top == slot && data.TryGetLine(local, out _))
                (found ??= new()).Add((formId, entry.Count));
        }
        SetActivity(main, menu);
        if (found is null)
            return;
        foreach (var (formId, count) in found)
        {
            sync.Take(main, formId, count);
            if (data.TryGetLine(formId & 0x00FFFFFF, out var line))
                Relay(id, line);
        }
    }

    /// <summary>Notes the menu a player has open and tells whoever watches their conversation.</summary>
    private void SetActivity(MainCharacter main, string? menu)
    {
        var id = main.PlayerId;
        // The barter screen is the game's container screen: outside a conversation it is a chest, not a merchant.
        if (menu == "barter" && !main.InDialogue)
            menu = null;
        var doing = menu is not null && Activities.TryGetValue(menu, out var label) ? label : null;
        _activity.TryGetValue(id, out var was);
        if (doing == was)
            return;
        if (doing is null)
            _activity.Remove(id);
        else
            _activity[id] = doing;
        logger.LogInformation("[QuestSync] {Player} {What}.", sync.Nickname(id), doing is null ? $"stopped {was}" : $"is {doing}");
        if (_talks.TryGetValue(id, out var talk))
            foreach (var w in talk.Witnesses)
                rpc.SendConversationActivity(w, ActivityJson(doing));
    }

    private static string ActivityJson(string? doing) => JsonSerializer.Serialize(new { activity = doing });

    /// <summary>What the player is doing in a menu ("bartering", "picking a lock"), or null.</summary>
    public string? Activity(PlayerId id) => _activity.TryGetValue(id, out var a) ? a : null;

    private void Relay(PlayerId talker, GameData.Line line)
    {
        var speaker = line.Speaker is { } key ? data.Name(key) : null;
        if (_talks.TryGetValue(talker, out var talk))
        {
            if (speaker is not null && talk.Npc.Length == 0)
                talk.Npc = speaker;
            var json = JsonSerializer.Serialize(new
            {
                topic = data.Name(line.Topic),
                lines = line.Lines.Select(l => data.Text(l)).Where(l => l is not null).ToArray(),
                speaker = speaker ?? talk.Npc,
                quest = line.Quest is { } q ? data.QuestName(q) : null,
            });
            talk.Recent.Add(json);
            if (talk.Recent.Count > RecentLines)
                talk.Recent.RemoveAt(0);
            foreach (var w in talk.Witnesses)
                rpc.SendConversationLine(w, json);
        }

        // A reward handed over in the conversation goes to the others too (not trades: lines that also take something).
        var settings = sync.Settings;
        if (!settings.ShareDialogueRewards || line.Takes)
            return;
        foreach (var (tableId, count) in line.Gives)
        {
            if (count <= 0 || data.ToGame(tableId) is not { } formId)
                continue;
            var questItem = sync.Map.QuestItems.Contains(tableId);
            if (questItem && !settings.ShareQuestItems)
                continue;
            sync.ShareItem(talker, formId, count, onlyIfMissing: questItem);
        }
    }

    /// <summary>The closest NPC to the talker in the same area: the one they are most likely talking to.</summary>
    private string? NearestNpc(MainCharacter talker)
    {
        if (!sync.Players.TryGetValue(talker.PlayerId, out var view) || view.Area is not { } area)
            return null;
        var pos = talker.Position;
        string? best = null;
        var bestDistance = NpcSearchRange;
        foreach (var npc in entities.Query<Npc>())
        {
            if (npc.IsDead || !npc.Cell.AreaId.Equals(area))
                continue;
            var d = Vector3.Distance(npc.Position, pos);
            if (d >= bestDistance)
                continue;
            uint baseId = npc.BaseId.FormId;
            if (data.NpcName(baseId) is not { } name)
                continue;
            best = name;
            bestDistance = d;
        }
        return best;
    }

    /// <summary>Each player is frozen while any conversation freezes them.</summary>
    private void UpdateFrozen(List<MainCharacter> mains)
    {
        foreach (var main in mains)
        {
            var id = main.PlayerId;
            if (!sync.Players.TryGetValue(id, out var view))
                continue;
            var want = _talks.Values.Any(t => t.Frozen.Contains(id)) || _ghosts.TryGetValue(id, out var g) && g.Watching && g.Frozen;
            if (want == view.WantFrozen)
                continue;
            view.WantFrozen = want;
            sync.ApplySwitches(main, view);
        }
    }

    /// <summary>
    /// Test: shows <paramref name="witness"/> a conversation between a made-up player and an NPC, a few real lines
    /// from the game's dialogue, and freezes them as a witness in the same area would be.
    /// </summary>
    public void StartGhost(MainCharacter witness, string ghostName, bool ask)
    {
        var id = witness.PlayerId;
        var settings = sync.Settings;
        var freeze = settings.ConversationFreeze != "off" && !witness.InCombat;
        var samples = data.SampleLines(4);
        var npc = samples.Select(l => l.Speaker).FirstOrDefault(n => n is not null) is { } key ? data.Name(key) : "Ysabel Andronicus";
        var ghost = new Ghost { Name = ghostName, Npc = npc, Frozen = freeze, Watching = !ask };
        var at = _clock + 2f;
        var index = 0;
        foreach (var line in samples)
        {
            // Between lines, the ghost opens a menu or two, as a talker would: bartering after the second line,
            // persuading after the third.
            if (index is 2 or 3)
            {
                ghost.Lines.Enqueue((at, true, ActivityJson(index == 2 ? "bartering" : "persuading")));
                at += 6f;
                ghost.Lines.Enqueue((at, true, ActivityJson(null)));
                at += 1f;
            }
            index++;
            ghost.Lines.Enqueue((at, false, JsonSerializer.Serialize(new
            {
                topic = data.Name(line.Topic),
                lines = line.Lines.Select(l => data.Text(l)).Where(l => l is not null).ToArray(),
                speaker = npc,
                quest = line.Quest is { } q ? data.QuestName(q) : null,
            })));
            at += 5f;
        }
        ghost.EndsAt = at + 2f;
        _ghosts[id] = ghost;
        rpc.SendConversationStarted(id, JsonSerializer.Serialize(ask
            ? new { key = FollowKey, talker = ghostName, npc, frozen = false, offer = true }
            : new { key = FollowKey, talker = ghostName, npc, frozen = freeze, offer = false }));
        logger.LogInformation("[QuestSync] Test: {Player} watches {Ghost} talk to {Npc} for {Seconds:0} s (frozen: {Frozen}).",
            sync.Nickname(id), ghostName, npc, ghost.EndsAt - _clock, freeze);
    }

    /// <summary>The follow key during a test conversation: stop or start watching it, as with a real one.</summary>
    private void ToggleGhost(PlayerId id, Ghost ghost)
    {
        ghost.Watching = !ghost.Watching;
        if (!ghost.Watching)
        {
            rpc.SendConversationStarted(id, JsonSerializer.Serialize(new { key = FollowKey, talker = ghost.Name, npc = ghost.Npc, offer = true }));
            return;
        }
        rpc.SendConversationStarted(id, JsonSerializer.Serialize(new { key = FollowKey, talker = ghost.Name, npc = ghost.Npc, frozen = ghost.Frozen }));
        foreach (var json in ghost.Recent)
            rpc.SendConversationLine(id, json);
        if (ghost.Activity is not null)
            rpc.SendConversationActivity(id, ghost.Activity);
    }

    /// <summary>Ends a test conversation early.</summary>
    public bool StopGhost(PlayerId id)
    {
        if (!_ghosts.Remove(id))
            return false;
        rpc.SendConversationEnded(id);
        return true;
    }

    private void RunGhosts()
    {
        foreach (var (id, ghost) in _ghosts.ToList())
        {
            while (ghost.Lines.Count > 0 && ghost.Lines.Peek().At <= _clock)
            {
                var (_, activity, json) = ghost.Lines.Dequeue();
                if (activity)
                    ghost.Activity = json;
                else
                {
                    ghost.Recent.Add(json);
                    if (ghost.Recent.Count > RecentLines)
                        ghost.Recent.RemoveAt(0);
                }
                if (!ghost.Watching)
                    continue;
                if (activity)
                    rpc.SendConversationActivity(id, json);
                else
                    rpc.SendConversationLine(id, json);
            }
            if (_clock >= ghost.EndsAt)
                StopGhost(id);
        }
    }

    /// <summary>Who is talking, for the web page and the tags over heads.</summary>
    public bool IsTalking(PlayerId id) => _talks.ContainsKey(id);
}
