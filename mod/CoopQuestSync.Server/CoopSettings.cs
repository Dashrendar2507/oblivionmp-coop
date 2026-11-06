using System.Text.Json;
using System.Text.Json.Serialization;

namespace CoopQuestSync.Server;

/// <summary>
/// Every switch on the web page. Kept in the state file next to the shared progress, so a restart keeps them.
/// The defaults are what a fresh server starts with.
/// </summary>
public sealed class CoopSettings
{
    /// <summary>The effect categories of stage scripts, in the plugin's order (scriptpatch.CATEGORIES).</summary>
    public static readonly string[] Categories = ["teleport", "combat", "items", "status", "ranks", "cutscene"];

    // ---- Quest progress ----

    /// <summary>"anyone": a stage anyone reaches counts for everybody. "leader": only the leader's stages are shared.</summary>
    public string ProgressFrom { get; set; } = "anyone";

    /// <summary>The party leader's nickname, or empty for none. Used by "leader only" progress and by respawns.</summary>
    public string Leader { get; set; } = "";

    /// <summary>Effects of a stage that reaches a player while they play (someone else just advanced it).</summary>
    public Dictionary<string, bool> Live { get; set; } = Categories.ToDictionary(c => c, _ => true);

    /// <summary>Effects of the stages a player catches up on when they join (what the party did without them).</summary>
    public Dictionary<string, bool> Join { get; set; } = Categories.ToDictionary(c => c, c => c is "items" or "status" or "ranks");

    /// <summary>
    /// Effects of a player's own stages, which the plugin sets again every time they join (the game forgets quests
    /// between sessions, OblivionMP keeps the inventory). Items are put back by the reward undo anyway.
    /// </summary>
    public Dictionary<string, bool> Rejoin { get; set; } = Categories.ToDictionary(c => c, c => c is "ranks");

    /// <summary>Waits until a player is out of a conversation before giving them new stages.</summary>
    public bool HoldDuringDialogue { get; set; } = true;

    /// <summary>"Alice advanced The Quest: journal text" in everyone's chat.</summary>
    public bool QuestMessages { get; set; } = true;

    // ---- Conversations ----

    /// <summary>Who sees someone's conversation: "everyone", "area" (same interior or exterior space) or "off".</summary>
    public string ConversationOverlay { get; set; } = "everyone";

    /// <summary>
    /// "auto": whoever may see a conversation is shown it. "ask": they get a banner and press the follow key to
    /// listen in. The follow key while watching always leaves.
    /// </summary>
    public string ConversationFollow { get; set; } = "auto";

    /// <summary>Watchers hear the NPC's voice with each line (played by the CoopQuestSyncUI add-on).</summary>
    public bool WitnessVoices { get; set; } = true;

    /// <summary>Players offered or watching a conversation can travel to the talker with the join key.</summary>
    public bool ConversationTeleport { get; set; } = true;

    /// <summary>Who stands still while someone talks: "area", "everyone" or "off". Players in combat are never frozen.</summary>
    public string ConversationFreeze { get; set; } = "area";

    /// <summary>When a line of dialogue hands the talker an item (a reward), the others get it too.</summary>
    public bool ShareDialogueRewards { get; set; } = true;

    /// <summary>When someone picks up a quest item (a key, a letter...), the others get a copy.</summary>
    public bool ShareQuestItems { get; set; } = false;

    // ---- Party ----

    /// <summary>"revive": a dead player is down until someone revives them or the timer runs out. "respawn": back
    /// at once next to the party. "off": the game's own death.</summary>
    public string Death { get; set; } = "revive";

    /// <summary>How long a downed player waits for a revive before respawning next to the party.</summary>
    public int ReviveSeconds { get; set; } = 60;

    /// <summary>How close a player must stand to revive someone, in centimetres (positions are in cm).</summary>
    public int ReviveRange { get; set; } = 400;

    /// <summary>On join, put players back where they left (outdoors), or outside the building they left from.</summary>
    public bool ReturnOnJoin { get; set; } = true;

    /// <summary>The co-op keys: Alt + these. Letters (or key names such as F6) the game doesn't use; K is the chat.</summary>
    public string KeyFollow { get; set; } = "U";

    public string KeyJoin { get; set; } = "Y";

    public string KeyRevive { get; set; } = "H";

    public string KeyRespawn { get; set; } = "G";

