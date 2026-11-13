using Microsoft.Extensions.Logging;
using OblivionMP.SDK;
using OblivionMP.SDK.Archetypes.Extensions;
using OblivionMpCSharpMod;
using ReadyM.SDK.Attributes;

namespace CoopQuestSync.Client;

/// <summary>
/// A new player who skipped the intro makes their character at the sewer exit: the race menu (name, race, face), then
/// the class, then the birthsign, one after the other, the way the intro would have shown them. Each opens once the
/// game is back in play (the move to the sewers is a load), and the next once the last one has closed. The sewer gate
/// then offers to change any of them again before going out, as in the game.
/// </summary>
[Service]
public sealed partial class CharacterMenus(IPlayers players, IInput input, IUserInterface ui, ILogger logger)
{
    /// <summary>In play this long before a menu opens: the load and the server's chat line settle first.</summary>
    private const float SettleSeconds = 2f;
    /// <summary>A menu that hasn't shown by then didn't open: go on to the next.</summary>
    private const float OpenTimeoutSeconds = 4f;
    private static readonly string[] Names = ["race", "class", "birthsign"];

    private int _step = -1;       // -1 nothing to do, else the menu to open next (or that is open)
    private bool _opened;         // asked the game to open it
    private bool _seen;           // the game says it is open
    private float _timer;

    public void Request()
    {
        _step = 0;
        _opened = false;
        _timer = 0;
        logger.LogInformation("[QuestSync] Making the character at the sewer exit: race, class and birthsign menus.");
    }

    private void Update()
    {
        if (_step < 0)
            return;
        if (players.LocalMainCharacter is not { } me)
        {
            _timer = 0;
            return;
        }
        var dt = Time.DeltaTime;
        if (_opened)
        {
            var open = ui.IsCharacterCreatorOpen();
            _timer += dt;
            if (open)
            {
                _seen = true;
                return;
            }
            if (!_seen && _timer < OpenTimeoutSeconds)
                return;
            if (!_seen)
                logger.LogWarning("[QuestSync] The {Menu} menu didn't open.", Names[_step]);
            _opened = false;
            _timer = 0;
            if (++_step >= Names.Length)
            {
                _step = -1;
                ui.ShowMessage("Your character is ready. The sewer gate lets you change it before you go out.", MessagePosition.Center, 6f);
                logger.LogInformation("[QuestSync] Character menus done.");
            }
            return;
        }
        // Wait for play: not loading, no message box or other menu open.
        if (!input.CanApplyInput())
        {
            _timer = 0;
            return;
        }
        _timer += dt;
        if (_timer < SettleSeconds)
            return;
        switch (_step)
        {
            case 0: me.OpenCharacterCreator(); break;
            case 1: me.OpenClassMenu(); break;
            default: me.OpenBirthSignMenu(); break;
        }
        _opened = true;
        _seen = false;
        _timer = 0;
        logger.LogInformation("[QuestSync] Opened the {Menu} menu.", Names[_step]);
    }
}
