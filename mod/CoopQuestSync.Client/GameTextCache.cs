using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using OblivionMP.SDK;
using ReadyM.SDK.Attributes;

namespace CoopQuestSync.Client;

/// <summary>
/// The game's own text (names, dialogue, quest names), read from this player's install in the language their game
/// is set to, so the conversation overlay shows words even when the server has no copy of the game, and each
/// player reads them in their own language. The server sends the text keys with every line; this swaps in the text.
///
/// The language comes from the game itself: the CoopQuestSyncUI add-on writes it to language.txt. Without the
/// add-on, the language in the game's settings file, then Windows' language, then English.
///
/// The remaster's own .pak keeps its text Oodle-compressed, which this mod can only read where an Oodle library is
/// on the computer. So the add-on also looks text up in the running game, which has it loaded in its language:
/// this side writes the keys it lacks to text-request.txt, the add-on answers in text-reply.txt.
/// </summary>
[Service]
public sealed partial class GameTextCache(ILogger logger)
{
    private volatile Dictionary<string, string>? _text;
    /// <summary>Text the add-on looked up in the running game; an empty value: the game has none for that key.</summary>
    private readonly Dictionary<string, string> _fromGame = new(StringComparer.Ordinal);
    private readonly HashSet<string> _asked = new(StringComparer.Ordinal);
    private float _clock;
    private float _pollIn;
    private float _firstAskAt = -1;
    private bool _addonAnswers;
    private bool _addonSilent;
    private float _waited;
    private bool _started;

    /// <summary>Set by the mod's entry point: the RPCs to the server (resolved late, the RPC class needs this one).</summary>
    public static Func<ClientRpc>? Rpc;

    /// <summary>The language this game runs in, once known (the add-on's, else the settings' or Windows' guess).</summary>
    private string? _language;
    private bool _languageWanted;
    /// <summary>Keys the server asked for (TextWanted), until they are all looked up and sent back.</summary>
    private List<string>? _serverWants;
    private float _serverWantsAt;

    private static string AddonFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ReadyM.Launcher", "Oblivion", "Mods", "CoopQuestSyncUI");

    private static readonly DateTime StartedAt = DateTime.Now;

    /// <summary>Waits a few seconds for the add-on to say the game's language, then reads the text in the background.</summary>
    private void Update()
    {
        _clock += Time.DeltaTime;
        _pollIn -= Time.DeltaTime;
        if (_pollIn <= 0)
        {
            _pollIn = 0.1f;
            ReadReplies();
            AnswerServer(false);
        }
        if (_started)
            return;
        _waited += Time.DeltaTime;
        var fromAddon = AddonLanguage();
        if (fromAddon is null && _waited < 15f)
            return;
        _started = true;
        var languages = Candidates(fromAddon);
        _language = languages[0];
        if (_languageWanted)
            SendLanguage();
        Task.Run(() => Load(languages));
    }

    /// <summary>
    /// The server asks for the game's text (keys, one per line), to keep for its web page and chat lines; an empty
    /// request asks only which language this game runs in. The add-on looks the keys up in the running game.
    /// </summary>
    public void ServerWants(string keys)
    {
        _languageWanted = true;
        if (_language is null)
            return; // answered once the language is known (no text before that either)
        SendLanguage();
        var list = keys.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(k => k.StartsWith("LOC_", StringComparison.Ordinal) && k.Length < 200).Distinct().Take(1000).ToList();
        if (list.Count == 0)
            return;
        _serverWants = list;
        _serverWantsAt = _clock;
        if (_text is null && !_addonSilent)
        {
            // Asked again even when asked before: a reply may have been lost.
            var ask = list.Where(k => !_fromGame.ContainsKey(k)).ToList();
            foreach (var k in ask)
                _asked.Add(k);
            WriteRequest(ask);
        }
        AnswerServer(false);
    }

    private void SendLanguage()
    {
        try
        {
            Rpc?.Invoke().SendTextLanguage(_language!);
        }
        catch (Exception e)
        {
            logger.LogWarning("[QuestSync] Couldn't tell the server the game's language: {Error}", e.Message);
        }
    }

    /// <summary>Sends the server the text it asked for, once every key is looked up (or after 20 s, what there is).</summary>
    private void AnswerServer(bool force)
    {
        if (_serverWants is not { } wants || _language is null)
            return;
        var late = _clock - _serverWantsAt > 20f;
        var text = new Dictionary<string, string>(StringComparer.Ordinal);
        string? error = null;
        if (_text is { } files)
            foreach (var k in wants)
                text[k] = files.TryGetValue(k, out var v) ? v : "";
        else if (_addonSilent)
            error = "the CoopQuestSyncUI add-on doesn't answer";
        else
        {
            foreach (var k in wants)
                if (_fromGame.TryGetValue(k, out var v))
                    text[k] = v;
            if (text.Count < wants.Count && !late && !force)
                return;
            if (text.Count == 0)
                error = "the CoopQuestSyncUI add-on didn't answer in time";
        }
        _serverWants = null;
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(new { language = _language, text, error });
            Rpc?.Invoke().SendTextFound(json);
            logger.LogInformation("[QuestSync] Sent the server {Count} of {Wanted} game text(s) in {Language}{Error}.",
                text.Count(kv => kv.Value.Length > 0), wants.Count, _language, error is null ? "" : $" ({error})");
        }
        catch (Exception e)
        {
            logger.LogWarning("[QuestSync] Couldn't send the server the game's text: {Error}", e.Message);
        }
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
    {
        if (key is null)
            return null;
        if (_text is { } text && text.TryGetValue(key, out var value) && value.Length > 0)
            return value;
        return _fromGame.TryGetValue(key, out var game) && game.Length > 0 ? game : null;
    }

