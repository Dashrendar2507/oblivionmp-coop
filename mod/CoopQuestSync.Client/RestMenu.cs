using System.Reflection;
using Microsoft.Extensions.Logging;
using OblivionMP.SDK;
using ReadyM.Api.DI;
using ReadyM.SDK.Attributes;

namespace CoopQuestSync.Client;

/// <summary>
/// Shows the game's wait menu when the server's co-op rule handles waiting and sleeping (<see cref="ClientRpc"/>,
/// RestRule "vote" or "anyone"; see the server's RestVote). OblivionMP 1.2 hides it out of the box (its client setting
/// HideWaitMenu, which the host would otherwise have to change in OblivionMP's own config file).
///
/// The SDK has no public switch for it, only its own internal one (NativeSleepWaitControl.SetHideWaitMenu, which its
/// TimeSkipBridge calls once at start from that setting), so it is reached by name. If a later OblivionMP renames it,
/// this logs once and the menu stays as OblivionMP's setting has it; the vote still works for hosts who set
/// HideWaitMenu to false themselves.
/// </summary>
[Service]
public sealed partial class RestMenu(IDependencyContainer services, ILogger logger)
{
    private string? _wanted;      // the server's rule, not applied yet
    private bool? _shown;         // what this game's menu is set to by us
    private bool _broken;

    public void Set(string mode)
    {
        _wanted = mode;
    }

    private void Update()
    {
        if (_wanted is not { } mode || _broken)
            return;
        _wanted = null;
        // "off": back to OblivionMP's own setting.
        var show = mode is "vote" or "anyone" || !HideByConfig();
        if (_shown == show)
            return;
        try
        {
            var control = SleepWaitControl() ?? throw new MissingMemberException("SleepWaitControl");
            var set = control.GetType().GetMethod("SetHideWaitMenu", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, [typeof(bool)])
                ?? throw new MissingMethodException("SetHideWaitMenu");
            set.Invoke(control, [!show]);
            _shown = show;
            logger.LogInformation("[QuestSync] Wait menu {State} (co-op rule \"{Mode}\").", show ? "shown" : "back to OblivionMP's setting", mode);
        }
        catch (Exception e)
        {
            _broken = true;
            logger.LogWarning("[QuestSync] Could not show the wait menu ({Error}). The host can set \"HideWaitMenu\": false in "
                + "mods\\OblivionMp.Sdk\\client\\config.json on the server instead.", e.Message);
        }
    }

    /// <summary>OblivionMP's container (the one mods are handed, else its static instance) holds the control.</summary>
    private object? SleepWaitControl()
    {
        const BindingFlags any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        if (services.GetType().GetProperty("SleepWaitControl", any) is { } onServices)
            return onServices.GetValue(services);
        var di = typeof(OblivionMpCSharpMod.Input.ModifierKeys).Assembly.GetType("OblivionMpCSharpMod.DI");
        var instance = di?.GetProperty("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
        return instance is null ? null : di!.GetProperty("SleepWaitControl", any)?.GetValue(instance);
    }

    private bool HideByConfig()
    {
        try
        {
            return services.Resolve<ClientConfig>().HideWaitMenu;
        }
        catch
        {
            return true;
        }
    }
}
