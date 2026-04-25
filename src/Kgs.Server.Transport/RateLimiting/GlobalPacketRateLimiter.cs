using Kgs.Server.Transport.Configuration;
using Microsoft.Extensions.Options;

namespace Kgs.Server.Transport.RateLimiting;

public sealed class GlobalPacketRateLimiter
{
    private readonly TokenBucketRateLimiter _limiter;

    public GlobalPacketRateLimiter(IOptions<SessionOptions> options)
    {
        _limiter = new TokenBucketRateLimiter(
            options.Value.GlobalPacketsPerSecond,
            options.Value.GlobalPacketBurst);
    }

    public bool TryConsume()
    {
        return _limiter.TryConsume(DateTimeOffset.UtcNow);
    }
}
