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
    public void EncodeTo_writes_the_same_bytes_as_Encode()
    {
        var packet = new GamePacket(1000, 42, 7, "payload"u8.ToArray());
        var expected = PacketCodec.Encode(packet, maxPayloadLength: 1024);
        var destination = new byte[expected.Length];

        var written = PacketCodec.EncodeTo(packet, destination, maxPayloadLength: 1024);

        written.Should().Be(expected.Length);
        destination.Should().Equal(expected);
        PacketCodec.GetEncodedLength(packet, maxPayloadLength: 1024).Should().Be(expected.Length);
    }

    [Fact]
    public void EncodeTo_rejects_destination_buffers_that_are_too_small()
    {
        var packet = new GamePacket(1000, 42, 7, "payload"u8.ToArray());
        var destination = new byte[PacketCodec.GetEncodedLength(packet, maxPayloadLength: 1024) - 1];

        var act = () => PacketCodec.EncodeTo(packet, destination, maxPayloadLength: 1024);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("destination");
    }

    [Fact]
    public void GetEncodedLength_rejects_payloads_larger_than_configured_limit()
    {
        var packet = new GamePacket(1000, 42, 7, "payload"u8.ToArray());

        var act = () => PacketCodec.GetEncodedLength(packet, maxPayloadLength: 2);

        act.Should().Throw<ArgumentOutOfRangeException>();
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

    [Fact]
    public void Protocol_error_packets_can_be_limited_to_the_configured_payload_length()
    {
        var packet = ProtocolErrorPackets.Create(
            ProtocolErrorCode.PayloadTooLarge,
            requestId: 9,
            "这个错误消息会被截断",
            maxPayloadLength: 8);

        packet.OpCode.Should().Be(SystemOpCodes.Error);
        packet.RequestId.Should().Be(9);
        packet.Flags.Should().Be((ushort)ProtocolErrorCode.PayloadTooLarge);
        packet.Payload.Length.Should().BeLessThanOrEqualTo(8);

        var encoded = PacketCodec.Encode(packet, maxPayloadLength: 8);
        var decoded = PacketCodec.Decode(encoded, maxPayloadLength: 8);
        decoded.Succeeded.Should().BeTrue(decoded.ErrorMessage);
    }
}