    private static readonly string[] KeyFields = ["npcKey", "speakerKey", "topicKey", "questKey"];

    /// <summary>
    /// The text keys of a message from the server that nobody has looked up yet. Empty when the game's files were
    /// read, or when the add-on doesn't answer (not installed): then there is nothing to wait for.
    /// </summary>
    public List<string> Missing(string json)
    {
        var missing = new List<string>();
        if (_text is not null || _addonSilent)
            return missing;
        try
        {
            if (JsonNode.Parse(json) is not JsonObject o)
                return missing;
            var keys = KeyFields.Select(f => o[f]?.GetValue<string>()).ToList();
            if (o["keys"] is JsonArray array)
                keys.AddRange(array.Select(k => k?.GetValue<string>()));
            foreach (var key in keys)
                if (key is not null && key.StartsWith("LOC_", StringComparison.Ordinal) && !_fromGame.ContainsKey(key) && !missing.Contains(key))
                    missing.Add(key);
        }
        catch
        {
            // not one of ours: nothing to wait for
        }
        return missing;
    }

    /// <summary>Asks the add-on for these keys (once each).</summary>
    public void Request(IEnumerable<string> keys)
    {
        WriteRequest(keys.Where(k => _asked.Add(k)).ToList());
    }

    private void WriteRequest(List<string> fresh)
    {
        if (fresh.Count == 0)
            return;
        try
        {
            if (!Directory.Exists(AddonFolder))
            {
                _addonSilent = true;
                return;
            }
            File.AppendAllText(Path.Combine(AddonFolder, "text-request.txt"), string.Join("\n", fresh) + "\n");
            if (_firstAskAt < 0)
                _firstAskAt = _clock;
        }
        catch (Exception e)
        {
            logger.LogWarning("[QuestSync] Couldn't ask the add-on for the game's text: {Error}", e.Message);
        }
    }

    /// <summary>The add-on writes a line break as \n and a backslash as \\.</summary>
    private static string Unescape(string s)
    {
        if (!s.Contains('\\'))
            return s;
        var b = new System.Text.StringBuilder(s.Length);
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] == '\\' && i + 1 < s.Length)
            {
                i++;
                b.Append(s[i] == 'n' ? '\n' : s[i]);
            }
            else
                b.Append(s[i]);
        }
        return b.ToString();
    }

    /// <summary>Takes in the add-on's answers.</summary>
    private void ReadReplies()
    {
        if (_firstAskAt >= 0 && !_addonAnswers && !_addonSilent && _clock - _firstAskAt > 6f)
        {
            _addonSilent = true;
            logger.LogWarning("[QuestSync] The CoopQuestSyncUI add-on doesn't answer text requests (not installed, or an older one): the overlay shows the server's text.");
        }
        var reply = Path.Combine(AddonFolder, "text-reply.txt");
        var taken = Path.Combine(AddonFolder, "text-reply.read");
        try
        {
            if (!File.Exists(reply))
                return;
            File.Move(reply, taken, overwrite: true);
            int found = 0, total = 0;
            foreach (var line in File.ReadAllLines(taken))
            {
                var tab = line.IndexOf('\t');
                if (tab <= 0)
                    continue;
                var text = Unescape(line[(tab + 1)..]);
                _fromGame[line[..tab]] = text;
                total++;
                if (text.Length > 0)
                    found++;
            }
            File.Delete(taken);
            if (!_addonAnswers)
                logger.LogInformation("[QuestSync] The game's text comes from the running game (CoopQuestSyncUI add-on): {Found} of {Total} found.", found, total);
            _addonAnswers = true;
            _addonSilent = false;
        }
        catch (IOException)
        {
            // the add-on is writing it: next time
        }
        catch (Exception e)
        {
            logger.LogWarning("[QuestSync] Couldn't read the add-on's text: {Error}", e.Message);
        }
    }

    /// <summary>
    /// A conversation message from the server with this player's text swapped in where their game has it:
    /// "npc" (npcKey), "speaker" (speakerKey), "topic" (topicKey), "quest" (questKey) and each of "lines" (keys).
    /// </summary>
    public string Localize(string json)
    {
        if (_text is null && _fromGame.Count == 0)
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
