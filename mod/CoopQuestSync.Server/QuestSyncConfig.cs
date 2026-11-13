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

    /// <summary>The game's text (key -> text). Empty: coop-quest-sync-strings.json in the server's working folder (next
    /// to the state file), which the server makes itself from the game's files the first time, or copies from a
    /// strings.json next to the DLL (make one with plugin/tools/locres.py).</summary>
    public string StringsFile { get; set; } = "";

    /// <summary>Where Oblivion Remastered is installed, to read its text from. Empty: found through Steam.</summary>
    public string GameFolder { get; set; } = "";

    /// <summary>The language of the game's text: en, de, fr, es, it, pl, ru, ja, zh-Hans... (the folder name under
    /// Localization/Game in the game's files).</summary>
    public string Language { get; set; } = "en";

    /// <summary>
    /// Travel into a building or dungeon straight onto the spot (the console's <c>player.positioncell</c>) instead of
    /// its door first (<c>coc</c>) and then the spot: NPCs by the door no longer greet you on the way in (2026-11-13).
    /// false goes back to coc.
    /// </summary>
    public bool ArriveInsideDirectly { get; set; } = true;

    /// <summary>
    /// The starter kit for players who skip the intro (setting "Starter kit"): form id (hexadecimal, as the game's
    /// console shows it) and count, separated by commas. The default is an iron shortsword, an iron bow, 25 iron arrows,
    /// 5 lockpicks, 3 healing potions, a torch, a leather cuirass and leather boots.
    /// </summary>
    public string StarterKitItems { get; set; } =
        "00000C0D:1,00025231:1,00017829:25,0000000A:5,00098496:3,0002CF9F:1,0002319A:1,0002319B:1";

    /// <summary>
    /// Learn the game's text from the players' own games (their CoopQuestSyncUI add-on looks it up in the running game),
    /// one file per language next to the state file, so the web page and chat lines have words without anyone making a
    /// file by hand, and each player's chat lines come in their own game's language.
    /// </summary>
    public bool LearnTextFromPlayers { get; set; } = true;

    /// <summary>
    /// The ReadyM admin panel's database, where its admin list lives. Its admins may use every co-op command.
    /// A relative path is from the server's working folder. Empty: don't read it (only the admins added on our page).
    /// </summary>
    public string PanelDatabase { get; set; } = "data/web.db";

    /// <summary>
    /// The ReadyM panel permission a panel admin needs for the co-op admin commands (the panel's Admins view, per admin):
    /// players.manage, settings.manage, users.manage, terminal.access or dashboard.access. Superusers always may.
    /// Empty: every active panel admin.
    /// </summary>
    public string PanelPermission { get; set; } = "players.manage";
}
