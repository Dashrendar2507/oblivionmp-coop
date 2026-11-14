using Microsoft.Extensions.Logging;

namespace CoopQuestSync.Server;

/// <summary>
/// Tells a brand-new character from a returning one. OblivionMP 1.1 keeps each character on the server, in
/// <c>saves/players/player_&lt;ReadyM id&gt;.sav</c> under the server folder. A player with no file there has never played
/// on this server (or an admin moved their file aside to start them over). The SDK has no call for this yet.
/// </summary>
internal static class PlayerSaves
{
    /// <summary>A save made this recently, for an account this server has never seen, was made by the join itself.</summary>
    private static readonly TimeSpan JustMade = TimeSpan.FromMinutes(2);

    private static string? _saves;

    /// <summary>
    /// Whether the account's character is brand new. <paramref name="known"/>: the account joined this server before
    /// (since 0.7.0). False whenever in doubt: the setup empties the bag, so it must never hit a played character.
    /// </summary>
    public static bool IsBrandNew(Guid account, bool known, ILogger logger)
    {
        var saves = Folder();
        if (saves is null)
        {
            logger.LogWarning("[QuestSync] Can't find the server's saves folder (looked in {Where}): new characters are not set up.", Directory.GetCurrentDirectory());
            return false;
        }
        var players = Path.Combine(saves, "players");
        if (!Directory.Exists(players))
        {
            logger.LogInformation("[QuestSync] Player {Account}: no {Folder} yet, so a new character.", account.ToString("N"), players);
            return true;
        }
        string n = account.ToString("N"), d = account.ToString("D");
        var files = Directory.EnumerateFiles(players)
            .Where(f => Path.GetFileName(f) is var name
                        && (name.Contains(n, StringComparison.OrdinalIgnoreCase) || name.Contains(d, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        var fresh = files.Count == 0 || !known && files.All(f => DateTime.UtcNow - File.GetCreationTimeUtc(f) < JustMade);
        logger.LogInformation("[QuestSync] Player {Account}: {Save} in {Folder}, so {Verdict}.", n,
            files.Count == 0 ? "no save" : files.Count == 1 ? Path.GetFileName(files[0]) : $"{files.Count} saves",
            players, fresh ? "a new character" : "a returning character");
        return fresh;
    }

    /// <summary>The server's saves folder: in the folder it runs in, or above the mod's own folder.</summary>
    private static string? Folder()
    {
        if (_saves is not null)
            return _saves;
        // The server runs in its own folder. AppContext.BaseDirectory is empty on the Windows server (2026-11-14: an empty
        // path threw here, on every join), so the mod's own folder comes from its assembly.
        var places = new List<string> { Directory.GetCurrentDirectory() };
        foreach (var start in new[] { AppContext.BaseDirectory, Path.GetDirectoryName(typeof(PlayerSaves).Assembly.Location) })
        {
            if (string.IsNullOrWhiteSpace(start))
                continue;
            var count = 0;
            for (var dir = new DirectoryInfo(start); dir is not null && count < 5; dir = dir.Parent, count++)
                places.Add(dir.FullName);
        }
        foreach (var place in places)
        {
            var saves = Path.Combine(place, "saves");
            if (File.Exists(Path.Combine(saves, "world.sav")) || Directory.Exists(Path.Combine(saves, "players")))
                return _saves = saves;
        }
        return null;
    }
}
