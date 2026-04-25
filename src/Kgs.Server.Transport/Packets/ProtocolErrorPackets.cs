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
}
