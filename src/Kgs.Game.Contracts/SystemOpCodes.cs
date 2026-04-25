namespace Kgs.Game.Contracts;

public static class SystemOpCodes
{
    public const ushort AuthRequest = 1;
    public const ushort AuthResponse = 2;
    public const ushort Ping = 3;
    public const ushort Pong = 4;
    public const ushort Error = 5;
}
