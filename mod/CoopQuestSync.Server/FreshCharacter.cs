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
/// The first join of a brand-new character (setting NewCharacterSetup). OblivionMP 1.1 starts everyone as the same
/// preset character, from the same world save: a level 1 Dunmer near Aleswell with that character's gear and a few
/// quests under way. Once the player is in the world, this service:
/// <list type="bullet">
/// <item>empties their bag, keeping the starter kit (config.json StarterKitItems, setting StarterKit);</item>
/// <item>takes them to the sewer exit, where the vanilla game starts you off (setting NewCharacterAtSewers);</item>
/// <item>asks their game to open the race, class and birthsign menus, one after the other (once they are there);</item>
/// <item>lets the party's progress reach them (<see cref="QuestTokenSync"/> holds it until then, and keeps the quest
/// stages the preset came with out of the shared progress).</item>
/// </list>
/// Who is brand new: <see cref="PlayerSaves"/>. It runs once per character, and again only if an admin moves the
/// player's save aside on the stopped server.
/// </summary>
[Service]
public sealed partial class FreshCharacter(IEntities entities, QuestTokenSync sync, Inventories inventories, CoopRpc rpc,
    ReturnOnJoin travel, QuestSyncConfig config, IChat chat, ILogger logger)
{
    /// <summary>CGSewerExitMarker in Oblivion.esm: the vanilla start's spot just inside the sewer gate (ImperialSewers03).</summary>
    private static readonly ReturnSpot SewerExit = ReturnOnJoin.GameSpot(0x000272F2, "ImperialSewers03", 3822.1f, 2046.3f, 6894.6f);
    /// <summary>In the sewers this long before the menus open: the move onto the spot and the black screen finish first.</summary>
    private static readonly TimeSpan ArriveSettle = TimeSpan.FromSeconds(4);
    /// <summary>No arrival by then (no add-on?): the menus open where they are.</summary>
    private static readonly TimeSpan TravelTimeout = TimeSpan.FromSeconds(60);

    /// <summary>Players on their way to the sewer exit: when they left, and since when they stand there.</summary>
    private readonly Dictionary<PlayerId, (DateTime Left, DateTime? There)> _travelling = new();
    /// <summary>In the world this long before the setup: the load and the plugin's first look settle first.</summary>
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(8);

    private readonly Dictionary<PlayerId, DateTime> _inWorldSince = new();
    private List<(uint FormId, int Count)> _kit = [];

    private void Start()
    {
        _kit = NewStart.ParseKit(config.StarterKitItems, logger);
        sync.Passed += OnPassed;
    }

    private void Stop() => sync.Passed -= OnPassed;

    private void OnPassed()
    {
        var now = DateTime.UtcNow;
        foreach (var main in entities.Query<MainCharacter>())
        {
            var id = main.PlayerId;
            if (_travelling.TryGetValue(id, out var trip) && sync.Players.TryGetValue(id, out var traveller))
            {
                var there = traveller.Area is { } a && GameData.AreaKey(a) == SewerExit.Area;
                if (there && trip.There is null)
                    _travelling[id] = trip = (trip.Left, now);
                else if (!there)
                    _travelling[id] = trip = (trip.Left, null);
                if (trip.There is { } since2 && now - since2 >= ArriveSettle)
                {
                    _travelling.Remove(id);
                    OpenMenus(id);
                }
                else if (now - trip.Left > TravelTimeout)
                {
                    _travelling.Remove(id);
                    logger.LogWarning("[QuestSync] {Player}: never got to the sewer exit (is the CoopQuestSyncUI add-on installed?). Opening the character menus here.", sync.Nickname(id));
                    OpenMenus(id);
                }
                continue;
            }
            if (!sync.Players.TryGetValue(id, out var view) || !view.Fresh)
                continue;
            if (!sync.Settings.NewCharacterSetup)
            {
                sync.CharacterSetUp(id, done: false);
                continue;
            }
            if (view.Phase != QuestTokenSync.Phase.Ready || view.Area is null || view.InDialogue || main.IsDead)
            {
                _inWorldSince.Remove(id);
                continue;
            }
            if (!_inWorldSince.TryGetValue(id, out var since))
                _inWorldSince[id] = since = now;
            if (now - since < Settle)
                continue;
            _inWorldSince.Remove(id);
            SetUp(main, view);
        }
        foreach (var id in _inWorldSince.Keys.Where(id => !sync.Players.ContainsKey(id)).ToList())
            _inWorldSince.Remove(id);
        foreach (var id in _travelling.Keys.Where(id => !sync.Players.ContainsKey(id)).ToList())
            _travelling.Remove(id);
    }

    private void SetUp(MainCharacter main, QuestTokenSync.PlayerView view)
    {
        var id = main.PlayerId;
        var name = sync.Nickname(id);
        // The preset starts at level 1. A character that has levelled has been played: never empty that bag.
        if (main.Level > 1)
        {
            logger.LogWarning("[QuestSync] {Player}: looked new (no save on this server), but is level {Level}. Left as is.", name, main.Level);
            sync.CharacterSetUp(id, done: false);
            return;
        }

        var kit = sync.Settings.StarterKit ? _kit : [];
        var keep = new Dictionary<uint, int>();
        foreach (var (formId, count) in kit)
            keep[formId] = keep.GetValueOrDefault(formId) + count;

        var taken = 0;
        foreach (var (formId, have) in view.Previous.ToList())
        {
            var extra = have - keep.GetValueOrDefault(formId);
            if (extra <= 0)
                continue;
            sync.Take(main, formId, extra);
            view.Previous[formId] = have - extra;
            taken += extra;
        }
        var equipment = main.As<Equipment>();
        foreach (var (formId, want) in keep)
        {
            var missing = want - view.Previous.GetValueOrDefault(formId);
            if (missing <= 0)
                continue;
            inventories.Add(equipment, new InventoryEntry(new FormId(formId), missing, 0));
            view.Previous[formId] = want;
        }
        // The server changed the bag itself: nothing here was picked up in the game.
        view.SkipItemDiff = true;
        _kitGiven[id] = kit.Count > 0;
        if (sync.Settings.NewCharacterAtSewers && travel.SendTo(id, SewerExit, "Welcome! Taking you to the sewer exit..."))
        {
            _travelling[id] = (DateTime.UtcNow, null);
            logger.LogInformation("[QuestSync] {Player}: new character set up: took {Taken} item(s) of the preset's, {Kit} starter item(s). Taking them to the sewer exit, then the character menus.",
                name, taken, kit.Count);
            return;
        }
        logger.LogInformation("[QuestSync] {Player}: new character set up: took {Taken} item(s) of the preset's, {Kit} starter item(s), opening the character menus.",
            name, taken, kit.Count);
        OpenMenus(id);
    }

    private readonly Dictionary<PlayerId, bool> _kitGiven = new();

    private void OpenMenus(PlayerId id)
    {
        var kit = _kitGiven.Remove(id, out var given) && given;
        // From here on the party's progress reaches them (it waited, so the catch-up didn't hold up the trip).
        sync.CharacterSetUp(id, done: true);
        rpc.SendMakeCharacter(id);
        chat.SendTo(id, kit
            ? "Welcome! Make your character: name and race, then class, then birthsign. Your bag holds the starter kit."
            : "Welcome! Make your character: name and race, then class, then birthsign.", ChatLineKind.System);
    }
}
