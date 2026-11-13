using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using ReadyM.SDK.Attributes;

namespace CoopQuestSync.Server;

/// <summary>
/// The generator's tables (dialogue lines, NPC names) and the game's text.
///
/// The remaster keeps its text out of the plugins: names and lines in the plugins are keys such as
/// LOC_FN_YsabelAndronicus, and the words live in the game's localization file. When a strings.json (key -> text,
/// made with plugin/tools/locres.py from the game's Game.locres) sits next to this DLL, the server shows real text;
/// otherwise it makes do with the key.
///
/// Form ids in the tables use the generator's numbering (its list of plugins). <see cref="ToGame"/> and
/// <see cref="FromGame"/> translate to the numbering of the players' load order (config GamePlugins).
/// </summary>
[Service]
public sealed partial class GameData(QuestSyncConfig config, ILogger logger)
{
    /// <summary>The quest tokens table (tokens.json).</summary>
    public TokenMap Map { get; } = TokenMap.LoadEmbedded();

    public sealed record Line(string Topic, string[] Lines, string? Speaker, string? Quest, (uint FormId, int Count)[] Gives, bool Takes);

    private readonly Dictionary<uint, Line> _lines = new();
    private readonly Dictionary<uint, string> _npcs = new();
    private readonly Dictionary<uint, string> _menus = new();
    private readonly Dictionary<uint, string> _cells = new();
    private readonly Dictionary<uint, string> _voices = new();
    private readonly Dictionary<string, string> _voicesByName = new();
    /// <summary>The host's file (coop-quest-sync-strings.json, made by hand or from the game), in config Language.</summary>
    private Dictionary<string, string> _text = new(StringComparer.Ordinal);
    /// <summary>Text learned from the players' games, by language (coop-quest-sync-strings.&lt;language&gt;.json).</summary>
    private readonly Dictionary<string, Dictionary<string, string>> _learned = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _unsaved = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string>? _pageText;
    private int[] _toGame = [];
    private Dictionary<int, int> _fromGame = new();

    /// <summary>Local id (low 24 bits) of the dialogue plugin's beacon item.</summary>
    public uint DialogueBeacon { get { EnsureLoaded(); return _dialogueBeacon; } }

    private uint _dialogueBeacon;

    public bool HasText { get { EnsureLoaded(); return PageText.Count > 0; } }

    private bool _loaded;

    private void Start() => EnsureLoaded();

    private void EnsureLoaded()
    {
        if (_loaded)
            return;
        _loaded = true;
        var map = Map;
        var order = config.GamePlugins.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        _toGame = new int[map.Masters.Count];
        for (var i = 0; i < map.Masters.Count; i++)
        {
            var at = order.FindIndex(p => p.Equals(map.Masters[i], StringComparison.OrdinalIgnoreCase));
            _toGame[i] = at < 0 ? -1 : at;
            if (at < 0)
                logger.LogWarning("[QuestSync] {Plugin} is not in GamePlugins; its items cannot be shared.", map.Masters[i]);
            else
                _fromGame[at] = i;
        }

        var dialogue = TokenMap.ReadEmbedded("dialogue.json");
        if (dialogue is not null)
        {
            using var doc = JsonDocument.Parse(dialogue);
            _dialogueBeacon = TokenMap.ParseHex(doc.RootElement.GetProperty("beacon").GetString()!);
            if (doc.RootElement.TryGetProperty("menus", out var menus))
                foreach (var p in menus.EnumerateObject())
                    _menus[TokenMap.ParseHex(p.Name)] = p.Value.GetString()!;
            foreach (var p in doc.RootElement.GetProperty("lines").EnumerateObject())
            {
                var v = p.Value;
                string? Str(string n) => v.TryGetProperty(n, out var x) && x.ValueKind == JsonValueKind.String ? x.GetString() : null;
                var gives = v.TryGetProperty("gives", out var g)
                    ? g.EnumerateArray().Select(e => (TokenMap.ParseHex(e[0].GetString()!), e[1].GetInt32())).ToArray()
                    : [];
                _lines[TokenMap.ParseHex(p.Name)] = new Line(Str("topic") ?? "", v.GetProperty("lines").EnumerateArray().Select(l => l.GetString()!).ToArray(),
                    Str("speaker"), Str("quest"), gives, v.TryGetProperty("takes", out _));
            }
        }

        var npcs = TokenMap.ReadEmbedded("npcs.json");
        if (npcs is not null)
            foreach (var p in JsonDocument.Parse(npcs).RootElement.EnumerateObject())
                _npcs[TokenMap.ParseHex(p.Name)] = p.Value.GetString()!;

        var voices = TokenMap.ReadEmbedded("voices.json");
        if (voices is not null)
        {
            var root = JsonDocument.Parse(voices).RootElement;
            foreach (var p in root.GetProperty("npcs").EnumerateObject())
                _voices[TokenMap.ParseHex(p.Name)] = p.Value.GetString()!;
            foreach (var p in root.GetProperty("names").EnumerateObject())
                _voicesByName[p.Name] = p.Value.GetString()!;
        }

        var cells = TokenMap.ReadEmbedded("cells.json");
        if (cells is not null)
            foreach (var p in JsonDocument.Parse(cells).RootElement.EnumerateObject())
                _cells[TokenMap.ParseHex(p.Name)] = p.Value.GetString()!;

        LoadText();
        logger.LogInformation("[QuestSync] Game data: {Lines} dialogue lines, {Npcs} names, {Text} text entries{Note}.",
            _lines.Count, _npcs.Count, PageText.Count, PageText.Count == 0 ? " (no " + KeptFileName + " or strings.json yet: names and lines show as keys until a player's game gives the text)" : "");
    }

