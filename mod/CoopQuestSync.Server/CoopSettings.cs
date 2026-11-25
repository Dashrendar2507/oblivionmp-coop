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

    /// <summary>
    /// The NPCs and creatures that wait hidden until a script brings them in (quest ambushes, the prison rats) are made
    /// on the server, still hidden, so the script's enable reaches everyone (<see cref="HiddenActors"/>).
    /// </summary>
    public bool HiddenNpcs { get; set; } = true;

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

    /// <summary>A black screen with one line while the game moves a player (back on join, or to a teammate).</summary>
    public bool TravelScreen { get; set; } = true;

    /// <summary>Players can travel to a teammate whenever they like: /coop goto &lt;name&gt; or the goto key.</summary>
    public bool TeleportAnytime { get; set; } = true;

    /// <summary>Seconds a player waits between two trips with /coop goto (0: no wait).</summary>
    public int TeleportCooldown { get; set; } = 60;

    /// <summary>No /coop goto while the traveller is fighting.</summary>
    public bool TeleportNotInCombat { get; set; } = true;

    /// <summary>The co-op keys: Alt + these. Letters (or key names such as F6) the game doesn't use; K is the chat.</summary>
    public string KeyFollow { get; set; } = "U";

    public string KeyJoin { get; set; } = "Y";

    public string KeyRevive { get; set; } = "H";

    public string KeyRespawn { get; set; } = "G";

    public string KeyGoto { get; set; } = "O";

    /// <summary>"Alt+U", for messages.</summary>
    public string Key(string action) => "Alt+" + action switch { "follow" => KeyFollow, "join" => KeyJoin, "revive" => KeyRevive, "goto" => KeyGoto, _ => KeyRespawn };

    /// <summary>Tags over players' heads: "Talking", "Down".</summary>
    public bool StatusTags { get; set; } = true;

    /// <summary>Level of spawns and loot: "default" (the server's own, halfway), "lowest", "mid", "highest".</summary>
    public string Scaling { get; set; } = "default";

    /// <summary>Difficulty of damage players deal and take: "Unset" (game default) or a tier name (Novice...Master).</summary>
    public string DifficultyDealt { get; set; } = "Unset";

    public string DifficultyTaken { get; set; } = "Unset";

    /// <summary>
    /// Waiting and sleeping under OblivionMP 1.2's one clock (see <see cref="RestVote"/>): "vote" (the others agree
    /// first), "anyone" (anyone's wait moves everyone's clock) or "off" (OblivionMP's own settings decide).
    /// </summary>
    public string Rest { get; set; } = "vote";

    /// <summary>How long the others have to agree to a wait or sleep.</summary>
    public int RestVoteSeconds { get; set; } = 30;

    /// <summary>Players can hurt each other. Off: a player's hit on another player is refused (see <see cref="FriendlyFire"/>).</summary>
    public bool FriendlyFire { get; set; } = false;

    /// <summary>
    /// On start, the server puts a save made in the prison cell into its modloader, so a new character starts the
    /// game's own way (see <see cref="StartSave"/>). Off puts OblivionMP's own start save back. Next start.
    /// </summary>
    public bool PrisonStartSave { get; set; } = true;

    // ---- New players ----

    /// <summary>
    /// What a player who has never left the sewers gets on joining: "ask" (a window: the prison intro or the sewer exit),
    /// "sewers" (always the sewer exit, with a short note), or "off" (the game's own start, nothing from the mod).
    /// Either way, the party's quest progress waits until they are out of the sewers. OblivionMP 1.1 starts every new
    /// character from the server's start save (world.sav in its modloader.7z): this only does something when that save
    /// is in the prison (see docs/INSTALL.md). With OblivionMP's own save (out of the sewers already) nothing is asked.
    /// </summary>
    public string NewStart { get; set; } = "ask";

    /// <summary>
    /// When the prison intro is offered: "fresh" (only until someone has left the sewers on this server: the Emperor and
    /// the Blades are shared, so the intro can only be played once), "always" (for testing), or "never".
    /// </summary>
    public string PrisonStart { get; set; } = "fresh";

    /// <summary>
    /// A brand-new character (no save on this server yet) is set up on their first join: their bag is emptied, they get
    /// the starter kit (if <see cref="StarterKit"/>), and the race, class and birthsign menus open. OblivionMP 1.1 starts
    /// everyone as the same preset character; this lets them make their own. The quest stages that preset already has
    /// stay out of the party's shared progress.
    /// </summary>
    public bool NewCharacterSetup { get; set; } = true;

    /// <summary>
    /// The new-character setup first takes the player to the sewer exit (just inside the gate out of the Imperial
    /// Sewers, where the vanilla game starts you off), then opens the menus there.
    /// </summary>
    public bool NewCharacterAtSewers { get; set; } = true;

    /// <summary>New characters (and, with the custom start, players who skip the intro) get some gear (config.json StarterKitItems), like what the prison gives.</summary>
    public bool StarterKit { get; set; } = true;

    /// <summary>
    /// Once someone has given the Amulet of Kings to Jauffre (Deliver the Amulet, stage 50), the copies other players
    /// still carry go away. Every player who starts gets one, and it's a quest item, so it can't be dropped.
    /// </summary>
    public bool TakeSpareAmulets { get; set; } = true;

    // ---- Command access ----

    /// <summary>
    /// Who may use the commands that change things for everybody (/qs push, auto, source, reset, /qsprobe, /coop test):
    /// "admins" (the admins of the ReadyM admin panel, plus <see cref="Admins"/>) or "everyone".
    /// </summary>
    public string CommandAccess { get; set; } = "admins";

    /// <summary>Extra admins added on this page, by ReadyM account id. Admins of the ReadyM panel need no entry here.</summary>
    public List<AdminEntry> Admins { get; set; } = [];

    public sealed class AdminEntry
    {
        public string Id { get; set; } = "";

        /// <summary>Their nickname when they were added, only to show on the page.</summary>
        public string Name { get; set; } = "";
    }

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
        KeyGoto = CleanKey(KeyGoto, "O");
        TeleportCooldown = Math.Clamp(TeleportCooldown, 0, 3600);
        Rest = Rest is "anyone" or "off" ? Rest : "vote";
        RestVoteSeconds = Math.Clamp(RestVoteSeconds, 10, 300);
        DifficultyDealt ??= "Unset";
        DifficultyTaken ??= "Unset";
        CommandAccess = CommandAccess is "everyone" ? "everyone" : "admins";
        NewStart = NewStart is "sewers" or "ask" ? NewStart : "off";
        PrisonStart = PrisonStart is "always" or "never" ? PrisonStart : "fresh";
        Admins = (Admins ?? []).Where(a => a is not null && Guid.TryParse(a.Id, out _)).DistinctBy(a => Guid.Parse(a.Id)).ToList();
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
        if (prop is null || prop.PropertyType == typeof(Dictionary<string, bool>) || prop.PropertyType == typeof(List<AdminEntry>))
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
