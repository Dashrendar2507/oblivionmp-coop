using OblivionMP.SDK.Common.Archetypes;

namespace CoopQuestSync.Server;

/// <summary>Helpers shared by the probe: which form ids count as "plugin items" worth logging.</summary>
internal static class PluginItems
{
    /// <summary>
    /// Items from Oblivion.esm (top byte 0x00) and dynamic forms the game makes at runtime (top byte 0xFF)
    /// are skipped. Everything else comes from a plugin, which is where our tokens live.
    /// </summary>
    public static bool IsPluginForm(uint formId)
    {
        var modIndex = formId >> 24;
        return modIndex != 0x00 && modIndex != 0xFF;
    }

    /// <summary>For the Phase 0 probe every item is logged, so a console AddItem of a vanilla item can be traced too.</summary>
    public static bool IsLogged(uint formId) => (formId >> 24) != 0xFF;

    public static string Hex(uint formId) => formId.ToString("X8");

    public static bool TryParseHex(string text, out uint formId)
    {
        text = text.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            text = text[2..];
        return uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out formId);
    }

    /// <summary>Counts per form id in the player's inventory as the server sees it, for the forms <paramref name="include"/> accepts.</summary>
    public static Dictionary<uint, int> ReadItems(MainCharacter main, Func<uint, bool> include)
    {
        var items = new Dictionary<uint, int>();
        for (var i = 0; i < main.ItemsCount; i++)
        {
            var entry = main.GetItems(i);
            uint formId = entry.FormId.FormId;
            if (!include(formId))
                continue;
            items[formId] = items.GetValueOrDefault(formId) + entry.Count;
        }
        return items;
    }
}
