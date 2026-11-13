using Microsoft.Extensions.Logging;
using OblivionMp.Sdk;
using OblivionMp.Sdk.Archetypes.Extensions;
using OblivionMpCSharpMod;
using ReadyM.Api.DI;
using ReadyM.SDK.Attributes;

namespace CoopQuestSync.Client;

/// <summary>
/// Brings the player back to life when the server says so (a revive or a respawn), but only while no menu or chat
/// has the keyboard: being brought back with the chat open left the chat stuck open and unusable.
///
/// Coming back to life is OblivionMP's "rebirth": it heals the player and quickloads their own save, wherever that
/// was (2026-11-12). So this then watches for that reload to end and tells the server, which takes the player back
/// to the party behind the travel screen.
/// </summary>
[Service]
public sealed partial class Resurrection(IPlayers players, IInput input, IUserInterface ui, TravelScreen travel, DownNote down,
    IDependencyContainer services, ILogger logger)
{
    /// <summary>No load this long after the rebirth: there was none (or it was very quick).</summary>
    private const float NoLoadSeconds = 6f;
    /// <summary>Back in the world, able to move, for this long: the reload is over.</summary>
    private const float SettledSeconds = 1f;
    private const float GiveUpSeconds = 45f;

    private bool _pending;
    private float _waited;
    private bool _told;

    private bool _watching;
    private bool _sawLoad;
    private float _watchFor;
    private float _settled;

    /// <summary>The server's revive or respawn.</summary>
    public void Request()
    {
        _pending = true;
        _waited = 0;
        _told = false;
    }

    private bool _wasDead;

    private void Update()
    {
        // Back up without the server asking: OblivionMP's own rebirth (its R key, or on its own after a while,
        // 2026-11-13) heals and reloads the save. Watch that reload like ours, so the server takes the player back
        // after it, and keep the travel page away from the reload.
        var deadNow = players.LocalMainCharacter is { IsDead: true };
        if (_wasDead && !deadNow && players.LocalMainCharacter is not null && !_watching && !_pending)
        {
            down.Set("");
            travel.Hold();
            _watching = true;
            _sawLoad = false;
            _watchFor = 0;
            _settled = 0;
            logger.LogInformation("[QuestSync] OblivionMP's own rebirth brought this player back; watching its reload.");
        }
        if (players.LocalMainCharacter is not null)
            _wasDead = deadNow;
        WatchReload(Time.DeltaTime);
        if (!_pending)
            return;
        if (players.LocalMainCharacter is not { } me || !me.IsDead)
        {
            _pending = false;
            return;
        }
        if (!input.CanApplyInput())
        {
            _waited += Time.DeltaTime;
            if (!_told && _waited > 1f)
            {
                _told = true;
                logger.LogInformation("[QuestSync] Waiting for the chat or menu to close before getting back up.");
            }
            return;
        }
        _pending = false;
        _watching = true;
        _sawLoad = false;
        _watchFor = 0;
        _settled = 0;
        down.Set("");       // gone before the reload, not during it
        travel.Hold();      // no travel page while the reload tears the world down
        me.ResurrectLocalPlayer();
        logger.LogInformation("[QuestSync] Brought back to life by the server{Waited}.", _told ? " (after the chat or menu closed)" : "");
        if (_told)
            ui.ShowMessage("Back on your feet.", MessagePosition.Center, 3f);
    }

    private void WatchReload(float dt)
    {
        if (!_watching)
            return;
        _watchFor += dt;
        var playing = players.LocalMainCharacter is { IsDead: false } && input.CanApplyInput();
        if (!playing)
        {
            _sawLoad = true;
            _settled = 0;
        }
        else
            _settled += dt;
        var done = _sawLoad ? _settled >= SettledSeconds : _watchFor >= NoLoadSeconds;
        if (!done && _watchFor < GiveUpSeconds)
            return;
        _watching = false;
        logger.LogInformation("[QuestSync] Back in the world after the rebirth{Note}; telling the server.",
            _sawLoad ? "'s reload" : " (no reload seen)");
        travel.Refresh();
        try
        {
            services.Resolve<ClientRpc>().SendRebirthLoaded();
        }
        catch (Exception e)
        {
            logger.LogWarning("[QuestSync] Could not tell the server the reload is over: {Error}", e.Message);
        }
    }
}
