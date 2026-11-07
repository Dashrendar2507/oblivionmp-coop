using Microsoft.Extensions.Logging;
using OblivionMp.ChatMod.Common;
using OblivionMp.ChatMod.Serverside;
using ReadyM.Api.Idents;
using ReadyM.Relay.Server.Sdk.Players;
using ReadyM.SDK.Attributes;

namespace CoopQuestSync.Server;

/// <summary>
/// Decides who may use the commands that change things for everybody.
///
/// An admin is anyone listed in the ReadyM admin panel's Admins view (read from its database, see
/// <see cref="PanelAdminDb"/>), plus anyone added on our own web page. Players are recognised by their ReadyM account
/// id, which can't be faked the way a nickname can. The page's "Commands" setting can also open them to everyone.
/// </summary>
[Service]
public sealed partial class CommandAccess(IPlayers players, IChat chat, QuestTokenSync sync, QuestSyncConfig config, ILogger logger)
{
    /// <summary>How long the panel's admin list is kept before it is read again, so changes there apply within seconds.</summary>
    private static readonly TimeSpan PanelRefresh = TimeSpan.FromSeconds(10);

    private readonly object _gate = new();
    private PanelAdminDb.Result _panel = new([], false, "not read yet");
    private DateTime _panelReadAt = DateTime.MinValue;
    private string? _loggedStatus;

    public enum Reason { Everyone, Panel, Page, NotAdmin, UnknownAccount, Ghost }

    /// <summary>Accounts testing as Ghost (/coop test asghost): treated as non-admins until they turn it off.</summary>
    private readonly HashSet<Guid> _asGhost = [];

    private void Start()
    {
        sync.PlayerAccess = id =>
        {
            var why = Check(id);
            return new { admin = why is Reason.Everyone or Reason.Panel or Reason.Page, why = why.ToString().ToLowerInvariant(), readyMId = players.GetReadyMId(id)?.ToString() };
        };
        sync.AccessInfo = () => new { panel = PanelStatus(), panelOk = Panel().Ok, panelAdmins = Panel().Ids.Count };
        Panel();
    }

    /// <summary>Whether <paramref name="player"/> may use admin commands, and why.</summary>
    public Reason Check(PlayerId player)
    {
        if (sync.Settings.CommandAccess == "everyone")
            return Reason.Everyone;
        if (players.GetReadyMId(player) is not { } account)
            return Reason.UnknownAccount;
        lock (_asGhost)
            if (_asGhost.Contains(account))
                return Reason.Ghost;
        if (Panel().Ids.Contains(account))
            return Reason.Panel;
        if (sync.Settings.Admins.ToArray().Any(a => Guid.TryParse(a.Id, out var g) && g == account))
            return Reason.Page;
        return Reason.NotAdmin;
    }

    public bool IsAdmin(PlayerId player) => Check(player) is Reason.Everyone or Reason.Panel or Reason.Page;

    /// <summary>
    /// For a command handler: true when <paramref name="player"/> may run <paramref name="command"/>. Otherwise tells
    /// them why not, logs it and returns false.
    /// </summary>
    public bool Allow(PlayerId player, string command)
    {
        var why = Check(player);
        if (why is Reason.Everyone or Reason.Panel or Reason.Page)
            return true;
        if (why == Reason.Ghost)
        {
            logger.LogInformation("[QuestSync] {Player} (testing as Ghost) was refused {Command}.", sync.Nickname(player), command);
            chat.SendTo(player, $"Only server admins can use {command}. (You are testing as Ghost; /coop test asghost off gives your rights back.)", ChatLineKind.Rejection);
            return false;
        }
        logger.LogInformation("[QuestSync] {Player} tried {Command} but is not an admin.", sync.Nickname(player), command);
        chat.SendTo(player, $"Only server admins can use {command}. The host can make you one in the ReadyM admin panel "
            + "(Admins), or on the co-op page. /qs whoami shows your ReadyM id.", ChatLineKind.Rejection);
        return false;
    }

