using Microsoft.Extensions.Logging;
using OblivionMp.Sdk;
using OblivionMpCSharpMod;
using ReadyM.Cef;
using ReadyM.SDK.Attributes;

namespace CoopQuestSync.Client;

/// <summary>
/// While the player is down, a note under OblivionMP's own "You died / Press R to rebirth" message saying what the
/// co-op keys do, with the seconds left. The server's 5 s notice for this was lost under that message (2026-11-13).
/// The server raises it (<see cref="ClientRpc"/>, Down) and takes it away; it also goes before the rebirth's reload.
/// </summary>
[Service]
public sealed partial class DownNote(CefHost cef, IPlayers players, ILogger logger)
{
    private const string Host = "coopwitness";

    private string? _wanted;       // "<seconds>|<text>", or null
    private bool _fresh;
    private ViewportHandle? _viewport;
    private BrowserHandle? _browser;

    /// <summary>"&lt;seconds&gt;|&lt;text&gt;" to show it, "" to take it away.</summary>
    public void Set(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            _wanted = null;
            _fresh = false;
            Destroy();
            return;
        }
        _wanted = text;
        _fresh = true;
    }

    private void Update()
    {
        if (!_fresh || _wanted is null)
            return;
        // Only on a character that is in the world (and down): never in the middle of a load.
        if (players.LocalMainCharacter is not { IsDead: true })
            return;
        _fresh = false;
        Destroy();
        try
        {
            var bar = _wanted.IndexOf('|');
            var seconds = bar > 0 && int.TryParse(_wanted[..bar], out var n) ? n : 0;
            var line = bar >= 0 ? _wanted[(bar + 1)..] : _wanted;
            var viewport = cef.CreateViewport(zOrder: 2);
            cef.SetPlacement(viewport, ViewportPlacement.FullScreen);
            cef.SetInputMode(viewport, CefInputMode.DisplayOnly);
            cef.SetFocusable(viewport, false);
            var browser = cef.CreateBrowser();
            browser.Navigate($"readym://{Host}/down.html#{seconds}|{Uri.EscapeDataString(line)}");
            cef.Assign(viewport, browser.Handle);
            cef.SetVisible(viewport, true);
            _viewport = viewport;
            _browser = browser.Handle;
            logger.LogInformation("[QuestSync] Down note shown.");
        }
        catch (Exception e)
        {
            logger.LogWarning("[QuestSync] Could not show the down note: {Error}", e.Message);
        }
    }

    private void Destroy()
    {
        try
        {
            if (_viewport is { } v)
                cef.DestroyViewport(v);
            if (_browser is { } b)
                cef.DestroyBrowser(b);
        }
        catch (Exception e)
        {
            logger.LogDebug("[QuestSync] Down note cleanup failed: {Error}", e.Message);
        }
        _viewport = null;
        _browser = null;
    }
}
