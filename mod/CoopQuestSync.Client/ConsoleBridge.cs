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
    private static readonly string[] AddonFiles = ["enabled.txt", Path.Combine("Scripts", "main.lua")];

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
            logger.LogInformation("[QuestSync] Installed the CoopQuestSyncUI add-on in {Folder} ({Files}). It starts with the next start of the game.",
                Folder, string.Join(", ", changed));
            _pendingNotice = "Co-op: the CoopQuestSyncUI add-on was installed or updated. Restart the game once to turn it on.";
        }
        catch (Exception e)
        {
            logger.LogWarning("[QuestSync] Could not install the CoopQuestSyncUI add-on in {Folder}: {Error}", Folder, e.Message);
        }
    }

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

    private void Write(string command)
    {
        try
        {
            if (!Directory.Exists(Folder))
            {
                logger.LogWarning("[QuestSync] The CoopQuestSyncUI add-on isn't installed ({Folder}); can't run: {Command}", Folder, command);
                ui.ShowMessage("Co-op: install the CoopQuestSyncUI add-on to be taken back inside.", MessagePosition.Center, 6f);
                return;
            }
            var tmp = Path.Combine(Folder, "command.tmp");
            File.WriteAllText(tmp, command);
            File.Move(tmp, Path.Combine(Folder, "command.txt"), overwrite: true);
            logger.LogInformation("[QuestSync] Asked the add-on to run: {Command}", command);
        }
        catch (Exception e)
        {
            logger.LogWarning("[QuestSync] Could not hand {Command} to the add-on: {Error}", command, e.Message);
        }
    }
}