    /// <summary>For /qs whoami.</summary>
    public string Describe(PlayerId player)
    {
        var account = players.GetReadyMId(player);
        var verdict = Check(player) switch
        {
            Reason.Everyone => "Admin commands are open to everyone on this server.",
            Reason.Panel => "You are an admin (listed in the ReadyM admin panel).",
            Reason.Page => "You are an admin (added on the co-op page).",
            Reason.UnknownAccount => "The server doesn't know your ReadyM account yet, so you are not an admin.",
            Reason.Ghost => "You are testing as Ghost, so you are not an admin. /coop test asghost off gives your rights back.",
            _ => "You are not an admin.",
        };
        return $"{verdict} (Your ReadyM account id, which the server uses instead of your name: {account?.ToString() ?? "unknown"}.)";
    }

    /// <summary>/coop test asghost on|off. Keyed by account, so it survives a rejoin and can't pass to someone else.</summary>
    public string SetActAsGhost(PlayerId player, bool on)
    {
        if (players.GetReadyMId(player) is not { } account)
            return "The server doesn't know your ReadyM account yet; try again in a moment.";
        lock (_asGhost)
        {
            if (on)
                _asGhost.Add(account);
            else
                _asGhost.Remove(account);
        }
        logger.LogInformation("[QuestSync] {Player} is {State} testing as Ghost (not an admin).", sync.Nickname(player), on ? "now" : "no longer");
        if (!on)
            return "You have your own rights back. " + Describe(player);
        return sync.Settings.CommandAccess == "everyone"
            ? "You are now treated like Ghost, but admin commands are open to everyone on the co-op page, so nothing is refused. Set \"Who can use admin commands\" to \"Admins only\" to see the refusals."
            : "You are now treated like Ghost, a player who isn't an admin. Try /qs auto off or /qs reset: they should be refused. /coop test asghost off gives your rights back.";
    }

    /// <summary>Adds an online player as an admin on our page, by their ReadyM id. Runs on the game loop.</summary>
    public string? AddPageAdmin(string nickname)
    {
        foreach (var (id, _) in sync.Players)
        {
            if (sync.Nickname(id) != nickname)
                continue;
            if (players.GetReadyMId(id) is not { } account)
                return "the server doesn't know their ReadyM account yet";
            if (!sync.Settings.Admins.ToArray().Any(a => Guid.TryParse(a.Id, out var g) && g == account))
                sync.Settings.Admins.Add(new CoopSettings.AdminEntry { Id = account.ToString(), Name = nickname });
            sync.MarkDirty();
            logger.LogInformation("[QuestSync] Web: {Player} ({Account}) is now an admin.", nickname, account);
            return null;
        }
        return "no such player online";
    }

    /// <summary>Removes an admin added on our page. Runs on the game loop.</summary>
    public void RemovePageAdmin(string id)
    {
        if (sync.Settings.Admins.RemoveAll(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase)) > 0)
        {
            sync.MarkDirty();
            logger.LogInformation("[QuestSync] Web: {Account} is no longer an admin (page list).", id);
        }
    }

    private string PanelStatus() => string.IsNullOrWhiteSpace(config.PanelDatabase) ? "off in config.json" : Panel().Status;

    private PanelAdminDb.Result Panel()
    {
        if (string.IsNullOrWhiteSpace(config.PanelDatabase))
            return new PanelAdminDb.Result([], false, "off");
        lock (_gate)
        {
            if (DateTime.UtcNow - _panelReadAt < PanelRefresh)
                return _panel;
            _panelReadAt = DateTime.UtcNow;
            _panel = PanelAdminDb.Load(config.PanelDatabase, config.PanelPermission.Trim());
            if (_panel.Status != _loggedStatus)
            {
                _loggedStatus = _panel.Status;
                if (_panel.Ok)
                    logger.LogInformation("[QuestSync] ReadyM panel admins: {Status}", _panel.Status);
                else
                    logger.LogWarning("[QuestSync] ReadyM panel admins can't be used: {Status}. Admins added on the co-op page still work.", _panel.Status);
            }
            return _panel;
        }
    }
}
