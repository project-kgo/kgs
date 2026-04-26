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
        var buffer = new byte[GetEncodedLength(packet, maxPayloadLength)];
        EncodeTo(packet, buffer, maxPayloadLength);
        return buffer;
    }

    public static int GetEncodedLength(GamePacket packet, int maxPayloadLength)
    {
        if (packet.Payload.Length > maxPayloadLength)
        {
            throw new ArgumentOutOfRangeException(nameof(packet), "Packet payload is too large.");
        }

        return HeaderSize + packet.Payload.Length;
    }

    public static int EncodeTo(GamePacket packet, Span<byte> destination, int maxPayloadLength)
    {
        var encodedLength = GetEncodedLength(packet, maxPayloadLength);
        if (destination.Length < encodedLength)
        {
            throw new ArgumentException("Destination buffer is too small.", nameof(destination));
        }

        BinaryPrimitives.WriteUInt16BigEndian(destination[..2], packet.OpCode);
        BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(2, 4), packet.RequestId);
        BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(6, 2), packet.Flags);
        BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(8, 4), (uint)packet.Payload.Length);
        packet.Payload.Span.CopyTo(destination[HeaderSize..encodedLength]);
        
        return encodedLength;
    }
}