    /// <summary>
    /// The game's text, kept next to the state file (the server's working folder) as coop-quest-sync-strings.json, so
    /// replacing the mod's folder on an update doesn't lose it (2026-11-07: names showed as ids after an update). An
    /// older strings.json next to the DLL is copied there. Without either, the server tries to make it from the host's
    /// game, which needs an Oodle library the Steam version doesn't ship; then it learns the text from the players'
    /// games instead (<see cref="TextLearning"/>), one file per language.
    /// </summary>
    private void LoadText()
    {
        var dir = Path.GetDirectoryName(typeof(GameData).Assembly.Location) ?? ".";
        var custom = !string.IsNullOrWhiteSpace(config.StringsFile);
        var path = custom ? Path.GetFullPath(config.StringsFile) : Path.GetFullPath(KeptFileName);
        try
        {
            var besideDll = Path.Combine(dir, "strings.json");
            if (!custom && !File.Exists(path) && File.Exists(besideDll))
            {
                File.Copy(besideDll, path);
                logger.LogInformation("[QuestSync] Copied the game's text to {Path}, where updates of the mod keep it.", path);
            }
            // First start: make it from the host's own copy of the game, unless the players' games already gave it.
            if (!File.Exists(path) && !custom && !File.Exists(LearnedPath(PageLanguage)))
                if (!GameText.Build(path, config.GameFolder, config.Language, logger))
                    logger.LogInformation("[QuestSync] The server will learn the game's text from the players' games instead, " +
                        "in each player's language, as they join ({Path}).", LearnedPath("<language>"));
            if (File.Exists(path))
            {
                _text = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? new();
                logger.LogInformation("[QuestSync] The game's text: {Path}", path);
            }
        }
        catch (Exception e)
        {
            logger.LogWarning("[QuestSync] Could not read {Path}: {Error}", path, e.Message);
        }
        LoadLearned();
    }

