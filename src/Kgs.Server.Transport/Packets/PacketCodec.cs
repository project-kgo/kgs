using System.Buffers.Binary;
using Kgs.Game.Contracts;

namespace Kgs.Server.Transport.Packets;

public static class PacketCodec
{
    public const int HeaderSize = 12;

    public static PacketDecodeResult Decode(ReadOnlyMemory<byte> frame, int maxPayloadLength)
    {
        if (frame.Length < HeaderSize)
        {
            return PacketDecodeResult.Fail(PacketDecodeError.IncompleteHeader, "Packet header is incomplete.");
        }

        var span = frame.Span;
        var opCode = BinaryPrimitives.ReadUInt16BigEndian(span[..2]);
        var requestId = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(2, 4));
        var flags = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(6, 2));
        var payloadLength = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(8, 4));

        if (payloadLength > maxPayloadLength)
        {
            return PacketDecodeResult.Fail(PacketDecodeError.PayloadTooLarge, "Packet payload is too large.");
        }

        var expectedLength = HeaderSize + checked((int)payloadLength);
        if (frame.Length != expectedLength)
        {
            return PacketDecodeResult.Fail(PacketDecodeError.InvalidLength, "Packet length does not match payload length.");
        }

        return PacketDecodeResult.Success(new GamePacket(
            opCode,
            requestId,
            flags,
            frame.Slice(HeaderSize, (int)payloadLength).ToArray()));
    }

    public static byte[] Encode(GamePacket packet, int maxPayloadLength)
    {
        if (packet.Payload.Length > maxPayloadLength)
        {
            throw new ArgumentOutOfRangeException(nameof(packet), "Packet payload is too large.");
        }

        var buffer = new byte[HeaderSize + packet.Payload.Length];
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(0, 2), packet.OpCode);
        BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(2, 4), packet.RequestId);
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(6, 2), packet.Flags);
        BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(8, 4), (uint)packet.Payload.Length);
        packet.Payload.CopyTo(buffer.AsMemory(HeaderSize));
        return buffer;
    }
}
