using GuildWarsCompanion.Services;
using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// Staying inside the API's rate limit.
///
/// Rate limiting is invisible when it works and baffling when it does not — the failure this
/// exists to prevent was a Today tracker silently disappearing after a catalogue fill had
/// spent the bucket. The clock is injected so none of this waits on real time.
/// </summary>
public class TokenBucketTests
{
    /// <summary>A clock the test drives by hand.</summary>
    private sealed class Clock
    {
        private DateTimeOffset _now = new(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

        public Func<DateTimeOffset> Read => () => _now;

        public void Advance(TimeSpan by) => _now += by;
        public void Rewind(TimeSpan by) => _now -= by;
    }

    [Fact]
    public void The_burst_goes_through_without_waiting()
    {
        var clock = new Clock();
        var bucket = new TokenBucket(capacity: 10, refillPerSecond: 5, clock.Read);

        for (var i = 0; i < 10; i++)
            Assert.Equal(TimeSpan.Zero, bucket.Take());
    }

    [Fact]
    public void Past_the_burst_the_caller_is_asked_to_wait()
    {
        var clock = new Clock();
        var bucket = new TokenBucket(10, 5, clock.Read);

        for (var i = 0; i < 10; i++) bucket.Take();

        // One token behind, refilling at five a second: a fifth of a second.
        Assert.Equal(TimeSpan.FromSeconds(0.2), bucket.Take());
    }

    [Fact]
    public void Each_further_caller_waits_proportionally_longer()
    {
        // This is what spreads a burst out. If everyone were told the same delay they would
        // all wake together and collide with the limiter again.
        var clock = new Clock();
        var bucket = new TokenBucket(1, 5, clock.Read);

        bucket.Take();

        Assert.Equal(TimeSpan.FromSeconds(0.2), bucket.Take());
        Assert.Equal(TimeSpan.FromSeconds(0.4), bucket.Take());
        Assert.Equal(TimeSpan.FromSeconds(0.6), bucket.Take());
    }

    [Fact]
    public void Waiting_refills_the_bucket()
    {
        var clock = new Clock();
        var bucket = new TokenBucket(10, 5, clock.Read);

        for (var i = 0; i < 10; i++) bucket.Take();
        clock.Advance(TimeSpan.FromSeconds(1));

        // Five seconds' worth of tokens back; the next five are free.
        for (var i = 0; i < 5; i++)
            Assert.Equal(TimeSpan.Zero, bucket.Take());
    }

    [Fact]
    public void The_bucket_never_fills_past_its_capacity()
    {
        // An app left idle overnight does not earn a thousand free requests.
        var clock = new Clock();
        var bucket = new TokenBucket(10, 5, clock.Read);

        bucket.Take();
        clock.Advance(TimeSpan.FromHours(8));

        Assert.Equal(10, bucket.Available);
    }

    [Fact]
    public void Partial_seconds_accumulate_rather_than_being_lost()
    {
        // Ten requests a second at five a second would otherwise never refill at all if
        // fractional time were rounded away.
        var clock = new Clock();
        var bucket = new TokenBucket(10, 5, clock.Read);

        for (var i = 0; i < 10; i++) bucket.Take();

        clock.Advance(TimeSpan.FromMilliseconds(100));
        clock.Advance(TimeSpan.FromMilliseconds(100));

        Assert.Equal(TimeSpan.Zero, bucket.Take());
    }

    [Fact]
    public void A_clock_that_jumps_backwards_does_not_take_tokens_away()
    {
        // Device clocks drift and correct themselves. Losing tokens over it would stall the
        // app for as long as the jump.
        var clock = new Clock();
        var bucket = new TokenBucket(10, 5, clock.Read);

        bucket.Take();
        clock.Rewind(TimeSpan.FromMinutes(5));

        Assert.Equal(TimeSpan.Zero, bucket.Take());
        Assert.True(bucket.Available > 0);
    }

    [Fact]
    public void The_guild_wars_bucket_matches_the_published_limit()
    {
        // 300 of burst, refilled at five a second.
        var clock = new Clock();
        var bucket = TokenBucket.ForGuildWars2(clock.Read);

        Assert.Equal(300, bucket.Available);

        for (var i = 0; i < 300; i++)
            Assert.Equal(TimeSpan.Zero, bucket.Take());

        Assert.Equal(TimeSpan.FromSeconds(0.2), bucket.Take());
    }

    [Fact]
    public void A_full_catalogue_fill_stays_inside_the_limit()
    {
        // 371 item batches plus 67 recipe batches, all queued at once. The first 300 go
        // straight through; the rest are spaced at the refill rate.
        //
        // Each delay is measured from the same instant, so the LAST caller's delay is how
        // long the whole run takes — summing them would count the same waiting many times
        // over and is what an earlier version of this test got wrong.
        var clock = new Clock();
        var bucket = TokenBucket.ForGuildWars2(clock.Read);

        var last = TimeSpan.Zero;
        for (var i = 0; i < 438; i++) last = bucket.Take();

        Assert.True(last > TimeSpan.Zero, "the tail of a fill must be paced");

        // 138 past the burst at five a second is a shade under half a minute.
        Assert.Equal(138 / 5.0, last.TotalSeconds, precision: 3);
        Assert.True(last < TimeSpan.FromMinutes(1), $"paced too hard: {last}");
    }
}
