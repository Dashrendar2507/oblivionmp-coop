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
    private Dictionary<string, string> _text = new(StringComparer.Ordinal);
    private int[] _toGame = [];
    private Dictionary<int, int> _fromGame = new();

    /// <summary>Local id (low 24 bits) of the dialogue plugin's beacon item.</summary>
    public uint DialogueBeacon { get { EnsureLoaded(); return _dialogueBeacon; } }

    private uint _dialogueBeacon;

    public bool HasText { get { EnsureLoaded(); return _text.Count > 0; } }

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
            _lines.Count, _npcs.Count, _text.Count, _text.Count == 0 ? " (no strings.json: names and lines show as keys)" : "");
    }

    private void LoadText()
    {
        var dir = Path.GetDirectoryName(typeof(GameData).Assembly.Location) ?? ".";
        var path = string.IsNullOrWhiteSpace(config.StringsFile) ? Path.Combine(dir, "strings.json") : Path.GetFullPath(config.StringsFile);
        try
        {
            // First start: make it from the host's own copy of the game.
            if (!File.Exists(path) && string.IsNullOrWhiteSpace(config.StringsFile))
                GameText.Build(path, config.GameFolder, config.Language, logger);
            if (File.Exists(path))
                _text = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? new();
        }
        catch (Exception e)
        {
            logger.LogWarning("[QuestSync] Could not read {Path}: {Error}", path, e.Message);
        }
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
    public string Name(string? key)
    {
        EnsureLoaded();
        if (string.IsNullOrEmpty(key))
            return "";
        if (_text.TryGetValue(key, out var text))
            return text;
        var bare = Regex.Replace(key, "^LOC_[A-Z]+_", "");
        return Regex.Replace(bare, "(?<=[a-z])(?=[A-Z0-9])", " ");
    }

    /// <summary>Text for a localization key, or null when there is none (dialogue lines, journal entries).</summary>
    public string? Text(string? key)
    {
        EnsureLoaded();
        if (string.IsNullOrEmpty(key))
            return null;
        if (_text.TryGetValue(key, out var text))
            return text;
        return key.StartsWith("LOC_", StringComparison.Ordinal) ? null : key;
    }

    /// <summary>A quest's display name.</summary>
    public string QuestName(string editorId)
    {
        var q = Map.Quest(editorId);
        var name = q?.Name is { } key ? Text(key) : null;
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
    public string? StageText(QuestStage stage)
        => Text(Map.Quest(stage.Quest)?.StageInfos.FirstOrDefault(s => s.Stage == stage.Stage)?.Text);
}
