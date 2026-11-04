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
            if (File.Exists(path))
                _text = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? new();
        }
        catch (Exception e)
        {
            logger.LogWarning("[QuestSync] Could not read {Path}: {Error}", path, e.Message);
        }
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

    /// <summary>The name of an NPC or creature, by its base form id in the game's numbering.</summary>
    public string? NpcName(uint gameBaseId)
        => FromGame(gameBaseId) is { } id && _npcs.TryGetValue(id, out var key) ? Name(key) : null;

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
