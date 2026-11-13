using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using OblivionMP.SDK;
using OblivionMpCSharpMod;
using ReadyM.SDK.Attributes;

namespace CoopQuestSync.Client;

/// <summary>
/// Asks the CoopQuestSyncUI add-on (UE4SS, in the same game) to run console commands. This mod can't run console
/// commands itself; the add-on can. They share a file in the add-on's folder: this side adds commands to it, one a
/// line, and the add-on takes the file, runs them in order and deletes it.
///
/// Only commands built here are written, from checked values, and the add-on only runs commands of the same shapes,
/// so nothing the server sends can become another console command.
///
/// The add-on comes with this mod (the server sends it to every player, in addon\). UE4SS starts add-ons before
/// this mod runs, so a missing or old add-on is copied into place now and works from the next start of the game.
/// </summary>
[Service]
public sealed partial class ConsoleBridge(IUserInterface ui, IInput input, RestartNotice restart, ILogger logger)
{
    private static readonly Regex CellId = new("^[A-Za-z0-9_]{1,64}$");
    private static readonly Regex CellSpot = new("^([A-Za-z0-9_]{1,64}) (-?[0-9]{1,6}) (-?[0-9]{1,6}) (-?[0-9]{1,6})$");
    private static readonly Regex WorldPlace = new("^[A-Za-z0-9_]{1,64} -?[0-9]{1,4} -?[0-9]{1,4}$");
    private static readonly Regex RefId = new("^[0-9A-Fa-f]{1,8}$");
    /// <summary>Commands the server may ask for by name, with nothing of its own in them.</summary>
    private static readonly HashSet<string> Fixed = ["player.scaonactor", "player.SetCrimeGold 0"];
    private static readonly Regex VoiceId = new("^[a-z0-9_]{1,40} [a-z0-9_]{1,120}$");
    private static readonly string[] AddonFiles = ["enabled.txt", Path.Combine("Scripts", "main.lua"), Path.Combine("Scripts", "coop.lua")];

