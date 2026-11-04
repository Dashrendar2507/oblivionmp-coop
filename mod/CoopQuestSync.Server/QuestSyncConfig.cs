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

    /// <summary>Port of the quest admin web page. 0 turns it off.</summary>
    public int WebPort { get; set; } = 9060;

    /// <summary>
    /// Which address the web page listens on. 127.0.0.1 means only this computer can open it. Use 0.0.0.0 to open it
    /// to your network, which also needs a <see cref="WebPassword"/>.
    /// </summary>
    public string WebBind { get; set; } = "127.0.0.1";

    /// <summary>Password the web page asks for. Required when <see cref="WebBind"/> is not 127.0.0.1.</summary>
    public string WebPassword { get; set; } = "";

    /// <summary>Where the server keeps the shared quest progress. A relative path is from the server's working folder.</summary>
    public string StateFile { get; set; } = "coop-quest-sync-state.json";

    /// <summary>
    /// The players' load order, as in the game's Plugins.txt (the plugins before ours). Only used to find items and
    /// NPCs of the DLC and the remaster's own plugins; vanilla ones work whatever this says.
    /// </summary>
    public string GamePlugins { get; set; } =
        "Oblivion.esm,DLCBattlehornCastle.esp,DLCFrostcrag.esp,DLCHorseArmor.esp,DLCMehrunesRazor.esp,DLCOrrery.esp," +
        "DLCShiveringIsles.esp,DLCSpellTomes.esp,DLCThievesDen.esp,DLCVileLair.esp,Knights.esp,AltarESPMain.esp,AltarDeluxe.esp,AltarESPLocal.esp";

    /// <summary>The game's text (key -> text), made with plugin/tools/locres.py. Empty: strings.json next to the DLL.</summary>
    public string StringsFile { get; set; } = "";
}
