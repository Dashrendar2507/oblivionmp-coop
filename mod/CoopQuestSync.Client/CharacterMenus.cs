using Microsoft.Extensions.Logging;
using OblivionMP.SDK;
using OblivionMP.SDK.Archetypes.Extensions;
using OblivionMpCSharpMod;
using ReadyM.SDK.Attributes;

namespace CoopQuestSync.Client;

/// <summary>
/// A new character is made: the race menu (name, race, face), then the class, then the birthsign, one after the other,
/// the way the intro would have shown them. The server asks for it on a brand-new character's first join (OblivionMP
/// starts everyone as the same preset character), or at the sewer exit when the player skipped the intro. Each menu
/// opens once the game is in play (no load, no other menu), and the next once the last one has closed. A player who
/// chose the prison intro gets the race menu alone: the intro shows the birthsign and the class itself.
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
    private int _last = 2;        // the last menu to open

    public void Request(bool raceOnly = false)
    {
        _step = 0;
        _last = raceOnly ? 0 : Names.Length - 1;
        _opened = false;
        _timer = 0;
        logger.LogInformation(raceOnly ? "[QuestSync] Making the character: race menu." : "[QuestSync] Making the character: race, class and birthsign menus.");
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
            if (++_step > _last)
            {
                _step = -1;
                if (_last == Names.Length - 1)
                    ui.ShowMessage("Your character is ready.", MessagePosition.Center, 5f);
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
