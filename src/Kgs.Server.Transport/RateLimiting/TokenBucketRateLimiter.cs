namespace Kgs.Server.Transport.RateLimiting;

public sealed class TokenBucketRateLimiter
{
    private readonly object _syncRoot = new();
    private readonly double _tokensPerSecond;
    private readonly double _burst;
    private double _tokens;
    private DateTimeOffset _lastRefill;

    public TokenBucketRateLimiter(double tokensPerSecond, int burst)
    {
        if (tokensPerSecond <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tokensPerSecond));
        }

        if (burst <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(burst));
        }

        _tokensPerSecond = tokensPerSecond;
        _burst = burst;
        _tokens = burst;
        _lastRefill = DateTimeOffset.UtcNow;
    }

    public bool TryConsume(DateTimeOffset now, int tokens = 1)
    {
        lock (_syncRoot)
        {
            Refill(now);

            if (_tokens < tokens)
            {
                return false;
            }

            _tokens -= tokens;
            return true;
        }
    }

    private void Refill(DateTimeOffset now)
    {
        if (now <= _lastRefill)
        {
            return;
        }

        var elapsedSeconds = (now - _lastRefill).TotalSeconds;
        _tokens = Math.Min(_burst, _tokens + elapsedSeconds * _tokensPerSecond);
        _lastRefill = now;
    }
}
