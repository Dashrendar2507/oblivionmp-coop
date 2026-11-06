using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using OblivionMp.Sdk;
using OblivionMpCSharpMod;
using ReadyM.SDK.Attributes;

namespace CoopQuestSync.Client;

/// <summary>
/// Asks the CoopQuestSyncUI add-on (UE4SS, in the same game) to run a console command. This mod can't run console
/// commands itself; the add-on can. They share a file in the add-on's folder: this side writes one command, the
/// add-on runs it and deletes the file.
///
/// Only commands built here are written, from checked values, and the add-on only runs commands of the same shapes,
/// so nothing the server sends can become another console command.
///
/// The add-on comes with this mod (the server sends it to every player, in addon\). UE4SS starts add-ons before
/// this mod runs, so a missing or old add-on is copied into place now and works from the next start of the game.
/// </summary>
[Service]
public sealed partial class ConsoleBridge(IUserInterface ui, ILogger logger)
{
    private static readonly Regex CellId = new("^[A-Za-z0-9_]{1,64}$");
    private static readonly Regex VoiceId = new("^[a-z0-9_]{1,40} [a-z0-9_]{1,120}$");
    private static readonly string[] AddonFiles = ["enabled.txt", Path.Combine("Scripts", "main.lua"), Path.Combine("Scripts", "coop.lua")];

    /// <summary>coop.lua is reloaded by the running add-on within seconds; the other files only load when the game starts.</summary>
    private static readonly string LiveFile = Path.Combine("Scripts", "coop.lua");

    private string? _pendingNotice;
    private float _noticeIn = 12f;

    private void Start() => InstallAddon();

    private void Update()
    {
        if (_pendingNotice is null)
            return;
        _noticeIn -= Time.DeltaTime;
        if (_noticeIn > 0)
            return;
        ui.ShowMessage(_pendingNotice, MessagePosition.Center, 10f);
        _pendingNotice = null;
    }

    /// <summary>Copies the add-on into the game's Mods folder when it is missing or older than this mod's copy.</summary>
    private void InstallAddon()
    {
        try
        {
            var source = Path.Combine(Path.GetDirectoryName(typeof(ConsoleBridge).Assembly.Location)!, "addon", "CoopQuestSyncUI");
            if (!Directory.Exists(source))
            {
                logger.LogWarning("[QuestSync] No add-on in {Source} to install.", source);
                return;
            }
            var changed = new List<string>();
            foreach (var file in AddonFiles)
            {
                var from = Path.Combine(source, file);
                var to = Path.Combine(Folder, file);
                if (!File.Exists(from))
                    continue;
                if (File.Exists(to) && File.ReadAllBytes(to).AsSpan().SequenceEqual(File.ReadAllBytes(from)))
                    continue;
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                File.Copy(from, to, overwrite: true);
                changed.Add(file);
            }
            if (changed.Count == 0)
            {
                logger.LogInformation("[QuestSync] The CoopQuestSyncUI add-on is up to date in {Folder}.", Folder);
                return;
            }
            var needsRestart = changed.Any(f => f != LiveFile);
            logger.LogInformation("[QuestSync] Installed the CoopQuestSyncUI add-on in {Folder} ({Files}). {When}",
                Folder, string.Join(", ", changed), needsRestart ? "It starts with the next start of the game." : "The running add-on picks it up in a few seconds.");
            if (needsRestart)
                _pendingNotice = "Co-op: the CoopQuestSyncUI add-on was installed or updated. Restart the game once to turn it on.";
        }
        catch (Exception e)
        {
            logger.LogWarning("[QuestSync] Could not install the CoopQuestSyncUI add-on in {Folder}: {Error}", Folder, e.Message);
        }
    }

    /// <summary>Exists while the add-on sees a menu open (map, inventory...).</summary>
    public static string MenuFlag => Path.Combine(Folder, "menu-open");

    private static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ReadyM.Launcher", "Oblivion", "Mods", "CoopQuestSyncUI");

    /// <summary>Takes the player into an interior: <c>coc &lt;cell&gt;</c>.</summary>
    public void GoToCell(string cell)
    {
        if (!CellId.IsMatch(cell))
        {
            logger.LogWarning("[QuestSync] Ignored a cell name that isn't one: {Cell}", cell);
            return;
        }
        Write("coc " + cell);
    }

    /// <summary>
    /// Plays one spoken line of dialogue, heard from nowhere, such as "imperial_m ms26_ms26filecharges_000363be_1"
    /// (written to voice.txt). The add-on finds the game's own sound for it.
    /// </summary>
    public void Voice(string voice)
    {
        if (!VoiceId.IsMatch(voice))
        {
            logger.LogWarning("[QuestSync] Ignored a voice line that isn't one: {Voice}", voice);
            return;
        }
        Write(voice, "voice", quiet: true);
    }

    /// <summary>Stops the voice line playing, if any.</summary>
    public void StopVoice() => Write("stop", "voice", quiet: true);

    private void Write(string command, string file = "command", bool quiet = false)
    {
        try
        {
            if (!Directory.Exists(Folder))
            {
                logger.LogWarning("[QuestSync] The CoopQuestSyncUI add-on isn't installed ({Folder}); can't run: {Command}", Folder, command);
                if (!quiet)
                    ui.ShowMessage("Co-op: install the CoopQuestSyncUI add-on to be taken back inside.", MessagePosition.Center, 6f);
                return;
            }
            var tmp = Path.Combine(Folder, file + ".tmp");
            File.WriteAllText(tmp, command);
            File.Move(tmp, Path.Combine(Folder, file + ".txt"), overwrite: true);
            logger.LogInformation("[QuestSync] Asked the add-on ({File}): {Command}", file, command);
        }
        catch (Exception e)
        {
            logger.LogWarning("[QuestSync] Could not hand {Command} to the add-on: {Error}", command, e.Message);
        }
    }
}
