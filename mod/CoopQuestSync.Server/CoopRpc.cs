using CoopQuestSync.Common;
using ReadyM.Api.Idents;
using ReadyM.Relay.Server.Sdk.Rpc;
using ReadyM.SDK.Attributes;

namespace CoopQuestSync.Server;

/// <summary>
/// Server side of <see cref="CoopRpcContracts"/>. The Send methods are generated; requests from players are passed
/// on as events, so the services that answer them do not depend on this class and the other way round.
/// </summary>
[RpcHandlersFor(typeof(CoopRpcContracts))]
public sealed partial class CoopRpc
{
    public event Action<PlayerId>? ReviveRequested;
    public event Action<PlayerId>? GiveUpRequested;
    public event Action<PlayerId>? FollowToggled;

    partial void OnToggleFollow(RpcContext context) => FollowToggled?.Invoke(context.Sender);

    partial void OnReviveNearest(RpcContext context) => ReviveRequested?.Invoke(context.Sender);

    partial void OnGiveUp(RpcContext context) => GiveUpRequested?.Invoke(context.Sender);
}
