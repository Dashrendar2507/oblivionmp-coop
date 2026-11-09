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
    public event Action<PlayerId>? JoinRequested;
    public event Action<PlayerId>? GotoRequested;

    /// <summary>
    /// Asked first when the join key comes in: true when it was taken (a teammate asked to come, and this accepts), so
    /// the conversation join doesn't run as well.
    /// </summary>
    public Func<PlayerId, bool>? JoinTaken;

    partial void OnJoinTalker(RpcContext context)
    {
        if (JoinTaken?.Invoke(context.Sender) == true)
            return;
        JoinRequested?.Invoke(context.Sender);
    }

    partial void OnTravelToFriend(RpcContext context) => GotoRequested?.Invoke(context.Sender);

    partial void OnToggleFollow(RpcContext context) => FollowToggled?.Invoke(context.Sender);

    partial void OnReviveNearest(RpcContext context) => ReviveRequested?.Invoke(context.Sender);

    partial void OnGiveUp(RpcContext context) => GiveUpRequested?.Invoke(context.Sender);
}
