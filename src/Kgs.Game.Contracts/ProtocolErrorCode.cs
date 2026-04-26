namespace Kgs.Game.Contracts;

public enum ProtocolErrorCode : ushort
{
    InvalidPacket = 1,
    Unauthorized = 2,
    RateLimited = 3,
    ServerBusy = 4,
    HeartbeatTimeout = 5,
    DispatchFailed = 6,
    PayloadTooLarge = 7
}
