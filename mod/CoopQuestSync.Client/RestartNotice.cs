using Microsoft.Extensions.Logging;
using OblivionMp.Sdk;
using OblivionMpCSharpMod;
using ReadyM.Cef;
using ReadyM.SDK.Attributes;

namespace CoopQuestSync.Client;

/// <summary>
/// A small note in a corner of the screen, for as long as the game runs: the CoopQuestSyncUI add-on was updated and
/// only a restart of the game turns the new one on. <see cref="ConsoleBridge"/> asks for it when the add-on the game
/// started with (its running.txt) isn't the one this mod brings.
/// </summary>
[Service]
public sealed partial class RestartNotice(CefHost cef, IPlayers players, IInput input, ILogger logger)
{
    /// <summary>The overlay's content folder (WitnessOverlay registers it while mods start).</summary>
    private const string Host = "coopwitness";
    /// <summary>
    /// Shown once the player has been in the world this long without a menu: a page made before the join's reload
    /// was lost with it (2026-11-07).
    /// </summary>
    private const float DelaySeconds = 5f;

    private bool _wanted;
    private bool _shown;
    private float _wait = DelaySeconds;

    public void Require()
    {
        if (_wanted)
            return;
        _wanted = true;
        logger.LogInformation("[QuestSync] The game still runs an older CoopQuestSyncUI add-on: showing the restart note.");
    }

    private void Update()
    {
        if (!_wanted || _shown)
            return;
        if (players.LocalMainCharacter is null || !input.CanApplyInput())
        {
            _wait = DelaySeconds;
            return;
        }
        _wait -= Time.DeltaTime;
        if (_wait > 0)
            return;
        _shown = true;
        try
        {
            var viewport = cef.CreateViewport(zOrder: 2);
            cef.SetPlacement(viewport, ViewportPlacement.FullScreen);
            cef.SetInputMode(viewport, CefInputMode.DisplayOnly);
            cef.SetFocusable(viewport, false);
            var browser = cef.CreateBrowser();
            browser.Navigate($"readym://{Host}/notice.html");
            cef.Assign(viewport, browser.Handle);
            cef.SetVisible(viewport, true);
            logger.LogInformation("[QuestSync] Restart note shown.");
        }
        catch (Exception e)
        {
            logger.LogWarning("[QuestSync] Could not show the restart note: {Error}", e.Message);
        }
    }
}
