namespace Kgs.Server.Transport.Configuration;

public sealed class SessionOptions
{
    public int MaxPayloadLength { get; set; } = 64 * 1024;

    public TimeSpan AuthTimeout { get; set; } = TimeSpan.FromSeconds(10);

    public TimeSpan HeartbeatTimeout { get; set; } = TimeSpan.FromSeconds(45);

    public int SendQueueCapacity { get; set; } = 1024;

    public double SessionPacketsPerSecond { get; set; } = 20;

    public int SessionPacketBurst { get; set; } = 40;

    public double GlobalPacketsPerSecond { get; set; } = 5000;

    public int GlobalPacketBurst { get; set; } = 10000;
}
