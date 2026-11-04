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
        input.RegisterKeyBind(ModifierKeys.Alt, Key.R, () => services.Resolve<ClientRpc>().SendReviveNearest(), KeySource.UnhandledByUi);
        input.RegisterKeyBind(ModifierKeys.Alt, Key.G, () => services.Resolve<ClientRpc>().SendGiveUp(), KeySource.UnhandledByUi);
        // Alt+F: stop watching someone's conversation, or listen in on one.
        input.RegisterKeyBind(ModifierKeys.Alt, Key.F, () => services.Resolve<ClientRpc>().SendToggleFollow(), KeySource.UnhandledByUi);

        logger.LogInformation("[QuestSync] Client half loaded. Alt+R revives a downed teammate, Alt+G respawns when you are down, Alt+F follows or leaves a conversation.");
    }
}
