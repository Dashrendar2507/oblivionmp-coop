using Microsoft.Extensions.Logging;
using OblivionMp.ChatMod.Common;
using OblivionMp.ChatMod.Serverside;
using OblivionMp.Sdk.Common.Archetypes;
using OblivionMp.Sdk.Common.Archetypes.Mixins;
using OblivionMp.Sdk.Serverside.Items;
using ReadyM.Api.Idents;
using ReadyM.Relay.Common.Oblivion;
using ReadyM.Relay.Common.Oblivion.ECS.Values;
using ReadyM.Relay.Common.Oblivion.Rpc;
using ReadyM.SDK.Attributes;
using ReadyM.SDK.Server.Entities;

namespace CoopQuestSync.Server;

/// <summary>
/// Chat commands for the test. They act on the player who types them, so each tester can try both directions.
///   /qsprobe inv                 - list the plugin items the server sees in your inventory
///   /qsprobe count &lt;formid&gt;      - how many of one item the server sees in your inventory
///   /qsprobe give &lt;formid&gt; &lt;n&gt;   - the server adds n of that item to your inventory
///   /qsprobe take &lt;formid&gt; &lt;n&gt;   - the server removes n of that item
/// </summary>
[Service]
public sealed partial class ProbeCommands(IChat chat, IEntities entities, Inventories inventories, ILogger logger)
{
    private const string Usage = "/qsprobe inv | count <formid> | give <formid> <count> | take <formid> <count>";

    private void Start() => chat.AddCommand("qsprobe", Usage, Run);

    private void Run(IChat c, PlayerId sender, string arguments)
    {
        var parts = arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            Reply(sender, Usage);
            return;
        }

        if (!entities.TryLookup(sender, out MainCharacter main))
        {
            Reply(sender, "No character found for you yet.");
            return;
        }

        switch (parts[0].ToLowerInvariant())
        {
            case "inv":
                var items = InventoryProbe.ReadItems(main, PluginItems.IsPluginForm);
                if (items.Count == 0)
                    Reply(sender, "The server sees no plugin items in your inventory.");
                foreach (var (formId, count) in items)
                    Reply(sender, $"{PluginItems.Hex(formId)} x{count}");
                break;

            case "count" when parts.Length == 2 && PluginItems.TryParseHex(parts[1], out var countId):
                var all = InventoryProbe.ReadItems(main, id => id == countId);
                Reply(sender, $"Server sees {PluginItems.Hex(countId)} x{all.GetValueOrDefault(countId)} in your inventory.");
                break;

            case "give" when parts.Length == 3 && PluginItems.TryParseHex(parts[1], out var giveId) && int.TryParse(parts[2], out var giveCount) && giveCount > 0:
                inventories.Add(main.As<Equipment>(), new InventoryEntry(new FormId(giveId), giveCount, 0));
                logger.LogInformation("[QuestSyncProbe] {Player}: server ADDED {FormId} x{Count}", sender, PluginItems.Hex(giveId), giveCount);
                Reply(sender, $"Server added {PluginItems.Hex(giveId)} x{giveCount}.");
                break;

            case "take" when parts.Length == 3 && PluginItems.TryParseHex(parts[1], out var takeId) && int.TryParse(parts[2], out var takeCount) && takeCount > 0:
                var stack = new ItemStackData
                {
                    BaseId = new FormId(takeId),
                    Count = takeCount,
                    Durability = InventoryEntry.NoInstanceOverride,
                    Charge = InventoryEntry.NoInstanceOverride,
                };
                var taken = inventories.TryTake(main.As<Equipment>(), stack);
                logger.LogInformation("[QuestSyncProbe] {Player}: server TAKE {FormId} x{Count}: {Result}", sender, PluginItems.Hex(takeId), takeCount, taken ? "ok" : "not enough");
                Reply(sender, taken ? $"Server removed {PluginItems.Hex(takeId)} x{takeCount}." : "You do not have that many.");
                break;

            default:
                Reply(sender, Usage);
                break;
        }
    }

    private void Reply(PlayerId player, string text) => chat.SendTo(player, text, ChatLineKind.CommandReply);
}
