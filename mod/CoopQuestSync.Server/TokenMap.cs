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
    public IReadOnlyDictionary<string, QuestInfo> Quests => _quests;

    private readonly Dictionary<uint, QuestStage> _byToken = new();
    private readonly Dictionary<QuestStage, uint> _byStage = new();
    private readonly Dictionary<string, QuestInfo> _quests = new(StringComparer.OrdinalIgnoreCase);

    public sealed record QuestInfo(string EditorId, string FormId, string? Name, IReadOnlyList<int> Stages);

    private TokenMap(JsonElement root)
    {
        BeaconLocalId = ParseHex(root.GetProperty("beacon").GetString()!);
        SeqLocalId = ParseHex(root.GetProperty("seq").GetString()!);
        foreach (var q in root.GetProperty("quests").EnumerateArray())
        {
            var editorId = q.GetProperty("editorId").GetString()!;
            var stages = new List<int>();
            foreach (var s in q.GetProperty("stages").EnumerateArray())
            {
                var stage = new QuestStage(editorId, s.GetProperty("stage").GetInt32());
                var token = ParseHex(s.GetProperty("token").GetString()!);
                _byToken[token] = stage;
                _byStage[stage] = token;
                stages.Add(stage.Stage);
            }
            var name = q.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
            _quests[editorId] = new QuestInfo(editorId, q.GetProperty("formId").GetString()!, name, stages);
        }
    }

    public int StageCount => _byToken.Count;

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
        using var doc = JsonDocument.Parse(stream);
        return new TokenMap(doc.RootElement);
    }

    private static uint ParseHex(string text) => uint.Parse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
}
