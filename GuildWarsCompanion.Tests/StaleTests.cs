using GuildWarsCompanion.Services;
using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// How old offline data says it is.
///
/// This wording is the entire difference between honest offline support and a screen quietly
/// showing yesterday's gold as though it were current, so it is worth pinning exactly.
/// </summary>
public class StaleTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 21, 18, 0, 0, TimeSpan.Zero);

    private static string AgeAfter(TimeSpan elapsed) => Stale.Age(Now - elapsed, Now);

    [Fact]
    public void Something_just_taken_does_not_claim_an_age() =>
        Assert.Equal("just now", AgeAfter(TimeSpan.FromSeconds(20)));

    [Fact]
    public void A_clock_running_backwards_still_reads_sensibly()
    {
        // Device clocks drift and the phone may correct itself between the write and the
        // read. A negative age must not print "-3 minutes ago".
        Assert.Equal("just now", Stale.Age(Now.AddMinutes(5), Now));
    }

    [Theory]
    [InlineData(3, "3 minutes ago")]
    [InlineData(59, "59 minutes ago")]
    public void Minutes_are_exact_while_they_matter(int minutes, string expected) =>
        Assert.Equal(expected, AgeAfter(TimeSpan.FromMinutes(minutes)));

    [Fact]
    public void The_first_hour_is_named_rather_than_counted()
    {
        // "1 hours ago" is the kind of detail that makes an app feel unfinished.
        Assert.Equal("an hour ago", AgeAfter(TimeSpan.FromMinutes(60)));
        Assert.Equal("an hour ago", AgeAfter(TimeSpan.FromMinutes(119)));
    }

    [Theory]
    [InlineData(2, "2 hours ago")]
    [InlineData(23, "23 hours ago")]
    public void Then_hours(int hours, string expected) =>
        Assert.Equal(expected, AgeAfter(TimeSpan.FromHours(hours)));

    [Fact]
    public void A_day_old_snapshot_says_yesterday() =>
        Assert.Equal("yesterday", AgeAfter(TimeSpan.FromHours(30)));

    [Theory]
    [InlineData(2, "2 days ago")]
    [InlineData(40, "40 days ago")]
    public void And_then_days(int days, string expected) =>
        Assert.Equal(expected, AgeAfter(TimeSpan.FromDays(days)));

    [Fact]
    public void Every_age_produces_a_readable_line()
    {
        // Whatever the gap, the sentence has to be one a player can act on: it names the
        // cause, the data, and the age.
        foreach (var elapsed in new[]
                 {
                     TimeSpan.Zero, TimeSpan.FromMinutes(5), TimeSpan.FromHours(3),
                     TimeSpan.FromDays(1), TimeSpan.FromDays(400)
                 })
        {
            var line = Stale.Describe("your wallet", Now - elapsed, Now);

            Assert.StartsWith("No connection — showing your wallet from ", line);
            Assert.EndsWith(".", line);
            Assert.DoesNotContain("-", line[..^1]);
        }
    }

    [Fact]
    public void The_subject_is_the_caller_s_to_name() =>
        Assert.Equal(
            "No connection — showing your characters from 2 hours ago.",
            Stale.Describe("your characters", Now.AddHours(-2), Now));
}
