namespace Kgs.Server.Transport.Packets;

public enum PacketDecodeError
{
    None = 0,
    IncompleteHeader = 1,
    InvalidLength = 2,
    PayloadTooLarge = 3
}
