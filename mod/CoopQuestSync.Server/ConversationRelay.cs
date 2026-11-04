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
    }

    private readonly Dictionary<PlayerId, Talk> _talks = new();
    private readonly Dictionary<PlayerId, uint> _dialogueSlot = new();
    private float _elapsed;

    private void Start()
    {
        events.OnPlayerDisconnected += OnPlayerDisconnected;
        sync.SettingsChanged += () => { foreach (var talker in _talks.Keys.ToList()) End(talker); };
    }

    private void Stop() => events.OnPlayerDisconnected -= OnPlayerDisconnected;

    private void OnPlayerDisconnected(PlayerId player, Entity _, LiteNetLib.DisconnectReason __)
    {
        _dialogueSlot.Remove(player);
        if (_talks.ContainsKey(player))
            End(player);
        foreach (var talk in _talks.Values)
        {
            talk.Witnesses.Remove(player);
            talk.Frozen.Remove(player);
        }
    }

    private void Update()
    {
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

    private void Begin(MainCharacter talker, List<MainCharacter> mains)
    {
        var id = talker.PlayerId;
        var talk = new Talk { Npc = NearestNpc(talker) ?? "" };
        _talks[id] = talk;

        // Someone who starts talking stops watching anyone else.
        foreach (var (other, t) in _talks)
            if (other != id && t.Witnesses.Remove(id))
            {
                t.Frozen.Remove(id);
                rpc.SendConversationEnded(id);
            }

        if (!Overlay)
            return;
        var settings = sync.Settings;
        foreach (var m in mains)
        {
            var w = m.PlayerId;
            if (w == id || m.InDialogue || _talks.Values.Any(t => t.Witnesses.Contains(w)))
                continue;
            var near = sync.SameArea(id, w);
            if (settings.ConversationOverlay == "area" && !near)
                continue;
            talk.Witnesses.Add(w);
            var freeze = !m.InCombat && (settings.ConversationFreeze == "everyone" || settings.ConversationFreeze == "area" && near);
            if (freeze)
                talk.Frozen.Add(w);
            rpc.SendConversationStarted(w, JsonSerializer.Serialize(new { talker = sync.Nickname(id), npc = talk.Npc, frozen = freeze }));
        }
        logger.LogInformation("[QuestSync] {Player} is talking to {Npc}; {Count} witness(es), {Frozen} frozen.",
            sync.Nickname(id), talk.Npc.Length > 0 ? talk.Npc : "someone", talk.Witnesses.Count, talk.Frozen.Count);
    }

    private void End(PlayerId talker)
    {
        if (!_talks.Remove(talker, out var talk))
            return;
        foreach (var w in talk.Witnesses)
            rpc.SendConversationEnded(w);
    }

    /// <summary>Takes the dialogue plugin's line tokens out of the player's inventory and passes the lines on.</summary>
    private void ReadLines(MainCharacter main)
    {
        var id = main.PlayerId;
        var beacon = data.DialogueBeacon;
        List<(uint FormId, int Count)>? found = null;
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
            if (slot != 0 && top == slot && data.TryGetLine(local, out _))
                (found ??= new()).Add((formId, entry.Count));
        }
        if (found is null)
            return;
        foreach (var (formId, count) in found)
        {
            sync.Take(main, formId, count);
            if (data.TryGetLine(formId & 0x00FFFFFF, out var line))
                Relay(id, line);
        }
    }

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
            var want = _talks.Values.Any(t => t.Frozen.Contains(id));
            if (want == view.WantFrozen)
                continue;
            view.WantFrozen = want;
            sync.ApplySwitches(main, view);
        }
    }

    /// <summary>Who is talking, for the web page and the tags over heads.</summary>
    public bool IsTalking(PlayerId id) => _talks.ContainsKey(id);
}
