using Kgs.Game.Contracts;
using Proto;

namespace Kgs.Game.Actors;

public interface IActorPacketRouteResolver
{
    ValueTask<PID?> ResolveAsync(
        PacketDispatchRequest request,
        CancellationToken cancellationToken);
}
