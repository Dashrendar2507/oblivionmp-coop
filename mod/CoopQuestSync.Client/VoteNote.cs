using Microsoft.Extensions.Logging;
using OblivionMP.SDK;
using OblivionMpCSharpMod;
using ReadyM.Cef;
using ReadyM.SDK.Attributes;

namespace CoopQuestSync.Client;

/// <summary>
/// "Alice wants to sleep 8 hours. Alt+Y or /coop yes to agree..." while a teammate's wait or sleep vote runs, with the
/// seconds left (the server's RestVote). Same page as <see cref="DownNote"/>; it goes when the player answers, the vote
/// ends, or a load starts (and comes back after it while the vote still runs).
/// </summary>
[Service]
public sealed partial class VoteNote(CefHost cef, IPlayers players, ILogger logger)
{
    private const string Host = "coopwitness";

    private string? _text;          // the note's text, {s} counting down
    private float _left;            // seconds left in the vote
    private bool _fresh;
    private ViewportHandle? _viewport;
    private BrowserHandle? _browser;

    /// <summary>"&lt;seconds&gt;|&lt;text&gt;" to show it, "" to take it away.</summary>
    public void Set(string text)
    {
        Destroy();
        var bar = text.IndexOf('|');
        if (string.IsNullOrEmpty(text) || bar <= 0 || !int.TryParse(text[..bar], out var seconds))
        {
            _text = null;
            _fresh = false;
            return;
        }
        _text = text[(bar + 1)..];
        _left = seconds;
        _fresh = true;
    }

    private void Update()
    {
        if (_text is null)
            return;
        _left -= Time.DeltaTime;
        if (_left <= 0)
        {
            _text = null;
            Destroy();
            return;
        }
        // Never in the middle of a load: shown again once the character is back in the world.
        if (players.LocalMainCharacter is null)
        {
            if (_viewport is not null)
            {
                Destroy();
                _fresh = true;
            }
            return;
        }
        if (!_fresh)
            return;
        _fresh = false;
        try
        {
            var viewport = cef.CreateViewport(zOrder: 2);
            cef.SetPlacement(viewport, ViewportPlacement.FullScreen);
            cef.SetInputMode(viewport, CefInputMode.DisplayOnly);
            cef.SetFocusable(viewport, false);
            var browser = cef.CreateBrowser();
            browser.Navigate($"readym://{Host}/down.html#{(int)Math.Ceiling(_left)}|{Uri.EscapeDataString(_text)}");
            cef.Assign(viewport, browser.Handle);
            cef.SetVisible(viewport, true);
            _viewport = viewport;
            _browser = browser.Handle;
            logger.LogInformation("[QuestSync] Wait vote note shown.");
        }
        catch (Exception e)
        {
            logger.LogWarning("[QuestSync] Could not show the wait vote note: {Error}", e.Message);
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
            logger.LogDebug("[QuestSync] Wait vote note cleanup failed: {Error}", e.Message);
        }
        _viewport = null;
        _browser = null;
    }
}
