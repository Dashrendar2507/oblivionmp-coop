using Microsoft.Extensions.Logging;
using OblivionMP.SDK;
using OblivionMP.SDK.Archetypes.Extensions;
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
    /// <summary>No load this long after the rebirth: there was none (or it was very quick). OblivionMP's quickload
    /// started 2.5 s after the rebirth on 2026-11-13 11:51, and later than 6 s in a 12:2x test.</summary>
    private const float NoLoadSeconds = 15f;
    /// <summary>Without input this long is a load, not the chat closing or the death screen going away: a short blip
    /// counted as the reload once, so the black page went up over the world (the player could still punch) and the
    /// real reload came after it (2026-11-13).</summary>
    private const float LoadSeconds = 1.5f;
    /// <summary>Back in the world, able to move, for this long: the reload is over.</summary>
    private const float SettledSeconds = 1f;
    private const float GiveUpSeconds = 45f;

    private bool _pending;
    private float _waited;
    private bool _told;

    private bool _watching;
    private bool _sawLoad;
    private float _blocked;
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
            _blocked = 0;
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
        _blocked = 0;
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
        var me = players.LocalMainCharacter;
        var loading = me is null || !input.CanApplyInput();
        _blocked = loading ? _blocked + dt : 0;
        if (_blocked >= LoadSeconds || me is null)
            _sawLoad = true;
        if (loading || me is { IsDead: true })
            _settled = 0;
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
