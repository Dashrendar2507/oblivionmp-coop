using ReadyM.SDK.Attributes;

namespace CoopQuestSync.Server;

/// <summary>Read from config.json in the mod's server folder. Every setting has a default, so the file is optional.</summary>
[ModConfig]
public sealed partial class QuestSyncConfig
{
    /// <summary>
    /// When on, a stage one player finishes is pushed to every other player, and a player who joins gets every stage
    /// the server knows about. When off, the server only records what it sees; /qs push still works.
    /// Can be changed in game with /qs auto on|off.
    /// </summary>
    public bool AutoSync { get; set; } = true;

    /// <summary>
    /// When a player rejoins, the plugin replays their own stages, and each stage's script runs again: rewards are
    /// handed out again and quest items are taken again. When on, the server puts the inventory back the way it was
    /// before the replay.
    /// </summary>
    public bool UndoReplayRewards { get; set; } = true;

    /// <summary>How often, in seconds, the server reads the players' tokens.</summary>
    public float IntervalSeconds { get; set; } = 1f;

    /// <summary>Where the server keeps the shared quest progress. A relative path is from the server's working folder.</summary>
    public string StateFile { get; set; } = "coop-quest-sync-state.json";
}
