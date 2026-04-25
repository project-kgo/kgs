using System.Text;
using Kgs.Game.Contracts;

namespace Kgs.Game.Actors;

public sealed class ActorPacketDispatcher(
    IActorSystemHost actorSystemHost,
    IActorPacketRouteResolver routeResolver) : IPacketDispatcher
{
    private readonly IActorSystemHost _actorSystemHost = actorSystemHost;
    private readonly IActorPacketRouteResolver _routeResolver = routeResolver;

    public async ValueTask<PacketDispatchResult> DispatchAsync(
        PacketDispatchRequest request,
        CancellationToken cancellationToken)
    {
        var target = await _routeResolver.ResolveAsync(request, cancellationToken);
        if (target is null)
        {
            return new PacketDispatchResult(new[]
            {
                CreateRouteNotFoundError(request.Packet.RequestId, request.Packet.OpCode)
            });
        }

        _actorSystemHost.ActorSystem.Root.Send(target, new PacketEnvelope(request));
        return PacketDispatchResult.Empty;
    }

    private static GamePacket CreateRouteNotFoundError(uint requestId, ushort opCode)
    {
        var payload = Encoding.UTF8.GetBytes($"No actor route for opcode: {opCode}");
        return new GamePacket(
            SystemOpCodes.Error,
            requestId,
            (ushort)ProtocolErrorCode.InvalidPacket,
            payload);
    }
}
