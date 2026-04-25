namespace Kgs.Game.Contracts;

public interface IPacketDispatcher
{
    ValueTask<PacketDispatchResult> DispatchAsync(
        PacketDispatchRequest request,
        CancellationToken cancellationToken);
}
