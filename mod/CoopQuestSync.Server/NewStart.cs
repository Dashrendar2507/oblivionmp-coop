using System.Globalization;
using Microsoft.Extensions.Logging;
using OblivionMp.ChatMod.Common;
using OblivionMp.ChatMod.Serverside;
using OblivionMP.SDK.Common.Archetypes;
using OblivionMP.SDK.Common.Archetypes.Mixins;
using OblivionMP.SDK.Serverside.Items;
using ReadyM.Api.Idents;
using ReadyM.Relay.Common.Oblivion;
using ReadyM.Relay.Common.Oblivion.ECS.Values;
using ReadyM.SDK.Attributes;
using ReadyM.SDK.Server.Entities;

namespace CoopQuestSync.Server;

/// <summary>
/// Where a new player starts: in the prison with the game's own intro, or at the sewer exit with the intro skipped.
///
/// The plugin's CQSNewStart quest holds CQSIntro while the player hasn't left the sewers (1 in the prison intro, 2 when no
/// intro runs). For such a player, this service gives CQSStartOffer: count 1 opens a window with both starts, count 2 a
/// note that they start at the sewer exit. The prison is only offered until someone has left the sewers on this server
/// (setting PrisonStart): the Emperor and the Blades are shared NPCs, so once the intro has played, the Emperor stays
/// dead and the doors stay open for everyone.
///
/// The skip happens in the player's own game (the plugin: MQ02 10 and 20, the intro's topics, stop the intro, move to
/// just inside the sewer gate), then the plugin gives back CQSStartDone. This service then hands out the Amulet of Kings
/// and the starter kit (marked with CQSStartKit so it happens once), and asks the player's game to open the race, class
/// and birthsign menus. The sewer gate's own script offers to change them again before going out, which sets MQ01 88 and
/// ends the intro. <see cref="QuestTokenSync"/> holds the party's quest progress until then.
///
/// If OblivionMP ever stops starting new characters in the prison (CQSIntro 2: never left the sewers, no intro running),
/// the sewer exit start still works, and the log says the game's start has changed.
/// </summary>
[Service]
public sealed partial class NewStart(IEntities entities, QuestTokenSync sync, Inventories inventories, CoopRpc rpc,
    QuestSyncConfig config, IChat chat, ILogger logger)
{
    /// <summary>The Amulet of Kings, in Oblivion.esm (the first plugin of every load order).</summary>
    public const uint AmuletOfKings = 0x000250A0;

    private static readonly QuestStage AmuletDelivered = new("MQ02", 50);

    private readonly Dictionary<PlayerId, string> _told = new();
    /// <summary>Players seen in the sewers (or the prison) on this server, and whether they took the intro skip.</summary>
    private readonly Dictionary<PlayerId, bool> _inSewers = new();
    /// <summary>The server had no saved world when the mod started: nobody has played the intro in this one.</summary>
    private bool _newWorld;
    private List<(uint FormId, int Count)>? _kit;

    private TokenMap Map => sync.Map;

    private void Start()
    {
        if (Map.IntroLocalId == 0)
        {
            logger.LogWarning("[QuestSync] The plugin's tables have no new-player start: new players get the game's own start.");
            return;
        }
        _kit = ParseKit(config.StarterKitItems, logger);
        _newWorld = PlayerSaves.WorldIsNew() == true;
        sync.Passed += OnPassed;
    }

    private void Stop() => sync.Passed -= OnPassed;

    /// <summary>After every pass over the inventories, on the game loop.</summary>
    private void OnPassed()
    {
        if (_newWorld)
        {
            _newWorld = false;
            if (sync.SewersLeft)
            {
                sync.SewersLeft = false;
                sync.MarkDirty();
                logger.LogInformation("[QuestSync] The server made a new world: the prison intro can be played again.");
            }
        }
        foreach (var main in entities.Query<MainCharacter>())
        {
            var id = main.PlayerId;
            if (!sync.Players.TryGetValue(id, out var view) || view.ModIndex is not { } slot || view.Phase != QuestTokenSync.Phase.Ready)
                continue;
            var offer = Count(view, Map.StartOfferLocalId);
            var prison = Count(view, Map.StartPrisonLocalId);
            var done = Count(view, Map.StartDoneLocalId);
            var kit = Count(view, Map.StartKitLocalId);

            if (view.Intro > 0)
                _inSewers[id] = done > 0;
            if (view.Intro == 0)
            {
                // Out of the sewers after playing the intro here: the shared Emperor and Blades have played their part.
                // Not for a character that was already out when it joined (OblivionMP's preset start), nor for one
                // who took the skip (the prison was never played).
                if (_inSewers.Remove(id, out var skipped) && !skipped && !sync.SewersLeft)
                {
                    sync.SewersLeft = true;
                    sync.MarkDirty();
                    logger.LogInformation("[QuestSync] {Player} played the intro and left the sewers: from now on, new players start at the sewer exit.", sync.Nickname(id));
                }
                if (offer > 0)
                    sync.Take(main, slot << 24 | Map.StartOfferLocalId, offer);
                Tell(id, "out", null);
                TakeSpareAmulet(main, view);
                continue;
            }

            if (prison > 0 || done > 0)
            {
                if (offer > 0)
                    sync.Take(main, slot << 24 | Map.StartOfferLocalId, offer);
                if (done > 0 && kit == 0)
                    GiveStart(main, view, slot);
                Tell(id, prison > 0 ? "prison" : "skipped", prison > 0
                    ? "{Player} chose the prison intro. The party's progress waits until they leave the sewers."
                    : "{Player} is at the sewer exit. The party's progress waits until they go through the gate.");
                continue;
            }

            var want = Wanted(view);
            if (offer < want)
                inventories.Add(main.As<Equipment>(), new InventoryEntry(new FormId(slot << 24 | Map.StartOfferLocalId), want - offer, 0));
            else if (offer > want)
                sync.Take(main, slot << 24 | Map.StartOfferLocalId, offer - want);
            if (want == 0)
                Tell(id, "game", "{Player} hasn't left the sewers yet; the mod leaves the start to the game (setting New players: off).");
            else if (view.Intro == 2)
                Tell(id, "changed", "{Player} never left the sewers, but the game's intro isn't running: OblivionMP's new-player start has changed. Offering the sewer exit start.", warn: true);
            else
                Tell(id, want == 1 ? "ask" : "sewers", want == 1
                    ? "{Player} is new: asking where they want to start (prison or sewer exit)."
                    : "{Player} is new: they start at the sewer exit" + (sync.Settings.NewStart == "sewers" ? "." : " (someone already left the sewers on this server)."));
        }
        foreach (var id in _told.Keys.Where(id => !sync.Players.ContainsKey(id)).ToList())
            _told.Remove(id);
        foreach (var id in _inSewers.Keys.Where(id => !sync.Players.ContainsKey(id)).ToList())
            _inSewers.Remove(id);
    }

    /// <summary>CQSStartOffer's count: 0 no window, 1 the prison or the sewer exit, 2 the sewer exit only.</summary>
    private int Wanted(QuestTokenSync.PlayerView view)
    {
        var settings = sync.Settings;
        if (settings.NewStart == "off")
            return 0;
        var prison = view.Intro == 1 && settings.NewStart == "ask"
                     && (settings.PrisonStart == "always" || settings.PrisonStart == "fresh" && !sync.SewersLeft);
        return prison ? 1 : 2;
    }

    /// <summary>The player stands at the sewer exit: the amulet, the kit, and the character menus.</summary>
    private void GiveStart(MainCharacter main, QuestTokenSync.PlayerView view, uint slot)
    {
        var name = sync.Nickname(main.PlayerId);
        var equipment = main.As<Equipment>();
        var delivered = sync.Shared.TryGetValue(AmuletDelivered.Quest, out var mq02) && mq02.Contains(AmuletDelivered.Stage);
        var amulet = !delivered && view.Previous.GetValueOrDefault(AmuletOfKings) == 0;
        if (amulet)
            inventories.Add(equipment, new InventoryEntry(new FormId(AmuletOfKings), 1, 0));
        var kit = sync.Settings.StarterKit ? _kit ?? [] : [];
        foreach (var (formId, count) in kit)
            inventories.Add(equipment, new InventoryEntry(new FormId(formId), count, 0));
        inventories.Add(equipment, new InventoryEntry(new FormId(slot << 24 | Map.StartKitLocalId), 1, 0));
        // These are the server's gifts, not something the player picked up: don't share the amulet as a quest item.
        view.SkipItemDiff = true;
        logger.LogInformation("[QuestSync] {Player}: start at the sewer exit: {Amulet}, {Kit} starter item(s).", name,
            amulet ? "gave the Amulet of Kings" : delivered ? "no amulet (Jauffre already has it)" : "already has the amulet", kit.Count);

        rpc.SendMakeCharacter(main.PlayerId);
        chat.SendTo(main.PlayerId, delivered
            ? "You start at the sewer exit. Make your character, then go through the gate. Your party has already brought the Amulet of Kings to Jauffre."
            : "You start at the sewer exit with the Amulet of Kings. Make your character, then go through the gate.", ChatLineKind.System);
    }

    /// <summary>Jauffre has the amulet: the copies the other players were given at the start go away.</summary>
    private void TakeSpareAmulet(MainCharacter main, QuestTokenSync.PlayerView view)
    {
        if (!sync.Settings.TakeSpareAmulets || view.CatchingUp || !view.Stages.Contains(AmuletDelivered))
            return;
        if (!sync.Shared.TryGetValue(AmuletDelivered.Quest, out var mq02) || !mq02.Contains(AmuletDelivered.Stage))
            return;
        var have = view.Previous.GetValueOrDefault(AmuletOfKings);
        if (have <= 0)
            return;
        sync.Take(main, AmuletOfKings, have);
        view.Previous[AmuletOfKings] = 0;
        view.SkipItemDiff = true;
        logger.LogInformation("[QuestSync] {Player}: took {Count} spare Amulet of Kings (Jauffre has it).", sync.Nickname(main.PlayerId), have);
        chat.SendTo(main.PlayerId, "Jauffre has the Amulet of Kings now, so your copy is gone.", ChatLineKind.System);
    }

    /// <summary>
    /// Admin: offers the start again to a player who hasn't left the sewers (takes their answer, so the window comes back).
    /// Returns what happened, for the reply.
    /// </summary>
    public string OfferAgain(PlayerId id)
    {
        if (!sync.Players.TryGetValue(id, out var view) || view.ModIndex is not { } slot)
            return "That player's plugin hasn't been found yet.";
        if (view.Intro == 0)
            return $"{sync.Nickname(id)} has already left the sewers.";
        foreach (var main in entities.Query<MainCharacter>())
        {
            if (main.PlayerId != id)
                continue;
            foreach (var local in new[] { Map.StartPrisonLocalId, Map.StartDoneLocalId, Map.StartKitLocalId })
                if (Count(view, local) is var n and > 0)
                    sync.Take(main, slot << 24 | local, n);
            _told.Remove(id);
            return $"{sync.Nickname(id)} gets the start window again.";
        }
        return "That player isn't in the game.";
    }

    /// <summary>Admin: the world was reset, nobody has played the intro here: the prison can be offered again.</summary>
    public void MarkPrisonFresh()
    {
        sync.SewersLeft = false;
        sync.MarkDirty();
        logger.LogInformation("[QuestSync] The prison intro is marked as not played: new players may start there again (until someone leaves the sewers).");
    }

    public string Describe()
    {
        var s = sync.Settings;
        var prison = s.NewStart != "ask" || s.PrisonStart == "never" ? "never offered"
            : s.PrisonStart == "always" ? "always offered"
            : sync.SewersLeft ? "not offered (someone has left the sewers on this server)" : "offered (nobody has left the sewers yet)";
        var players = string.Join(", ", sync.Players
            .Where(p => p.Value.ModIndex is not null)
            .Select(p => $"{sync.Nickname(p.Key)}: {StateOf(p.Value)}"));
        return $"New players: {s.NewStart}. Prison intro: {prison}. Starter kit: {(s.StarterKit ? "on" : "off")}." + (players.Length > 0 ? " " + players : "");
    }

    private string StateOf(QuestTokenSync.PlayerView view) =>
        view.Intro == 0 ? "out of the sewers"
        : Count(view, Map.StartDoneLocalId) > 0 ? "at the sewer exit"
        : Count(view, Map.StartPrisonLocalId) > 0 ? "in the prison intro"
        : Count(view, Map.StartOfferLocalId) > 0 ? "choosing a start"
        : view.Intro == 1 ? "in the prison intro" : "hasn't left the sewers";

    private static int Count(QuestTokenSync.PlayerView view, uint local) => local == 0 ? 0 : view.Own.GetValueOrDefault(local);

    /// <summary>Logs a player's start once per change of state.</summary>
    private void Tell(PlayerId id, string state, string? message, bool warn = false)
    {
        if (_told.TryGetValue(id, out var last) && last == state)
            return;
        _told[id] = state;
        if (message is null)
            return;
        if (warn)
            logger.LogWarning("[QuestSync] " + message, sync.Nickname(id));
        else
            logger.LogInformation("[QuestSync] " + message, sync.Nickname(id));
    }

    /// <summary>config.json StarterKitItems: "formid:count" pairs in hexadecimal, separated by commas.</summary>
    internal static List<(uint FormId, int Count)> ParseKit(string text, ILogger logger)
    {
        var kit = new List<(uint, int)>();
        foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var bits = part.Split(':');
            if (bits.Length is 1 or 2 && uint.TryParse(bits[0].Trim().Replace("0x", "", StringComparison.OrdinalIgnoreCase),
                    NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var formId)
                && (bits.Length == 1 || int.TryParse(bits[1].Trim(), out _)))
                kit.Add((formId, bits.Length == 2 ? Math.Max(1, int.Parse(bits[1].Trim())) : 1));
            else
                logger.LogWarning("[QuestSync] StarterKitItems: can't read \"{Item}\" (form id in hexadecimal, a colon, a count).", part);
        }
        return kit;
    }
}
