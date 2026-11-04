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
/// </summary>
[Service]
public sealed partial class ConsoleBridge(IUserInterface ui, ILogger logger)
{
    private static readonly Regex CellId = new("^[A-Za-z0-9_]{1,64}$");

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
