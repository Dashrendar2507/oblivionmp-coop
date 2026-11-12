using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace CoopQuestSync.Server;

/// <summary>
/// Makes strings.json (the game's names, dialogue and journal text, by key) from the host's own copy of Oblivion
/// Remastered, the first time the server starts without one, when the game is installed on the server computer.
/// Each player's game also reads its own text (see the client's GameTextCache), so the overlay has words even when
/// the server has no game. The manual route still works: export Game.locres with FModel and run plugin/tools/locres.py.
/// </summary>
public static class GameText
{
    /// <summary>Writes strings.json at <paramref name="output"/>; false (with the reason logged) when it can't.</summary>
    public static bool Build(string output, string gameFolder, string language, ILogger logger)
    {
        var game = GameFiles.FindGame(gameFolder, logger);
        if (game is null)
        {
            logger.LogInformation("[QuestSync] Oblivion Remastered isn't installed on this computer (or set GameFolder in config.json), " +
                "so the web page shows names worked out from the game's ids. Players' overlays use their own game's text either way.");
            return false;
        }
        var all = GameFiles.ReadText(game, language, logger);
        if (all is null)
            return false;
        var keys = UsedKeys();
        var text = all.Where(kv => keys.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
        File.WriteAllText(output, JsonSerializer.Serialize(text), new UTF8Encoding(false));
        logger.LogInformation("[QuestSync] Wrote {Path}: {Found} of the {Wanted} texts the mod uses ({All} in the game's file).",
            output, text.Count, keys.Count, all.Count);
        return true;
    }

    /// <summary>The text keys the mod's tables use (LOC_...): the rest of the game's text isn't kept.</summary>
    public static HashSet<string> UsedKeys()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        void Walk(JsonElement e)
        {
            switch (e.ValueKind)
            {
                case JsonValueKind.String:
                    if (e.GetString() is { } s && s.StartsWith("LOC_", StringComparison.Ordinal))
                        keys.Add(s);
                    break;
                case JsonValueKind.Object:
                    foreach (var p in e.EnumerateObject())
                        Walk(p.Value);
                    break;
                case JsonValueKind.Array:
                    foreach (var x in e.EnumerateArray())
                        Walk(x);
                    break;
            }
        }
        foreach (var name in new[] { "tokens.json", "dialogue.json", "npcs.json" })
            if (TokenMap.ReadEmbedded(name) is { } json)
                Walk(JsonDocument.Parse(json).RootElement);
        return keys;
    }
}
