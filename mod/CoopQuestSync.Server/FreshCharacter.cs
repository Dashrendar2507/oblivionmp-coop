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
/// <item>asks their game to open the race, class and birthsign menus, one after the other;</item>
/// <item>lets the party's progress reach them (<see cref="QuestTokenSync"/> holds it until then, and keeps the quest
/// stages the preset came with out of the shared progress).</item>
/// </list>
/// Who is brand new: <see cref="PlayerSaves"/>. It runs once per character, and again only if an admin moves the
/// player's save aside on the stopped server.
/// </summary>
[Service]
public sealed partial class FreshCharacter(IEntities entities, QuestTokenSync sync, Inventories inventories, CoopRpc rpc,
    QuestSyncConfig config, IChat chat, ILogger logger)
{
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
        sync.CharacterSetUp(id, done: true);
        logger.LogInformation("[QuestSync] {Player}: new character set up: took {Taken} item(s) of the preset's, {Kit} starter item(s), opening the character menus.",
            name, taken, kit.Count);

        rpc.SendMakeCharacter(id);
        chat.SendTo(id, kit.Count > 0
            ? "Welcome! Make your character: name and race, then class, then birthsign. Your bag holds the starter kit."
            : "Welcome! Make your character: name and race, then class, then birthsign.", ChatLineKind.System);
    }
}
