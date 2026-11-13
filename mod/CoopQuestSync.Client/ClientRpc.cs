using CoopQuestSync.Common;
using Microsoft.Extensions.Logging;
using OblivionMP.SDK;
using OblivionMP.SDK.Archetypes.Extensions;
using OblivionMpCSharpMod;
using ReadyM.SDK.Attributes;

namespace CoopQuestSync.Client;

/// <summary>Client side of <see cref="CoopRpcContracts"/>: what the server tells this game to show or do.</summary>
[RpcHandlersFor(typeof(CoopRpcContracts))]
public sealed partial class ClientRpc(WitnessOverlay overlay, PartyTags tags, CoopKeys keys, ConsoleBridge console, Resurrection resurrection, TravelScreen travel, CharacterMenus menus, GameTextCache text, DownNote down, IUserInterface ui)
{
    partial void OnConversationStarted(string json) => overlay.Started(json);

    partial void OnConversationLine(string json) => overlay.Line(json);

    partial void OnConversationActivity(string json) => overlay.Activity(json);

    partial void OnConversationEnded() => overlay.Ended();

    partial void OnPartyStatus(string json) => tags.Update(json);

    partial void OnKeys(string json) => keys.Update(json);

    partial void OnGoToCell(string cell) => console.GoToCell(cell);

    partial void OnGoToWorld(string place) => console.GoToWorld(place);

    partial void OnStopCombat(string refs) => console.StopCombat(refs);

    partial void OnStopChase() => console.Run("player.scaonactor");

    partial void OnClearBounty() => console.Run("player.SetCrimeGold 0");

    partial void OnTravel(string text) => travel.Set(text);

    partial void OnPlayVoice(string voice) => console.Voice(voice);

    partial void OnNotice(string text) => ui.ShowMessage(text, MessagePosition.Center, 5f);

    partial void OnResurrect() => resurrection.Request();

    partial void OnMakeCharacter() => menus.Request();

    partial void OnTextWanted(string keys) => text.ServerWants(keys);

    partial void OnDown(string text) => down.Set(text);
}
