using Microsoft.Extensions.Logging;

namespace CoopQuestSync.Server;

/// <summary>
/// The save every new character starts from. OblivionMP 1.1 hands each joining player <c>world.sav</c> from the server's
/// <c>modloader</c> folder, and starts a new character from it: by default a ready-made level 1 Dunmer near Aleswell.
/// This mod ships a save made in the prison cell at the start of a new game, and puts it there when the server starts
/// (setting PrisonStartSave), before the server reads the folder. Players' games pick up the new file on their next
/// join, as with any change to the modloader. OblivionMP's own file is kept next to the state file, and put back when
/// the setting is off. Characters already saved on the server don't change: only new ones start from it.
/// </summary>
internal static class StartSave
{
    private const string Resource = "CoopQuestSync.prison-start.sav";
    private const string Original = "coop-quest-sync-original-world.sav";

    public static void Apply(bool prison, ILogger logger)
    {
        try
        {
            Run(prison, logger);
        }
        catch (Exception e)
        {
            logger.LogWarning("[QuestSync] Couldn't change the start save: {Error}", e.Message);
        }
    }

    private static void Run(bool prison, ILogger logger)
    {
        if (Folder() is not { } modloader)
        {
            if (prison)
                logger.LogWarning("[QuestSync] No modloader folder found (looked in {Where}): new characters start from OblivionMP's own save.", Directory.GetCurrentDirectory());
            return;
        }
        var targets = Directory.EnumerateFiles(modloader, "world.sav", SearchOption.AllDirectories).ToList();
        if (targets.Count == 0)
        {
            logger.LogWarning("[QuestSync] No world.sav in {Folder}: the start save is left as it is.", modloader);
            return;
        }
        var ours = Ours();
        var backup = Path.Combine(Path.GetDirectoryName(modloader)!, Original);
        foreach (var target in targets)
        {
            var now = File.ReadAllBytes(target);
            var isOurs = now.AsSpan().SequenceEqual(ours);
            if (prison)
            {
                if (isOurs)
                {
                    logger.LogInformation("[QuestSync] Start save: the prison cell ({File}).", target);
                    continue;
                }
                // OblivionMP's own, or a newer one from an update: keep it, to put back if the setting goes off.
                File.WriteAllBytes(backup, now);
                Write(target, ours);
                logger.LogInformation("[QuestSync] Start save: now the prison cell ({File}). OblivionMP's own is kept in {Backup}.", target, backup);
            }
            else if (isOurs)
            {
                if (!File.Exists(backup))
                {
                    logger.LogWarning("[QuestSync] Start save: still the prison cell, and OblivionMP's own isn't in {Backup} to put back.", backup);
                    continue;
                }
                Write(target, File.ReadAllBytes(backup));
                logger.LogInformation("[QuestSync] Start save: OblivionMP's own again ({File}).", target);
            }
        }
    }

    /// <summary>Through a temporary file, so a crash halfway never leaves the server with half a save.</summary>
    private static void Write(string path, byte[] data)
    {
        var temp = path + ".coop-new";
        File.WriteAllBytes(temp, data);
        File.Move(temp, path, overwrite: true);
    }

    private static byte[] Ours()
    {
        using var stream = typeof(StartSave).Assembly.GetManifestResourceStream(Resource)
                           ?? throw new InvalidOperationException("the prison save is missing from the mod");
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    /// <summary>The server's modloader folder: in the folder it runs in, or above the mod's own folder.</summary>
    private static string? Folder()
    {
        var places = new List<string> { Directory.GetCurrentDirectory() };
        if (Path.GetDirectoryName(typeof(StartSave).Assembly.Location) is { Length: > 0 } start)
            for (DirectoryInfo? dir = new(start); dir is not null && places.Count < 7; dir = dir.Parent)
                places.Add(dir.FullName);
        return places.Select(p => Path.Combine(p, "modloader")).FirstOrDefault(Directory.Exists);
    }
}
