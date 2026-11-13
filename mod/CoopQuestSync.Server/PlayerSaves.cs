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
            return true;
        string n = account.ToString("N"), d = account.ToString("D");
        var files = Directory.EnumerateFiles(players)
            .Where(f => Path.GetFileName(f) is var name
                        && (name.Contains(n, StringComparison.OrdinalIgnoreCase) || name.Contains(d, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (files.Count == 0)
            return true;
        return !known && files.All(f => DateTime.UtcNow - File.GetCreationTimeUtc(f) < JustMade);
    }

    /// <summary>The server's saves folder: in the folder it runs in, or above the mod's own folder.</summary>
    private static string? Folder()
    {
        if (_saves is not null)
            return _saves;
        var places = new List<string> { Directory.GetCurrentDirectory() };
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null && places.Count < 6; dir = dir.Parent)
            places.Add(dir.FullName);
        foreach (var place in places)
        {
            var saves = Path.Combine(place, "saves");
            if (File.Exists(Path.Combine(saves, "world.sav")) || Directory.Exists(Path.Combine(saves, "players")))
                return _saves = saves;
        }
        return null;
    }
}
