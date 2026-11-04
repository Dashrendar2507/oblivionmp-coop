using CoopQuestSync.Common;
using Microsoft.Extensions.Logging;
using ReadyM.Api.DI;
using ReadyM.Cef.Rpc;
using ReadyM.SDK.Attributes;
using ReadyM.Sdk.Common.Input;
using OblivionMp.Sdk;

namespace CoopQuestSync.Client;

[ModEntry]
public sealed partial class Mod(IDependencyContainer services, IInput input, ILogger logger)
{
    private void Init()
    {
        services.RegisterSingleton<OverlayPage>();
        services.RegisterSingleton<ICefRpcHandler, OverlayPage>(services.Resolve<OverlayPage>());

        // Alt+R: revive the downed teammate next to you. Alt+G: when you are down, respawn by the party now.
        // Alt+F: stop watching someone's conversation, or listen in on one.
        // Polled, not UnhandledByUi: R and F are game keys (ready weapon, view), so the game's own input takes those
        // presses first and an "unhandled" bind never fires. The chat commands /coop revive, /coop giveup and
        // /coop follow do the same.
        Bind(Key.R, "Alt+R (revive)", () => services.Resolve<ClientRpc>().SendReviveNearest());
        Bind(Key.G, "Alt+G (respawn)", () => services.Resolve<ClientRpc>().SendGiveUp());
        Bind(Key.F, "Alt+F (follow)", () => services.Resolve<ClientRpc>().SendToggleFollow());

        logger.LogInformation("[QuestSync] Client half loaded. Alt+R revives a downed teammate, Alt+G respawns when you are down, Alt+F follows or leaves a conversation.");
    }

    private readonly Dictionary<Key, DateTime> _lastPress = new();

    /// <summary>A polled bind fires for as long as the keys are down: take one press per half second, and none
    /// while the chat or a menu has the keyboard.</summary>
    private void Bind(Key key, string name, Action action)
    {
        input.RegisterKeyBind(ModifierKeys.Alt, key, () =>
        {
            var now = DateTime.UtcNow;
            if (_lastPress.TryGetValue(key, out var last) && now - last < TimeSpan.FromSeconds(0.5))
            {
                _lastPress[key] = now;
                return;
            }
            _lastPress[key] = now;
            if (!input.CanApplyInput())
                return;
            logger.LogInformation("[QuestSync] {Key} pressed.", name);
            action();
        }, KeySource.Polled);
    }
}
