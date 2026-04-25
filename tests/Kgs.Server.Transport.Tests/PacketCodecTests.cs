using FluentAssertions;
using Kgs.Game.Contracts;
using Kgs.Server.Transport.Packets;
using Xunit;

namespace Kgs.Server.Transport.Tests;

public sealed class PacketCodecTests
{
    [Fact]
    public void Encode_and_decode_round_trips_big_endian_packet_header_and_payload()
    {
        var packet = new GamePacket(1000, 42, 7, "payload"u8.ToArray());

        var encoded = PacketCodec.Encode(packet, maxPayloadLength: 1024);
        var decoded = PacketCodec.Decode(encoded, maxPayloadLength: 1024);

        encoded[0].Should().Be(0x03);
        encoded[1].Should().Be(0xE8);
        decoded.Succeeded.Should().BeTrue();
        decoded.Packet.Should().NotBeNull();
        decoded.Packet!.OpCode.Should().Be(packet.OpCode);
        decoded.Packet.RequestId.Should().Be(packet.RequestId);
        decoded.Packet.Flags.Should().Be(packet.Flags);
        decoded.Packet.Payload.ToArray().Should().Equal(packet.Payload.ToArray());
    }

    [Fact]
    public void Decode_rejects_incomplete_header()
    {
        var decoded = PacketCodec.Decode(new byte[PacketCodec.HeaderSize - 1], maxPayloadLength: 1024);

        decoded.Succeeded.Should().BeFalse();
        decoded.Error.Should().Be(PacketDecodeError.IncompleteHeader);
    }

    [Fact]
    public void Decode_rejects_mismatched_payload_length()
    {
        var encoded = PacketCodec.Encode(
            new GamePacket(1000, 1, 0, "abc"u8.ToArray()),
            maxPayloadLength: 1024);
        encoded[^1] = 0;

        var decoded = PacketCodec.Decode(encoded[..^1], maxPayloadLength: 1024);

        decoded.Succeeded.Should().BeFalse();
        decoded.Error.Should().Be(PacketDecodeError.InvalidLength);
    }

    [Fact]
    public void Decode_rejects_payload_larger_than_configured_limit()
    {
        var encoded = PacketCodec.Encode(
            new GamePacket(1000, 1, 0, "abc"u8.ToArray()),
            maxPayloadLength: 1024);

        var decoded = PacketCodec.Decode(encoded, maxPayloadLength: 2);

        decoded.Succeeded.Should().BeFalse();
        decoded.Error.Should().Be(PacketDecodeError.PayloadTooLarge);
    }
}
