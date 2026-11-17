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
    public uint FreezeLocalId { get; }
    /// <summary>The new-player start's items (0 when the plugin is older than 0.6). See <see cref="NewStart"/>.</summary>
    public uint StartOfferLocalId { get; }
    public uint StartPrisonLocalId { get; }
    public uint StartDoneLocalId { get; }
    public uint IntroLocalId { get; }
    public uint StartKitLocalId { get; }
    /// <summary>Category -> local id of its CQSAllow item.</summary>
    public IReadOnlyDictionary<string, uint> AllowLocalIds => _allow;
    /// <summary>Plugins the generator read, in its numbering: the top byte of a form id in the tables is an index here.</summary>
    public IReadOnlyList<string> Masters { get; } = ["Oblivion.esm"];
    /// <summary>Items quests mark as quest items (keys, letters...), in the generator's numbering.</summary>
    public IReadOnlySet<uint> QuestItems => _questItems;
    /// <summary>Token ids from old test builds, [start, end). The server takes these out of inventories.</summary>
    public (uint Start, uint End) Legacy { get; }
    public IReadOnlyDictionary<string, QuestInfo> Quests => _quests;
    /// <summary>CQSDead tokens (local id) to the actor a vanilla script kills (its ref id in Oblivion.esm).</summary>
    public IReadOnlyDictionary<uint, (uint Ref, string? EditorId)> Deaths => _deaths;

    private readonly Dictionary<uint, QuestStage> _byToken = new();
    private readonly Dictionary<QuestStage, uint> _byStage = new();
    private readonly Dictionary<string, QuestInfo> _quests = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, uint> _allow = new();
    private readonly HashSet<uint> _questItems = new();
    private readonly Dictionary<uint, (uint, string?)> _deaths = new();

    /// <summary>A quest the plugin mirrors. <see cref="Sync"/> is false for quests that stay local (the tutorial).</summary>
    public sealed record QuestInfo(string EditorId, string FormId, string? Name, bool Sync, IReadOnlyList<StageInfo> StageInfos)
    {
        public IEnumerable<int> Stages => StageInfos.Select(s => s.Stage);
    }

    /// <summary>One stage: its journal entry, whether the plugin replays it, and the kinds of effect its script has.</summary>
    public sealed record StageInfo(int Stage, string? Text, bool Replay, IReadOnlyList<string> Effects);

    /// <summary>The raw tokens.json, served to the web page as is.</summary>
    public string Json { get; private set; } = "{}";

    private TokenMap(JsonElement root)
    {
        BeaconLocalId = ParseHex(root.GetProperty("beacon").GetString()!);
        SeqLocalId = ParseHex(root.GetProperty("seq").GetString()!);
        BusyLocalId = ParseHex(root.GetProperty("busy").GetString()!);
        AckLocalId = ParseHex(root.GetProperty("ack").GetString()!);
        if (root.TryGetProperty("freeze", out var freeze))
            FreezeLocalId = ParseHex(freeze.GetString()!);
        if (root.TryGetProperty("newStart", out var start))
        {
            StartOfferLocalId = ParseHex(start.GetProperty("offer").GetString()!);
            StartPrisonLocalId = ParseHex(start.GetProperty("prison").GetString()!);
            StartDoneLocalId = ParseHex(start.GetProperty("done").GetString()!);
            IntroLocalId = ParseHex(start.GetProperty("intro").GetString()!);
            StartKitLocalId = ParseHex(start.GetProperty("kit").GetString()!);
        }
        if (root.TryGetProperty("allow", out var allow))
            foreach (var p in allow.EnumerateObject())
                _allow[p.Name] = ParseHex(p.Value.GetString()!);
        if (root.TryGetProperty("masters", out var masters))
            Masters = masters.EnumerateArray().Select(m => m.GetString()!).ToList();
        if (root.TryGetProperty("questItems", out var qi))
            foreach (var x in qi.EnumerateArray())
                _questItems.Add(ParseHex(x.GetString()!));
        if (root.TryGetProperty("deaths", out var deaths))
            foreach (var d in deaths.EnumerateArray())
                _deaths[ParseHex(d.GetProperty("token").GetString()!)] = (ParseHex(d.GetProperty("ref").GetString()!),
                    d.TryGetProperty("editorId", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null);
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
                var effects = s.TryGetProperty("effects", out var ef) ? ef.EnumerateArray().Select(e => e.GetString()!).ToList() : [];
                stages.Add(new StageInfo(stage.Stage, text, replay, effects));
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
        var json = ReadEmbedded("tokens.json")!;
        using var doc = JsonDocument.Parse(json);
        return new TokenMap(doc.RootElement) { Json = json };
    }

    /// <summary>A text file embedded in this DLL (the generator's tables), or null.</summary>
    public static string? ReadEmbedded(string fileName)
    {
        var asm = Assembly.GetExecutingAssembly();
        var name = asm.GetManifestResourceNames().SingleOrDefault(n => n.EndsWith("." + fileName, StringComparison.Ordinal));
        if (name is null)
            return null;
        using var stream = asm.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public QuestInfo? Quest(string editorId) => _quests.GetValueOrDefault(editorId);

    public static uint ParseHex(string text) => uint.Parse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
}
