using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using OblivionMp.Sdk;
using ReadyM.SDK.Attributes;

namespace CoopQuestSync.Client;

/// <summary>
/// The game's own text (names, dialogue, quest names), read from this player's install in the language their game
/// is set to, so the conversation overlay shows words even when the server has no copy of the game, and each
/// player reads them in their own language. The server sends the text keys with every line; this swaps in the text.
///
/// The language comes from the game itself: the CoopQuestSyncUI add-on writes it to language.txt. Without the
/// add-on, the language in the game's settings file, then Windows' language, then English.
/// </summary>
[Service]
public sealed partial class GameTextCache(ILogger logger)
{
    private volatile Dictionary<string, string>? _text;
    private float _waited;
    private bool _started;

    private static string AddonFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ReadyM.Launcher", "Oblivion", "Mods", "CoopQuestSyncUI");

    private static readonly DateTime StartedAt = DateTime.Now;

    /// <summary>Waits a few seconds for the add-on to say the game's language, then reads the text in the background.</summary>
    private void Update()
    {
        if (_started)
            return;
        _waited += Time.DeltaTime;
        var fromAddon = AddonLanguage();
        if (fromAddon is null && _waited < 15f)
            return;
        _started = true;
        var languages = Candidates(fromAddon);
        Task.Run(() => Load(languages));
    }

    private void Load(List<string> languages)
    {
        try
        {
            var game = GameFiles.GameAbove(Environment.ProcessPath) ?? GameFiles.GameAbove(AppContext.BaseDirectory)
                       ?? GameFiles.FindGame("", logger);
            if (game is null)
            {
                logger.LogWarning("[QuestSync] Couldn't find this game's folder to read its text; the overlay uses the server's.");
                return;
            }
            logger.LogInformation("[QuestSync] Game text: trying {Languages} in {Game}.", string.Join(", ", languages), game);
            _text = GameFiles.ReadText(game, languages, logger);
            if (_text is not null)
                logger.LogInformation("[QuestSync] Game text ready: {Count} entries.", _text.Count);
        }
        catch (Exception e)
        {
            logger.LogWarning("[QuestSync] Couldn't read the game's text: {Error}", e.Message);
        }
    }

    /// <summary>The culture the game runs in, as the add-on wrote it this session (such as "fr" or "pt-BR").</summary>
    private static string? AddonLanguage()
    {
        try
        {
            var path = Path.Combine(AddonFolder, "language.txt");
            if (!File.Exists(path) || File.GetLastWriteTime(path) < StartedAt.AddMinutes(-2))
                return null;
            var value = File.ReadAllText(path).Trim();
            return Regex.IsMatch(value, "^[A-Za-z]{2,3}(-[A-Za-z0-9]{2,8})*$") ? value : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Folder names to try under Localization/Game, best first: "zh-Hans-CN" gives zh-Hans-CN, zh-Hans, zh.</summary>
    private static List<string> Candidates(string? fromAddon)
    {
        var list = new List<string>();
        void Add(string? culture)
        {
            if (string.IsNullOrWhiteSpace(culture))
                return;
            for (var c = culture.Trim(); c.Length > 0; c = c.Contains('-') ? c[..c.LastIndexOf('-')] : "")
                if (!list.Contains(c, StringComparer.OrdinalIgnoreCase))
                    list.Add(c);
        }
        Add(fromAddon);
        Add(SettingsCulture());
        Add(CultureInfo.CurrentUICulture.Name);
        Add("en");
        return list;
    }

    /// <summary>Culture=... in the game's GameUserSettings.ini, when the player picked a language in the game.</summary>
    private static string? SettingsCulture()
    {
        try
        {
            var saved = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OblivionRemastered", "Saved", "Config");
            if (!Directory.Exists(saved))
                return null;
            foreach (var ini in Directory.GetFiles(saved, "GameUserSettings.ini", SearchOption.AllDirectories))
                foreach (var line in File.ReadLines(ini))
                {
                    var m = Regex.Match(line, @"^\s*Culture\s*=\s*([A-Za-z0-9-]+)\s*$");
                    if (m.Success)
                        return m.Groups[1].Value;
                }
        }
        catch { /* not readable: next guess */ }
        return null;
    }

    private string? Text(string? key)
        => key is not null && _text is { } text && text.TryGetValue(key, out var value) && value.Length > 0 ? value : null;

    /// <summary>
    /// A conversation message from the server with this player's text swapped in where their game has it:
    /// "npc" (npcKey), "speaker" (speakerKey), "topic" (topicKey), "quest" (questKey) and each of "lines" (keys).
    /// </summary>
    public string Localize(string json)
    {
        if (_text is null)
            return json;
        try
        {
            if (JsonNode.Parse(json) is not JsonObject o)
                return json;
            foreach (var (field, keyField) in new[] { ("npc", "npcKey"), ("speaker", "speakerKey"), ("topic", "topicKey"), ("quest", "questKey") })
                if (Text(o[keyField]?.GetValue<string>()) is { } text)
                    o[field] = text;
            if (o["keys"] is JsonArray keys && o["lines"] is JsonArray lines)
                for (var i = 0; i < keys.Count && i < lines.Count; i++)
                    if (Text(keys[i]?.GetValue<string>()) is { } text)
                        lines[i] = text;
            return o.ToJsonString();
        }
        catch (Exception e)
        {
            logger.LogWarning("[QuestSync] Couldn't use the game's text for a line: {Error}", e.Message);
            return json;
        }
    }
}
