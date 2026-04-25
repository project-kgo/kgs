using Kgs.Game.Contracts;

public sealed class NoOpPacketDispatcher : IPacketDispatcher
{
    public ValueTask<PacketDispatchResult> DispatchAsync(
        PacketDispatchRequest request,
        CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(PacketDispatchResult.Empty);
    }
}
