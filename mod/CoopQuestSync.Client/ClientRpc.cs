using CoopQuestSync.Common;
using Microsoft.Extensions.Logging;
using OblivionMp.Sdk;
using OblivionMp.Sdk.Archetypes.Extensions;
using OblivionMpCSharpMod;
using ReadyM.SDK.Attributes;

namespace CoopQuestSync.Client;

/// <summary>Client side of <see cref="CoopRpcContracts"/>: what the server tells this game to show or do.</summary>
[RpcHandlersFor(typeof(CoopRpcContracts))]
public sealed partial class ClientRpc(WitnessOverlay overlay, PartyTags tags, IPlayers players, IUserInterface ui, ILogger logger)
{
    partial void OnConversationStarted(string json) => overlay.Started(json);

    partial void OnConversationLine(string json) => overlay.Line(json);

    partial void OnConversationActivity(string json) => overlay.Activity(json);

    partial void OnConversationEnded() => overlay.Ended();

    partial void OnPartyStatus(string json) => tags.Update(json);

    partial void OnNotice(string text) => ui.ShowMessage(text, MessagePosition.Center, 5f);

    partial void OnResurrect()
    {
        if (players.LocalMainCharacter is { IsDead: true } me)
        {
            me.ResurrectLocalPlayer();
            logger.LogInformation("[QuestSync] Brought back to life by the server.");
        }
    }
}
