using Kgs.Game.Contracts;

namespace Kgs.Server.Transport.Packets;

public sealed record PacketDecodeResult(
    bool Succeeded,
    GamePacket? Packet,
    PacketDecodeError Error,
    string? ErrorMessage)
{
    public static PacketDecodeResult Success(GamePacket packet)
    {
        return new PacketDecodeResult(true, packet, PacketDecodeError.None, null);
    }

    public static PacketDecodeResult Fail(PacketDecodeError error, string errorMessage)
    {
        return new PacketDecodeResult(false, null, error, errorMessage);
    }
}
