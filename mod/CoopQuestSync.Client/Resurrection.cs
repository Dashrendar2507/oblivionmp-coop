using Microsoft.Extensions.Logging;
using OblivionMp.Sdk;
using OblivionMp.Sdk.Archetypes.Extensions;
using OblivionMpCSharpMod;
using ReadyM.SDK.Attributes;

namespace CoopQuestSync.Client;

/// <summary>
/// Brings the player back to life when the server says so (a revive or a respawn), but only while no menu or chat
/// has the keyboard: being brought back with the chat open left the chat stuck open and unusable.
/// </summary>
[Service]
public sealed partial class Resurrection(IPlayers players, IInput input, IUserInterface ui, ILogger logger)
{
    private bool _pending;
    private float _waited;
    private bool _told;

    /// <summary>The server's revive or respawn.</summary>
    public void Request()
    {
        _pending = true;
        _waited = 0;
        _told = false;
    }

    private void Update()
    {
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
        me.ResurrectLocalPlayer();
        logger.LogInformation("[QuestSync] Brought back to life by the server{Waited}.", _told ? " (after the chat or menu closed)" : "");
        if (_told)
            ui.ShowMessage("Back on your feet.", MessagePosition.Center, 3f);
    }
}
