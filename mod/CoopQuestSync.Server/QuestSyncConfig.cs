using ReadyM.SDK.Attributes;

namespace CoopQuestSync.Server;

/// <summary>Read from config.json in the mod's server folder. Every setting has a default, so the file is optional.</summary>
[ModConfig]
public sealed partial class QuestSyncConfig
{
    /// <summary>
    /// When on, a stage one player finishes is pushed to every other player, and a player who joins gets every stage
    /// the server knows about. When off, the server only logs what it sees; use /qs push to test by hand.
    /// Can be changed in game with /qs auto on|off.
    /// </summary>
    public bool AutoSync { get; set; } = false;

    /// <summary>How often, in seconds, the server reads the players' tokens.</summary>
    public float IntervalSeconds { get; set; } = 1f;

    /// <summary>Where the server keeps the shared quest progress. A relative path is under the server's folder.</summary>
    public string StateFile { get; set; } = "coop-quest-sync-state.json";
}
