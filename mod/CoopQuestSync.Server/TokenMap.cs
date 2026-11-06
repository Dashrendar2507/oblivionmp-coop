using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace CoopQuestSync.Server;

/// <summary>One vanilla quest stage and the hidden token item that stands for it.</summary>
public readonly record struct QuestStage(string Quest, int Stage)
{
    public override string ToString() => $"{Quest} {Stage}";
}

/// <summary>
/// The map from token items to quest stages, generated with the plugin (plugin/tools/gen_plugin.py writes
/// tokens.json, which is embedded in this DLL). Token ids here are local ids, the low 24 bits of the form id;
/// the top byte is the plugin's slot in each player's load order, which the server learns from the beacon item.
/// </summary>
public sealed class TokenMap
{
    public uint BeaconLocalId { get; }
    public uint SeqLocalId { get; }
    public uint BusyLocalId { get; }
    public uint AckLocalId { get; }
    /// <summary>Token ids from old test builds, [start, end). The server takes these out of inventories.</summary>
    public (uint Start, uint End) Legacy { get; }
    public IReadOnlyDictionary<string, QuestInfo> Quests => _quests;

    private readonly Dictionary<uint, QuestStage> _byToken = new();
    private readonly Dictionary<QuestStage, uint> _byStage = new();
    private readonly Dictionary<string, QuestInfo> _quests = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A quest the plugin mirrors. <see cref="Sync"/> is false for quests that stay local (the tutorial).</summary>
    public sealed record QuestInfo(string EditorId, string FormId, string? Name, bool Sync, IReadOnlyList<StageInfo> StageInfos)
    {
        public IEnumerable<int> Stages => StageInfos.Select(s => s.Stage);
    }

    /// <summary>One stage: its journal entry, and whether the plugin replays it (some stages teleport or play cut scenes).</summary>
    public sealed record StageInfo(int Stage, string? Text, bool Replay);

    /// <summary>The raw tokens.json, served to the web page as is.</summary>
    public string Json { get; private set; } = "{}";

    private TokenMap(JsonElement root)
    {
        BeaconLocalId = ParseHex(root.GetProperty("beacon").GetString()!);
        SeqLocalId = ParseHex(root.GetProperty("seq").GetString()!);
        BusyLocalId = ParseHex(root.GetProperty("busy").GetString()!);
        AckLocalId = ParseHex(root.GetProperty("ack").GetString()!);
        if (root.TryGetProperty("legacy", out var legacy))
            Legacy = (ParseHex(legacy[0].GetString()!), ParseHex(legacy[1].GetString()!));
        foreach (var q in root.GetProperty("quests").EnumerateArray())
        {
            var editorId = q.GetProperty("editorId").GetString()!;
            var stages = new List<StageInfo>();
            foreach (var s in q.GetProperty("stages").EnumerateArray())
            {
                var stage = new QuestStage(editorId, s.GetProperty("stage").GetInt32());
                var token = ParseHex(s.GetProperty("token").GetString()!);
                _byToken[token] = stage;
                _byStage[stage] = token;
                var text = s.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
                var replay = !s.TryGetProperty("replay", out var r) || r.GetBoolean();
                stages.Add(new StageInfo(stage.Stage, text, replay));
            }
            var name = q.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
            var sync = !q.TryGetProperty("sync", out var sy) || sy.GetBoolean();
            _quests[editorId] = new QuestInfo(editorId, q.GetProperty("formId").GetString()!, name, sync, stages);
        }
    }

    public int StageCount => _byToken.Count;

    /// <summary>Whether stages of this quest are shared between players.</summary>
    public bool IsShared(string quest) => _quests.TryGetValue(quest, out var q) && q.Sync;

    public bool TryGetStage(uint localId, out QuestStage stage) => _byToken.TryGetValue(localId, out stage);

    public bool TryGetToken(QuestStage stage, out uint localId) => _byStage.TryGetValue(stage, out localId);

    /// <summary>Finds the quest by editor id, ignoring case, and returns its canonical spelling.</summary>
    public bool TryNormalizeQuest(string quest, out string editorId)
    {
        if (_quests.TryGetValue(quest, out var info))
        {
            editorId = info.EditorId;
            return true;
        }
        editorId = quest;
        return false;
    }

    public static TokenMap LoadEmbedded()
    {
        var asm = Assembly.GetExecutingAssembly();
        var name = asm.GetManifestResourceNames().Single(n => n.EndsWith("tokens.json", StringComparison.Ordinal));
        using var stream = asm.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();
        using var doc = JsonDocument.Parse(json);
        return new TokenMap(doc.RootElement) { Json = json };
    }

    private static uint ParseHex(string text) => uint.Parse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
}
