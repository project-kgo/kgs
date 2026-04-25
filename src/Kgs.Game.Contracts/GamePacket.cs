namespace Kgs.Game.Contracts;

public sealed record GamePacket(
    ushort OpCode,
    uint RequestId,
    ushort Flags,
    ReadOnlyMemory<byte> Payload);
