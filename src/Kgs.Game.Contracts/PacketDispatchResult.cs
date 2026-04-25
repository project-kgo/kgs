namespace Kgs.Game.Contracts;

public sealed record PacketDispatchResult(IReadOnlyList<GamePacket> OutboundPackets)
{
    public static PacketDispatchResult Empty { get; } = new(Array.Empty<GamePacket>());
}
