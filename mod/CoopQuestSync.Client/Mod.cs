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

        // The co-op keys (Alt+U listen in or leave, Alt+H revive, Alt+G respawn; changeable on the web page).
        // /coop follow, /coop revive and /coop giveup in chat do the same.
        var keys = new CoopKeys(input, () => services.Resolve<ClientRpc>(), logger);
        services.RegisterSingleton<CoopKeys, CoopKeys>(keys);
        keys.Start();

        // The game's text: the server asks this game for it (in its language) and keeps it for its page and chat.
        GameTextCache.Rpc = () => services.Resolve<ClientRpc>();

        logger.LogInformation("[QuestSync] Client half loaded.");
    }
}
