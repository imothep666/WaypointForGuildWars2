namespace GuildWarsCompanion.Services;

/// <summary>
/// The API's own rate limit, modelled so the app can stay inside it.
///
/// Guild Wars 2 meters per IP with a token bucket: 300 tokens of burst, refilled at 5 per
/// second. Normal use never touches it — the burst covers a dozen screens — but a catalogue
/// fill, or several screens opened in quick succession, will drain it and start earning 429s.
///
/// Rate limiting is usually invisible when it works and baffling when it does not: the failure
/// this replaces was a Today tracker silently vanishing from the screen after a catalogue fill
/// had spent the bucket. So the bucket is a plain object over an injectable clock, and its
/// arithmetic is tested without anything sleeping.
/// </summary>
public sealed class TokenBucket
{
    private readonly int _capacity;
    private readonly double _perSecond;
    private readonly Func<DateTimeOffset> _clock;

    private double _tokens;
    private DateTimeOffset _lastRefill;

    /// <summary>The published limit: 300 of burst, refilled at 5 a second.</summary>
    public static TokenBucket ForGuildWars2(Func<DateTimeOffset>? clock = null) =>
        new(capacity: 300, refillPerSecond: 5, clock);

    public TokenBucket(int capacity, double refillPerSecond, Func<DateTimeOffset>? clock = null)
    {
        _capacity = capacity;
        _perSecond = refillPerSecond;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);

        // Starting full: the app has made no requests yet, and the real bucket refills while
        // nothing is running.
        _tokens = capacity;
        _lastRefill = _clock();
    }

    /// <summary>Tokens available right now, for tests and diagnostics.</summary>
    public double Available
    {
        get
        {
            Refill();
            return _tokens;
        }
    }

    /// <summary>
    /// Takes a token, returning how long the caller must wait before its request is within
    /// the limit. Zero means go now.
    ///
    /// The token is taken either way — the caller is queued, not refused — so callers keep
    /// their order and the bucket cannot be oversubscribed by everyone asking at once.
    /// </summary>
    public TimeSpan Take()
    {
        Refill();

        _tokens -= 1;

        // A negative balance is a queue: each further caller waits proportionally longer,
        // which is what spreads a burst out instead of letting it collide with the limiter.
        return _tokens >= 0
            ? TimeSpan.Zero
            : TimeSpan.FromSeconds(-_tokens / _perSecond);
    }

    private void Refill()
    {
        var now = _clock();
        var elapsed = (now - _lastRefill).TotalSeconds;

        // A clock that jumps backwards must not remove tokens.
        if (elapsed <= 0)
        {
            _lastRefill = now;
            return;
        }

        _lastRefill = now;
        _tokens = Math.Min(_capacity, _tokens + elapsed * _perSecond);
    }
}
