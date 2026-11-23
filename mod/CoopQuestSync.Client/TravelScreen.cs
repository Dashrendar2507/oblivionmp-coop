using Microsoft.Extensions.Logging;
using OblivionMP.SDK;
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
public sealed partial class TravelScreen(CefHost cef, IPlayers players, IInput input, ConsoleBridge console, ILogger logger)
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
    private float _menuFor;
    private const float MenuAsideSeconds = 2.5f;
    private bool _moving;           // the add-on ran the coc/cow: stay up through the load
    private static string FadeIn => Path.Combine(Path.GetDirectoryName(ConsoleBridge.MenuFlag)!, "fade-in");
    // After the add-on's coc/cow: the load tears this page down, so watch for the load to end, then make the page
    // again and tell the add-on to bring the world back (it faded it to black before the move).
    private bool _watching;
    private bool _sawLoad;
    private float _watchFor;
    private float _settled;
    private float _fadeInIn = -1;
    private static string TravelRan => Path.Combine(Path.GetDirectoryName(ConsoleBridge.MenuFlag)!, "travel-ran");
    /// <summary>The add-on dropped the move (the player went elsewhere first, or it waited too long): no trip comes.</summary>
    private static string TravelDropped => Path.Combine(Path.GetDirectoryName(ConsoleBridge.MenuFlag)!, "travel-dropped");
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

    /// <summary>A load took the page (the rebirth's reload): make it again, black at once, if the screen is wanted.</summary>
    public void Refresh()
    {
        _held = false;
        if (_text is not null)
            _fresh = true;
    }

    /// <summary>
    /// The rebirth's reload is starting: make no page until <see cref="Refresh"/>. A page made right after the rebirth,
    /// while OblivionMP was tearing the world down for the reload, left that reload loading forever (2026-11-13).
    /// </summary>
    public void Hold() => _held = true;

    private bool _held;

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
        WatchMove(dt);
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
        if (_inWorld < InWorldSeconds || _held || players.LocalMainCharacter is null)
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
        // Aside for a menu that stays open (a quest pop-up the add-on couldn't close must be closed by hand before
        // the move), but not for the add-on's own quick close of the catch-up's pop-ups, which flashed the game
        // between two fades (2026-11-07), and not once the add-on has run the coc: the load that follows looks like
        // a menu.
        _menuFor = _menu ? _menuFor + dt : 0;
        var want = _moving || _menuFor < MenuAsideSeconds;
        if (_viewport is { } v && want != _visible)
        {
            _visible = want;
            cef.SetVisible(v, want);
            foreach (var (old, _, _) in _retired)
                cef.SetVisible(old, want);
        }
    }

    private bool _saidFadeOk;

    private void WatchMove(float dt)
    {
        // Tells the add-on this client brings the world back after a move, so it may fade it out first.
        if (!_saidFadeOk && Directory.Exists(Path.GetDirectoryName(FadeIn)))
        {
            _saidFadeOk = true;
            Touch(Path.Combine(Path.GetDirectoryName(FadeIn)!, "fade-ok"));
        }
        if (_fadeInIn >= 0)
        {
            _fadeInIn -= dt;
            if (_fadeInIn < 0)
                Touch(FadeIn);
        }
        _checkIn2 -= dt;
        if (!_watching)
        {
            if (_checkIn2 > 0)
                return;
            _checkIn2 = 0.2f;
            if (File.Exists(TravelDropped))
            {
                try { File.Delete(TravelDropped); } catch { /* next time */ }
                if (_text is not null)
                {
                    logger.LogInformation("[QuestSync] The trip was called off (you went elsewhere first); travel screen lifted.");
                    _text = null;
                    Lift();
                }
                return;
            }
            if (!File.Exists(TravelRan))
                return;
            // positionworld too: a short hop outdoors may not load at all, so don't wait 20 s for one.
            try { _watchedPositionCell = File.ReadAllText(TravelRan).StartsWith("player.position", StringComparison.Ordinal); }
            catch { _watchedPositionCell = false; }
            _watching = true;
            _sawLoad = false;
            _watchFor = 0;
            _settled = 0;
        }
        _watchFor += dt;
        var playing = players.LocalMainCharacter is not null && input.CanApplyInput();
        if (!playing)
        {
            _sawLoad = true;
            _settled = 0;
        }
        else if (_sawLoad)
            _settled += dt;
        // A positioncell the game takes starts its load within a second or two; one it ignores never does
        // (2026-11-13 12:27 and 12:35), so don't wait the full 20 s to find out.
        var noLoadAfter = _watchedPositionCell ? PositionCellNoLoadSeconds : 20f;
        if (_settled < 1f && (_sawLoad ? _watchFor < 20f : _watchFor < noLoadAfter))
            return;
        _watching = false;
        _moving = false;
        string ran = "";
        try { ran = File.ReadAllText(TravelRan).Trim(); } catch { /* gone, or the add-on is writing it */ }
        try { File.Delete(TravelRan); } catch { /* next time */ }
        logger.LogInformation("[QuestSync] In the new place after the move{Note}.", _settled >= 1f ? "" : " (no load seen)");
        // A player.positioncell the game ignored (no load, 2026-11-13 12:27): the door's coc now, not after the
        // server's 45 s.
        if (!_sawLoad && ran.StartsWith("player.positioncell ", StringComparison.Ordinal)
            && ran.Split(' ') is { Length: 6 } parts)
        {
            logger.LogInformation("[QuestSync] The game ignored {Command}; going in through the door instead.", ran);
            console.GoToCell(parts[5]);
            return;
        }
        if (_text is not null)
        {
            _fresh = true;      // the load took the page: a new one, black at once
            _fadeInIn = 0.6f;   // the world comes back behind it once it has painted
        }
        else
            Touch(FadeIn);
    }

    private float _checkIn2;
    private bool _watchedPositionCell;
    private const float PositionCellNoLoadSeconds = 6f;

    private void Touch(string path)
    {
        try
        {
            File.WriteAllText(path, "1");
        }
        catch (Exception e)
        {
            logger.LogDebug("[QuestSync] Could not write {Path}: {Error}", path, e.Message);
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
            var show = _moving || _menuFor < MenuAsideSeconds;
            cef.SetVisible(viewport, show);
            _visible = show;
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