    /// <summary>"Alt+U", for messages.</summary>
    public string Key(string action) => "Alt+" + action switch { "follow" => KeyFollow, "join" => KeyJoin, "revive" => KeyRevive, _ => KeyRespawn };

    /// <summary>Tags over players' heads: "Talking", "Down".</summary>
    public bool StatusTags { get; set; } = true;

    /// <summary>Level of spawns and loot: "default" (the server's own, halfway), "lowest", "mid", "highest".</summary>
    public string Scaling { get; set; } = "default";

    /// <summary>Difficulty of damage players deal and take: "Unset" (game default) or a tier name (Novice...Master).</summary>
    public string DifficultyDealt { get; set; } = "Unset";

    public string DifficultyTaken { get; set; } = "Unset";

    // ---------------------------------------------------------------------------------------------------------

    public bool Allowed(string phase, string category)
    {
        var set = phase switch { "join" => Join, "rejoin" => Rejoin, _ => Live };
        return set.TryGetValue(category, out var on) && on;
    }

    /// <summary>Fills in anything an older state file did not have.</summary>
    public void Normalize()
    {
        var fresh = new CoopSettings();
        Live = Merge(Live, fresh.Live);
        Join = Merge(Join, fresh.Join);
        Rejoin = Merge(Rejoin, fresh.Rejoin);
        ProgressFrom = ProgressFrom is "leader" ? "leader" : "anyone";
        ConversationOverlay = ConversationOverlay is "area" or "off" ? ConversationOverlay : "everyone";
        ConversationFreeze = ConversationFreeze is "everyone" or "off" ? ConversationFreeze : "area";
        ConversationFollow = ConversationFollow is "ask" ? "ask" : "auto";
        Death = Death is "respawn" or "off" ? Death : "revive";
        ReviveSeconds = Math.Clamp(ReviveSeconds, 5, 600);
        ReviveRange = Math.Clamp(ReviveRange, 50, 5000);
        Scaling = Scaling is "lowest" or "mid" or "highest" ? Scaling : "default";
        Leader ??= "";
        // L was the first default, but L also hides the game's interface (compass included): move it off L.
        KeyFollow = CleanKey(KeyFollow, "U");
        if (KeyFollow == "L")
            KeyFollow = "U";
        KeyJoin = CleanKey(KeyJoin, "Y");
        KeyRevive = CleanKey(KeyRevive, "H");
        KeyRespawn = CleanKey(KeyRespawn, "G");
        DifficultyDealt ??= "Unset";
        DifficultyTaken ??= "Unset";
    }

    private static string CleanKey(string? key, string fallback)
    {
        var k = (key ?? "").Trim().ToUpperInvariant();
        return k.Length is > 0 and <= 12 && k.All(c => char.IsAsciiLetterOrDigit(c) || c == '_') ? k : fallback;
    }

    private static Dictionary<string, bool> Merge(Dictionary<string, bool>? have, Dictionary<string, bool> defaults)
    {
        var merged = new Dictionary<string, bool>(defaults);
        if (have is not null)
            foreach (var (k, v) in have)
                if (merged.ContainsKey(k))
                    merged[k] = v;
        return merged;
    }

    /// <summary>
    /// Changes one setting from the web page. key is a property name, or "live.items" style for the effect switches.
    /// Returns an error, or null when it worked.
    /// </summary>
    public string? Set(string key, JsonElement value)
    {
        var dot = key.IndexOf('.');
        if (dot > 0)
        {
            var set = key[..dot] switch { "live" => Live, "join" => Join, "rejoin" => Rejoin, _ => null };
            var cat = key[(dot + 1)..];
            if (set is null || !set.ContainsKey(cat) || value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                return "unknown switch";
            set[cat] = value.GetBoolean();
            return null;
        }
        var prop = typeof(CoopSettings).GetProperty(key, System.Reflection.BindingFlags.IgnoreCase | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        if (prop is null || prop.PropertyType == typeof(Dictionary<string, bool>))
            return "unknown setting";
        try
        {
            object? v = prop.PropertyType == typeof(bool) ? value.GetBoolean()
                : prop.PropertyType == typeof(int) ? (value.ValueKind == JsonValueKind.Number ? value.GetInt32() : int.Parse(value.GetString()!))
                : value.GetString() ?? "";
            prop.SetValue(this, v);
        }
        catch
        {
            return "bad value";
        }
        Normalize();
        return null;
    }

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };
}
