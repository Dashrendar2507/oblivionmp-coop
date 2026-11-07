using Microsoft.Extensions.Logging;
using OblivionMp.Sdk;
using OblivionMpCSharpMod;
using ReadyM.Cef;
using ReadyM.Cef.Rpc;
using ReadyM.SDK.Attributes;

namespace CoopQuestSync.Client;

/// <summary>
/// The screen that shows another player's conversation, styled like the game's dialogue screen. It is display only:
/// it takes no mouse or keyboard, so the witness cannot pick topics (the topic list is the talker's alone).
/// </summary>
[Service]
public sealed partial class WitnessOverlay(CefHost cef, OverlayPage page, IInput input, ConsoleBridge console, GameTextCache text, ILogger logger)
{
    private const string Host = "coopwitness";

    private ViewportHandle _viewport;
    private bool _created;
    private bool _active;     // a conversation is on (or offered)
    private bool _visible;
    private float _menuFor;   // how long a menu (map, inventory, chat...) has been open
    private float _checkIn;
    private bool _menuFlag;   // the CoopQuestSyncUI add-on sees a menu open
    private float _clock;

    /// <summary>
    /// Messages waiting for their text: the add-on looks up the keys this game lacks, which takes a moment. They go
    /// out in order, each once its text is in or after <see cref="TextWaitSeconds"/>.
    /// </summary>
    private readonly Queue<(Action<string> Show, string Json, float Until)> _waiting = new();
    private const float TextWaitSeconds = 1.5f;

    /// <summary>Hides the overlay while this player has a menu open, such as the map, and brings it back after.</summary>
    private void Update()
    {
        _clock += Time.DeltaTime;
        while (_waiting.Count > 0 && (_clock >= _waiting.Peek().Until || text.Missing(_waiting.Peek().Json).Count == 0))
        {
            var (show, json, _) = _waiting.Dequeue();
            show(json);
        }
        if (!_active)
            return;
        _checkIn -= Time.DeltaTime;
        if (_checkIn <= 0)
        {
            _checkIn = 0.2f;
            _menuFlag = File.Exists(ConsoleBridge.MenuFlag);
        }
        _menuFor = input.CanApplyInput() && !_menuFlag ? 0 : _menuFor + Time.DeltaTime;
        var want = _menuFor < 0.25f;
        if (want == _visible)
            return;
        _visible = want;
        cef.SetVisible(_viewport, want);
        logger.LogInformation("[QuestSync] Witness overlay {State}.", want ? "shown again: menu closed" : "hidden: a menu is open");
    }

    private void Start()
    {
        // Content must be registered while mods start, never later.
        var directory = Path.GetDirectoryName(typeof(WitnessOverlay).Assembly.Location)!;
        cef.RegisterFolder(Host, Path.Combine(directory, "web"));
        // The page says when it shows a spoken line, so the voice starts with its text.
        page.Speak += voice =>
        {
            if (voice == "stop")
                console.StopVoice();
            else
                console.Voice(voice);
        };
    }

    public void Started(string json) => Queue(ShowStart, json);

    public void Line(string json) => Queue(ShowLine, json);

    public void Activity(string json) => Queue(ShowActivity, json);

    /// <summary>Shows the message now if its text is known (or can't be had), else once the add-on answers.</summary>
    private void Queue(Action<string> show, string json)
    {
        var missing = text.Missing(json);
        if (missing.Count > 0)
            text.Request(missing);
        if (_waiting.Count == 0 && missing.Count == 0)
            show(json);
        else
            _waiting.Enqueue((show, json, _clock + TextWaitSeconds));
    }

    private void ShowStart(string json)
    {
        EnsureCreated();
        page.Show(text.Localize(json));
        _active = true;
        _menuFor = 0;
        _visible = true;
        cef.SetVisible(_viewport, true);
    }

    private void ShowLine(string json)
    {
        EnsureCreated();
        page.AddLine(text.Localize(json));
    }

    private void ShowActivity(string json)
    {
        EnsureCreated();
        page.SetActivity(json);
    }

    public void Ended()
    {
        // Whatever still waits for its text belongs to this conversation: it ends with it.
        _waiting.Clear();
        if (!_created)
            return;
        page.Hide();
        _active = false;
        _visible = false;
        cef.SetVisible(_viewport, false);
    }

    private void EnsureCreated()
    {
        if (_created)
            return;
        _created = true;
        _viewport = cef.CreateViewport(zOrder: 1);
        cef.SetPlacement(_viewport, ViewportPlacement.FullScreen);
        cef.SetInputMode(_viewport, CefInputMode.DisplayOnly);
        cef.SetFocusable(_viewport, false);
        var browser = cef.CreateBrowser();
        page.Page = browser.Handle;
        browser.Navigate($"readym://{Host}/");
        cef.Assign(_viewport, browser.Handle);
        logger.LogInformation("[QuestSync] Witness overlay ready.");
    }
}

/// <summary>Messages to the overlay page. Remembers what is on screen, for a page that loads after the fact.</summary>
public sealed partial class OverlayPage
{
    private BrowserHandle? _page;
    private string? _current;
    private readonly List<string> _lines = new();
    private string? _activity;

    public BrowserHandle Page { set => _page = value; }

    public void Show(string json)
    {
        _current = json;
        _lines.Clear();
        _activity = null;
        if (_page is { } p) SendStart(p, json);
    }

    public void AddLine(string json)
    {
        _lines.Add(json);
        if (_page is { } p) SendLine(p, json);
    }

    /// <summary>The talker opened or left a menu (barter, persuasion...): the page says so and waits.</summary>
    public void SetActivity(string json)
    {
        _activity = json;
        if (_page is { } p) SendActivity(p, json);
    }

    public void Hide()
    {
        _current = null;
        _lines.Clear();
        _activity = null;
        if (_page is { } p) SendEnd(p);
    }

    /// <summary>The page's first message once it has loaded.</summary>
    /// <summary>The page shows a spoken line ("imperial_m ms26_..._1"), or "stop" when the voice should end.</summary>
    public event Action<string>? Speak;

    [ReceiveFromCef]
    private void OnSpeak(BrowserHandle page, string voice) => Speak?.Invoke(voice);

    [ReceiveFromCef]
    private void OnReady(BrowserHandle page)
    {
        if (_current is null)
            return;
        SendStart(page, _current);
        foreach (var line in _lines)
            SendLine(page, line);
        if (_activity is not null)
            SendActivity(page, _activity);
    }

    [SendToCef] public partial void SendStart(BrowserHandle page, string json);
    [SendToCef] public partial void SendLine(BrowserHandle page, string json);
    [SendToCef] public partial void SendEnd(BrowserHandle page);
    [SendToCef] public partial void SendActivity(BrowserHandle page, string json);
}