    private void Start() => InstallAddon();

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
                logger.LogInformation("[QuestSync] The CoopQuestSyncUI add-on is up to date in {Folder}.", Folder);
            else
                logger.LogInformation("[QuestSync] Installed the CoopQuestSyncUI add-on in {Folder} ({Files}). It starts with the next start of the game.",
                    Folder, string.Join(", ", changed));
            CheckRunning();
        }
        catch (Exception e)
        {
            logger.LogWarning("[QuestSync] Could not install the CoopQuestSyncUI add-on in {Folder}: {Error}", Folder, e.Message);
        }
    }

    /// <summary>
    /// The add-on writes running.txt when the game starts: a fingerprint of the main.lua and coop.lua it started with,
    /// or "off". When that isn't the add-on in the folder now, the game runs an older one until it restarts.
    /// </summary>
    private void CheckRunning()
    {
        try
        {
            var running = Path.Combine(Folder, "running.txt");
            var started = File.Exists(running) ? File.ReadAllText(running).Trim() : "";
            if (started == "off")
            {
                logger.LogInformation("[QuestSync] The CoopQuestSyncUI add-on is switched off (the file off in {Folder}).", Folder);
                return;
            }
            var expected = Fingerprint(File.ReadAllBytes(Path.Combine(Folder, "Scripts", "main.lua")),
                File.ReadAllBytes(Path.Combine(Folder, "Scripts", "coop.lua")));
            if (started.Equals(expected, StringComparison.OrdinalIgnoreCase))
                return;
            logger.LogInformation("[QuestSync] The game started with add-on {Started}, the folder has {Expected}: a restart turns the new one on.",
                started == "" ? "none" : started, expected);
            restart.Require();
        }
        catch (Exception e)
        {
            logger.LogWarning("[QuestSync] Could not tell which add-on the game runs: {Error}", e.Message);
        }
    }

    /// <summary>FNV-1a, 32 bits, as hex: the same as main.lua computes.</summary>
    internal static string Fingerprint(params byte[][] files)
    {
        uint h = 2166136261;
        foreach (var data in files)
            foreach (var b in data)
                h = unchecked((h ^ b) * 16777619);
        return h.ToString("x8");
    }

    /// <summary>Exists while the add-on sees a menu open (map, inventory...).</summary>
    public static string MenuFlag => Path.Combine(Folder, "menu-open");

    private static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ReadyM.Launcher", "Oblivion", "Mods", "CoopQuestSyncUI");

    /// <summary>Takes the player into an interior: <c>coc &lt;cell&gt;</c>.</summary>
    public void GoToCell(string cell)
    {
        // "<cell> <x> <y> <z>": straight onto the spot inside, so the NPCs by the door don't greet the player first.
        var m = CellSpot.Match(cell);
        if (m.Success)
        {
            Move($"player.positioncell {m.Groups[2].Value} {m.Groups[3].Value} {m.Groups[4].Value} 0 {m.Groups[1].Value}");
            return;
        }
        if (!CellId.IsMatch(cell))
        {
            logger.LogWarning("[QuestSync] Ignored a cell name that isn't one: {Cell}", cell);
            return;
        }
        Move("coc " + cell);
    }

    /// <summary>Takes the player to a cell of an exterior world space: <c>cow &lt;world&gt; &lt;x&gt; &lt;y&gt;</c>.</summary>
    public void GoToWorld(string place)
    {
        if (!WorldPlace.IsMatch(place))
        {
            logger.LogWarning("[QuestSync] Ignored a place that isn't one: {Place}", place);
            return;
        }
        Move("cow " + place);
    }

    /// <summary>A move waits until nothing has the keyboard: a load with the chat open leaves the game broken
    /// (2026-11-13, <c>/coop goto</c> typed in the chat). Only the newest move waits.</summary>
    private string? _heldMove;
    private float _heldFor;
    private float _freeFor;
    private const float HoldMaxSeconds = 40f;

    private void Move(string command)
    {
        if (_heldMove is null && input.CanApplyInput())
        {
            Write(command);
            return;
        }
        if (_heldMove is null)
        {
            ui.ShowMessage("Co-op: close the chat to travel.", MessagePosition.Center, 4f);
            logger.LogInformation("[QuestSync] Waiting for the chat or menu to close before: {Command}", command);
        }
        _heldMove = command;
        _heldFor = 0;
        _freeFor = 0;
    }

    private void Update()
    {
        if (_heldMove is not { } move)
            return;
        var dt = Time.DeltaTime;
        _heldFor += dt;
        _freeFor = input.CanApplyInput() ? _freeFor + dt : 0;
        if (_freeFor >= 0.3f)
        {
            _heldMove = null;
            Write(move);
        }
        else if (_heldFor > HoldMaxSeconds)
        {
            _heldMove = null;
            logger.LogInformation("[QuestSync] Dropped {Command}: the chat or a menu stayed open {Seconds} s.", move, (int)HoldMaxSeconds);
            ui.ShowMessage("Co-op: travel cancelled, the chat stayed open.", MessagePosition.Center, 5f);
        }
    }

    /// <summary>Stops these NPCs' fights ("0001A2B3 FF000C41"): <c>&lt;ref&gt;.StopCombat</c> for each.</summary>
    public void StopCombat(string refs)
    {
        foreach (var id in refs.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(64))
            if (RefId.IsMatch(id))
                Write(id.ToUpperInvariant() + ".StopCombat");
            else
                logger.LogWarning("[QuestSync] Ignored a ref id that isn't one: {Ref}", id);
    }

    /// <summary>Runs one of the <see cref="Fixed"/> commands.</summary>
    public void Run(string command)
    {
        if (Fixed.Contains(command))
            Write(command);
        else
            logger.LogWarning("[QuestSync] Ignored a command that isn't allowed: {Command}", command);
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
                    ui.ShowMessage("Co-op: install the CoopQuestSyncUI add-on to travel there.", MessagePosition.Center, 6f);
                return;
            }
            if (file == "command")
            {
                // Console commands queue up, one a line: the add-on takes the whole file and runs them in order.
                File.AppendAllText(Path.Combine(Folder, "command.txt"), command + "\n");
            }
            else
            {
                var tmp = Path.Combine(Folder, file + ".tmp");
                File.WriteAllText(tmp, command);
                File.Move(tmp, Path.Combine(Folder, file + ".txt"), overwrite: true);
            }
            logger.LogInformation("[QuestSync] Asked the add-on ({File}): {Command}", file, command);
        }
        catch (Exception e)
        {
            logger.LogWarning("[QuestSync] Could not hand {Command} to the add-on: {Error}", command, e.Message);
        }
    }
}
