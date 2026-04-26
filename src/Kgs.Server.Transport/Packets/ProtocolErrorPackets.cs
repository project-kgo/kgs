using System.Text;
using Kgs.Game.Contracts;

namespace Kgs.Server.Transport.Packets;

public static class ProtocolErrorPackets
{
    public static GamePacket Create(ProtocolErrorCode errorCode, uint requestId, string message)
    {
        var payload = Encoding.UTF8.GetBytes($"{(ushort)errorCode}:{message}");
        return new GamePacket(SystemOpCodes.Error, requestId, (ushort)errorCode, payload);
    }

    public static GamePacket Create(
        ProtocolErrorCode errorCode,
        uint requestId,
        string message,
        int maxPayloadLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxPayloadLength);

        var prefix = $"{(ushort)errorCode}:";
        var prefixBytes = Encoding.UTF8.GetBytes(prefix);
        if (maxPayloadLength is 0)
        {
            return new GamePacket(SystemOpCodes.Error, requestId, (ushort)errorCode, ReadOnlyMemory<byte>.Empty);
        }

        if (prefixBytes.Length >= maxPayloadLength)
        {
            return new GamePacket(SystemOpCodes.Error, requestId, (ushort)errorCode, prefixBytes.AsMemory(0, maxPayloadLength).ToArray());
        }

        var maxMessageBytes = maxPayloadLength - prefixBytes.Length;
        var messageByteCount = GetTruncatedUtf8ByteCount(message, maxMessageBytes);
        var payload = new byte[prefixBytes.Length + messageByteCount];
        prefixBytes.CopyTo(payload, 0);
        Encoding.UTF8.GetBytes(message.AsSpan(0, GetUtf16LengthForUtf8ByteCount(message, messageByteCount)), payload.AsSpan(prefixBytes.Length));

        return new GamePacket(SystemOpCodes.Error, requestId, (ushort)errorCode, payload);
    }

    private static int GetTruncatedUtf8ByteCount(string value, int maxByteCount)
    {
        var byteCount = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            var runeByteCount = rune.Utf8SequenceLength;
            if (byteCount > maxByteCount - runeByteCount)
            {
                break;
            }

            byteCount += runeByteCount;
        }

        return byteCount;
    }

    private static int GetUtf16LengthForUtf8ByteCount(string value, int byteCount)
    {
        var consumedBytes = 0;
        var utf16Length = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            var runeByteCount = rune.Utf8SequenceLength;
            if (consumedBytes + runeByteCount > byteCount)
            {
                break;
            }

            consumedBytes += runeByteCount;
            utf16Length += rune.Utf16SequenceLength;
        }

        return utf16Length;
    }
}
