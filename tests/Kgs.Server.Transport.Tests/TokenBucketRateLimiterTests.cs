using FluentAssertions;
using Kgs.Server.Transport.RateLimiting;
using Xunit;

namespace Kgs.Server.Transport.Tests;

public sealed class TokenBucketRateLimiterTests
{
    [Fact]
    public void TryConsume_rejects_packets_after_burst_is_exhausted()
    {
        var now = DateTimeOffset.UtcNow;
        var limiter = new TokenBucketRateLimiter(tokensPerSecond: 1, burst: 2);

        limiter.TryConsume(now).Should().BeTrue();
        limiter.TryConsume(now).Should().BeTrue();
        limiter.TryConsume(now).Should().BeFalse();
    }

    [Fact]
    public void TryConsume_refills_tokens_over_time()
    {
        var limiter = new TokenBucketRateLimiter(tokensPerSecond: 1, burst: 1);
        var now = DateTimeOffset.UtcNow.AddSeconds(1);

        limiter.TryConsume(now).Should().BeTrue();
        limiter.TryConsume(now).Should().BeFalse();
        limiter.TryConsume(now.AddSeconds(1)).Should().BeTrue();
    }
}
