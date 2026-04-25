namespace Kgs.Game.Contracts;

public sealed record PacketDispatchRequest(
    GameSessionContext Context,
    GamePacket Packet);
