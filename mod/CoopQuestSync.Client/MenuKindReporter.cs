using Microsoft.Extensions.Logging;
using OblivionMpCSharpMod;
using ReadyM.Api.DI;
using ReadyM.SDK.Attributes;

namespace CoopQuestSync.Client;

/// <summary>
/// Passes on the menu the add-on recognised in the game's own screens (its file menu-kind: "lockpick", or gone when
/// the menu closed), for menus the dialogue plugin's MenuMode can't see: the remaster's lockpicking never reached it
/// (2026-11-13). The server then tells whoever watches "Alice is picking a lock".
/// </summary>
[Service]
public sealed partial class MenuKindReporter(IDependencyContainer services, ILogger logger)
{
    private static string KindFile => Path.Combine(Path.GetDirectoryName(ConsoleBridge.MenuFlag)!, "menu-kind");
    private const float CheckEverySeconds = 0.5f;

    private float _checkIn;
    private string _sent = "";

    private void Update()
    {
        _checkIn -= Time.DeltaTime;
        if (_checkIn > 0)
            return;
        _checkIn = CheckEverySeconds;
        var kind = "";
        try
        {
            if (File.Exists(KindFile))
                kind = File.ReadAllText(KindFile).Trim();
        }
        catch
        {
            return; // the add-on is writing it: next time
        }
        if (kind.Length > 32 || !kind.All(char.IsAsciiLetterLower))
            kind = "";
        if (kind == _sent)
            return;
        try
        {
            services.Resolve<ClientRpc>().SendMenuKind(kind);
            _sent = kind;
        }
        catch (Exception e)
        {
            logger.LogDebug("[QuestSync] Could not tell the server about the menu: {Error}", e.Message);
        }
    }
}
