using Microsoft.Extensions.Logging;
using OblivionMp.Sdk;
using OblivionMpCSharpMod;
using ReadyM.Cef;
using ReadyM.SDK.Attributes;

namespace CoopQuestSync.Client;

/// <summary>
/// A black screen with one line ("Taking you back to where you left...") while the game moves this player: after the
/// join's load, the console's coc (its own load), then the server's position write. Without it the player sees the
/// save's spot, the door, then a jump. The server raises it and lifts it (setting "Black screen while travelling").
///
/// A load can drop a page made before it (2026-11-07, the restart note), so each raise makes a new page, only once the
/// player is in the world, and the server raises it again on arrival. It steps aside while a menu is open (the quest
/// pop-up during the join's catch-up must stay visible to be closed), and lifts by itself after a while.
/// </summary>
[Service]
public sealed partial class TravelScreen(CefHost cef, IPlayers players, IInput input, ILogger logger)
{
    private const string Host = "coopwitness";
    private const float MaxSeconds = 50f;
    private const float FadeSeconds = 0.7f;
    private const float InWorldSeconds = 1f;

    private string? _text;          // wanted, with this line
    private bool _fresh;            // a new page is due
    private float _up;
    private float _inWorld;
    private float _checkIn;
    private bool _menu;
    private bool _moving;           // the add-on ran the coc/cow: stay up through the load
    private static string TravelRan => Path.Combine(Path.GetDirectoryName(ConsoleBridge.MenuFlag)!, "travel-ran");
    private ViewportHandle? _viewport;
    private BrowserHandle? _browser;
    private bool _visible;
    private float _destroyIn = -1;
    private int _shown;

    public void Set(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            if (_text is null)
                return;
            _text = null;
            Lift();
            return;
        }
        _fresh = true;
        if (_text is null)
        {
            _moving = false;
            try { File.Delete(TravelRan); } catch { /* not there, or the add-on is writing it */ }
        }
        _text = text;
        _up = 0;
    }

    private void Update()
    {
        var dt = Time.DeltaTime;
        if (_destroyIn >= 0)
        {
            _destroyIn -= dt;
            if (_destroyIn < 0)
                Destroy();
        }
        for (var i = _retired.Count - 1; i >= 0; i--)
        {
            var (oldViewport, oldBrowser, left) = _retired[i];
            if (left - dt > 0)
            {
                _retired[i] = (oldViewport, oldBrowser, left - dt);
                continue;
            }
            _retired.RemoveAt(i);
            try
            {
                cef.DestroyViewport(oldViewport);
                cef.DestroyBrowser(oldBrowser);
            }
            catch (Exception e)
            {
                logger.LogDebug("[QuestSync] Travel screen cleanup failed: {Error}", e.Message);
            }
        }
        // In the world: a character that can move (not the join's loading screen), for a moment. Once seen, a menu
        // or a load no longer resets it; a page made during the join's load was lost with it (2026-11-07).
        if (players.LocalMainCharacter is null)
            _inWorld = 0;
        else if (_inWorld >= InWorldSeconds || input.CanApplyInput())
            _inWorld += dt;
        else
            _inWorld = 0;
        if (_text is null)
            return;
        _up += dt;
        if (_up > MaxSeconds)
        {
            logger.LogInformation("[QuestSync] Travel screen lifted after {Seconds} s.", (int)MaxSeconds);
            _text = null;
            Lift();
            return;
        }
        if (_inWorld < InWorldSeconds)
            return;
        if (_fresh)
        {
            _fresh = false;
            Create(_text);
        }
        _checkIn -= dt;
        if (_checkIn <= 0)
        {
            _checkIn = 0.2f;
            _menu = File.Exists(ConsoleBridge.MenuFlag);
            _moving |= File.Exists(TravelRan);
        }
        // Aside for a menu (the quest pop-up must be closed before the move), but not once the add-on has run the
        // coc: the load that follows looks like a menu.
        var want = _moving || !_menu;
        if (_viewport is { } v && want != _visible)
        {
            _visible = want;
            cef.SetVisible(v, want);
            foreach (var (old, _, _) in _retired)
                cef.SetVisible(old, want);
        }
    }

    private void Create(string text)
    {
        try
        {
            // A page already up stays black until the new one has loaded (no fade on a re-raise), then goes.
            var again = _viewport is not null && _destroyIn < 0;
            Retire();
            var viewport = cef.CreateViewport(zOrder: 3);
            cef.SetPlacement(viewport, ViewportPlacement.FullScreen);
            cef.SetInputMode(viewport, CefInputMode.DisplayOnly);
            cef.SetFocusable(viewport, false);
            var browser = cef.CreateBrowser();
            browser.Navigate($"readym://{Host}/travel.html#{(again ? "now:" : "")}{Uri.EscapeDataString(text)}");
            cef.Assign(viewport, browser.Handle);
            cef.SetVisible(viewport, _moving || !_menu);
            _visible = _moving || !_menu;
            _viewport = viewport;
            _browser = browser.Handle;
            if (_shown++ < 20)
                logger.LogInformation("[QuestSync] Travel screen: {Text}", text);
        }
        catch (Exception e)
        {
            logger.LogWarning("[QuestSync] Could not show the travel screen: {Error}", e.Message);
        }
    }

    /// <summary>The page fades out (a new hash), then goes.</summary>
    private void Lift()
    {
        if (_browser is not { } b)
            return;
        try
        {
            cef.Navigate(b, $"readym://{Host}/travel.html#lift");
        }
        catch (Exception e)
        {
            logger.LogDebug("[QuestSync] Travel screen fade failed: {Error}", e.Message);
        }
        _destroyIn = FadeSeconds + 0.2f;
    }

    private readonly List<(ViewportHandle Viewport, BrowserHandle Browser, float In)> _retired = new();

    private void Retire()
    {
        if (_viewport is { } v && _browser is { } b)
            _retired.Add((v, b, 0.6f));
        _viewport = null;
        _browser = null;
        _destroyIn = -1;
    }

    private void Destroy()
    {
        _destroyIn = -1;
        try
        {
            if (_viewport is { } v)
                cef.DestroyViewport(v);
            if (_browser is { } b)
                cef.DestroyBrowser(b);
        }
        catch (Exception e)
        {
            logger.LogDebug("[QuestSync] Travel screen cleanup failed: {Error}", e.Message);
        }
        _viewport = null;
        _browser = null;
        _visible = false;
    }
}