    /// <summary>Every coop-quest-sync-strings.&lt;language&gt;.json in the server's working folder.</summary>
    private void LoadLearned()
    {
        try
        {
            var folder = Path.GetDirectoryName(Path.GetFullPath(KeptFileName)) ?? ".";
            foreach (var file in Directory.GetFiles(folder, "coop-quest-sync-strings.*.json"))
            {
                var language = Path.GetFileName(file)["coop-quest-sync-strings.".Length..^".json".Length];
                if (!IsLanguage(language))
                    continue;
                try
                {
                    var text = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file)) ?? new();
                    _learned[language] = new Dictionary<string, string>(text, StringComparer.Ordinal);
                    logger.LogInformation("[QuestSync] The game's text in {Language} (from the players' games): {Path}, {Count} of {Wanted}.",
                        language, file, text.Count, UsedKeys.Count);
                }
                catch (Exception e)
                {
                    logger.LogWarning("[QuestSync] Could not read {Path}: {Error}", file, e.Message);
                }
            }
        }
        catch (Exception e)
        {
            logger.LogWarning("[QuestSync] Could not look for learned text: {Error}", e.Message);
        }
    }

    /// <summary>The game's text, in the server's working folder next to coop-quest-sync-state.json.</summary>
    public const string KeptFileName = "coop-quest-sync-strings.json";

    /// <summary>Where the text learned from the players' games in <paramref name="language"/> is kept.</summary>
    public static string LearnedPath(string language) => Path.GetFullPath($"coop-quest-sync-strings.{language}.json");

    /// <summary>A culture name the game uses ("en", "fr", "pt-BR", "zh-Hans"), safe in a file name.</summary>
    public static bool IsLanguage(string? value)
        => value is not null && Regex.IsMatch(value, "^[A-Za-z]{2,3}(-[A-Za-z0-9]{2,8}){0,2}$");

    /// <summary>The language of the host's file and of the web page (config Language).</summary>
    public string PageLanguage => config.Language?.Trim() is { } l && IsLanguage(l) ? l : "en";

    private HashSet<string>? _usedKeys;

    /// <summary>The text keys the mod's tables use (LOC_...).</summary>
    public HashSet<string> UsedKeys => _usedKeys ??= GameText.UsedKeys();

    /// <summary>
    /// The text the web page and the server's own lines use: the host's file, else what was learned in the page's
    /// language, else the language learned the most of.
    /// </summary>
    private Dictionary<string, string> PageText
    {
        get
        {
            EnsureLoaded();
            if (_pageText is not null)
                return _pageText;
            if (_text.Count > 0)
                return _pageText = _text;
            if (_learned.TryGetValue(PageLanguage, out var page) && page.Count > 0)
                return _pageText = page;
            return _pageText = _learned.Values.OrderByDescending(t => t.Count(kv => kv.Value.Length > 0)).FirstOrDefault()
                               ?? _text;
        }
    }

    /// <summary>Keys the server has no text for yet in <paramref name="language"/> (asked of a player of that language).</summary>
    public List<string> MissingText(string language)
    {
        EnsureLoaded();
        _learned.TryGetValue(language, out var learned);
        var host = SameLanguage(language, PageLanguage) ? _text : null;
        return UsedKeys.Where(k => (learned is null || !learned.ContainsKey(k)) && (host is null || !host.ContainsKey(k)))
            .OrderBy(k => k, StringComparer.Ordinal).ToList();
    }

    /// <summary>"en" and "EN" are the same; "en" and "en-GB" too (the game ships one English).</summary>
    public static bool SameLanguage(string a, string b)
        => string.Equals(a, b, StringComparison.OrdinalIgnoreCase)
           || string.Equals(a.Split('-')[0], b.Split('-')[0], StringComparison.OrdinalIgnoreCase)
              && (!a.Contains('-') || !b.Contains('-'));

    /// <summary>Takes in text a player's game looked up; an empty text marks a key the game has none for.</summary>
    public int Learn(string language, IEnumerable<KeyValuePair<string, string>> entries)
    {
        EnsureLoaded();
        if (!_learned.TryGetValue(language, out var learned))
            _learned[language] = learned = new Dictionary<string, string>(StringComparer.Ordinal);
        var added = 0;
        foreach (var (key, value) in entries)
            if (UsedKeys.Contains(key) && learned.TryAdd(key, value))
                added++;
        if (added > 0)
        {
            _unsaved.Add(language);
            _pageText = null;
            _webMap = null;
        }
        return added;
    }

    /// <summary>How much text is known in <paramref name="language"/>: (keys answered, keys with words).</summary>
    public (int Answered, int Found) Learned(string language)
        => _learned.TryGetValue(language, out var t) ? (t.Count, t.Count(kv => kv.Value.Length > 0)) : (0, 0);

    /// <summary>Writes the learned text that changed since the last save.</summary>
    public void SaveLearned()
    {
        foreach (var language in _unsaved.ToList())
        {
            var path = LearnedPath(language);
            try
            {
                var temp = path + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(_learned[language]), new System.Text.UTF8Encoding(false));
                File.Move(temp, path, overwrite: true);
                _unsaved.Remove(language);
            }
            catch (Exception e)
            {
                logger.LogWarning("[QuestSync] Could not write {Path}: {Error}", path, e.Message);
            }
        }
    }

    /// <summary>The text for a key in a language: what that language's players gave, else the page's text.</summary>
    private string? Lookup(string key, string? language)
    {
        EnsureLoaded();
        if (language is not null)
        {
            foreach (var (lang, learned) in _learned)
                if (SameLanguage(lang, language) && learned.TryGetValue(key, out var mine) && mine.Length > 0)
                    return mine;
            if (SameLanguage(language, PageLanguage) && _text.TryGetValue(key, out var host) && host.Length > 0)
                return host;
        }
        return PageText.TryGetValue(key, out var text) && text.Length > 0 ? text : null;
    }

    /// <summary>A few random topic lines that name their speaker, for test conversations.</summary>
    public List<Line> SampleLines(int count)
    {
        EnsureLoaded();
        // Lines with a known voice, so the test can play it.
        var pool = _lines.Values.Where(l => l.Speaker is not null && l.Lines.Length > 0 && l.Quest is not null
            && VoiceOfName(l.Speaker) is not null && VoiceLine(l, l.Lines[0]) is not null).ToList();
        if (pool.Count == 0)
            pool = _lines.Values.ToList();
        if (pool.Count == 0)
            return [];
        var start = Random.Shared.Next(pool.Count);
        var speaker = pool[start].Speaker;
        var same = pool.Where(l => l.Speaker == speaker).Take(count).ToList();
        return same.Count >= 2 ? same : pool.OrderBy(_ => Random.Shared.Next()).Take(count).ToList();
    }

    /// <summary>
    /// The menu a dialogue plugin token stands for ("barter", "persuade", "lockpick"...), by its local id; null for
    /// other items. The plugin keeps the token in the inventory while that menu is open.
    /// </summary>
    public string? MenuOf(uint localId)
    {
        EnsureLoaded();
        return _menus.TryGetValue(localId, out var menu) ? menu : null;
    }

    public bool TryGetLine(uint localId, out Line line)
    {
        EnsureLoaded();
        return _lines.TryGetValue(localId, out line!);
    }

    /// <summary>A form id from the tables, in the players' load order; null when its plugin is not loaded.</summary>
    public uint? ToGame(uint formId)
    {
        EnsureLoaded();
        var i = (int)(formId >> 24);
        if (i >= _toGame.Length || _toGame[i] < 0)
            return null;
        return (uint)_toGame[i] << 24 | formId & 0x00FFFFFF;
    }

    /// <summary>A form id from the game, in the tables' numbering; null when it is from a plugin the tables lack.</summary>
    public uint? FromGame(uint formId)
    {
        EnsureLoaded();
        return _fromGame.TryGetValue((int)(formId >> 24), out var i) ? (uint)i << 24 | formId & 0x00FFFFFF : null;
    }

    /// <summary>
    /// An area id as text: "L_Tamriel", "L_PersistentDungeon-80A" (AreaId.ToString() wraps it in "AreaId[...]").
    /// </summary>
    public static string AreaKey(ReadyM.Api.Idents.AreaId area)
    {
        var s = area.ToString();
        return s.StartsWith("AreaId[", StringComparison.Ordinal) && s.EndsWith(']') ? s[7..^1] : s;
    }

    public static bool IsInterior(string areaKey) => areaKey.StartsWith("L_PersistentDungeon", StringComparison.Ordinal);

    /// <summary>
    /// The editor id of the interior an area names, for the console's coc command; null for an exterior or a cell the
    /// tables don't know. OblivionMP names an interior "L_PersistentDungeon-" + the cell's form id in hexadecimal, in
    /// the players' load order (checked against OblivionAreaIds.BuildInterior).
    /// </summary>
    public string? InteriorCell(string areaKey)
    {
        EnsureLoaded();
        var m = Regex.Match(areaKey, "^L_PersistentDungeon-([0-9A-Fa-f]{1,8})$");
        if (!m.Success || !uint.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.HexNumber, null, out var formId))
            return null;
        return FromGame(formId) is { } id && _cells.TryGetValue(id, out var cell) ? cell : null;
    }

    /// <summary>The name of an NPC or creature, by its base form id in the game's numbering.</summary>
    public string? NpcName(uint gameBaseId)
        => FromGame(gameBaseId) is { } id && _npcs.TryGetValue(id, out var key) ? Name(key) : null;

    /// <summary>An NPC's name key (LOC_FN_...), by its base form id in the game's numbering.</summary>
    public string? NpcKey(uint gameBaseId)
        => FromGame(gameBaseId) is { } id && _npcs.TryGetValue(id, out var key) ? key : null;

    /// <summary>A quest's name key, by its editor id.</summary>
    public string? QuestKey(string editorId) => Map.Quest(editorId)?.Name;

    /// <summary>An NPC's voice ("imperial_m"), by its base form id in the game's numbering.</summary>
    public string? VoiceOf(uint gameBaseId)
        => FromGame(gameBaseId) is { } id && _voices.TryGetValue(id, out var voice) ? voice : null;

    /// <summary>An NPC's voice by their name key (LOC_FN_...), for a line whose speaker is only known by name.</summary>
    public string? VoiceOfName(string? nameKey)
        => nameKey is not null && _voicesByName.TryGetValue(nameKey, out var voice) ? voice : null;

    /// <summary>
    /// The voice file of one spoken line, without the speaker's voice: "&lt;quest&gt;_&lt;topic&gt;_&lt;line form id&gt;_&lt;n&gt;",
    /// as the game names it (Play_imperial_m_ms26_ms26filecharges_000363be_1). Read from the line's text key
    /// (LOC_RT_&lt;topic&gt;_&lt;form id&gt;_&lt;plugin&gt;_RESP&lt;n&gt;). Only lines of Oblivion.esm for now: the others keep their
    /// voices in their own plugin's folder, with a numbering not checked yet.
    /// </summary>
    public static string? VoiceLine(Line line, string textKey)
    {
        if (line.Quest is null)
            return null;
        var m = Regex.Match(textKey, "^LOC_RT_(.+)_([0-9A-Fa-f]{8})_Oblivion_RESP([0-9]+)$");
        if (!m.Success)
            return null;
        var name = $"{line.Quest}_{m.Groups[1].Value}_{m.Groups[2].Value}_{m.Groups[3].Value}".ToLowerInvariant();
        return Regex.IsMatch(name, "^[a-z0-9_]{1,120}$") ? name : null;
    }

    /// <summary>Text for a localization key; for a name without text, the key made readable.</summary>
    public string Name(string? key, string? language = null)
    {
        EnsureLoaded();
        if (string.IsNullOrEmpty(key))
            return "";
        if (Lookup(key, language) is { } text)
            return text;
        var bare = Regex.Replace(key, "^LOC_[A-Z]+_", "");
        return Regex.Replace(bare, "(?<=[a-z])(?=[A-Z0-9])", " ");
    }

    /// <summary>Text for a localization key, or null when there is none (dialogue lines, journal entries).</summary>
    public string? Text(string? key, string? language = null)
    {
        EnsureLoaded();
        if (string.IsNullOrEmpty(key))
            return null;
        if (Lookup(key, language) is { } text)
            return text;
        return key.StartsWith("LOC_", StringComparison.Ordinal) ? null : key;
    }

    /// <summary>A quest's display name.</summary>
    public string QuestName(string editorId, string? language = null)
    {
        var q = Map.Quest(editorId);
        var name = q?.Name is { } key ? Text(key, language) : null;
        return name ?? editorId;
    }

    private string? _webMap;

    /// <summary>tokens.json for the web page, with quest names and journal entries in words where the text is known.</summary>
    public string WebMapJson()
    {
        if (_webMap is not null)
            return _webMap;
        EnsureLoaded();
        var root = System.Text.Json.Nodes.JsonNode.Parse(Map.Json)!.AsObject();
        foreach (var q in root["quests"]!.AsArray())
        {
            var key = q!["name"]?.GetValue<string>();
            q["name"] = key is null ? null : Text(key) ?? Name(key);
            foreach (var st in q["stages"]!.AsArray())
                st!["text"] = Text(st["text"]?.GetValue<string>());
        }
        return _webMap = root.ToJsonString();
    }

    /// <summary>A stage's journal entry, or null.</summary>
    public string? StageText(QuestStage stage, string? language = null)
        => Text(Map.Quest(stage.Quest)?.StageInfos.FirstOrDefault(s => s.Stage == stage.Stage)?.Text, language);
}
