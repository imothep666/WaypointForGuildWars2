using GuildWarsCompanion.Services;
using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// When the game's clocks turn over.
///
/// These decide whether the app tells you your Wizard's Vault objectives are last cycle's, so
/// an hour of error here either hides a real warning or cries wolf every Monday morning.
/// </summary>
public class ResetsTests
{
    private static DateTimeOffset Utc(int year, int month, int day, int hour = 0, int minute = 0) =>
        new(year, month, day, hour, minute, 0, TimeSpan.Zero);

    // --- daily -------------------------------------------------------------

    [Fact]
    public void The_daily_reset_is_midnight_utc()
    {
        Assert.Equal(Utc(2026, 8, 24), Resets.LastDaily(Utc(2026, 8, 24, 13, 45)));
        Assert.Equal(Utc(2026, 8, 25), Resets.NextDaily(Utc(2026, 8, 24, 13, 45)));
    }

    [Fact]
    public void A_moment_after_midnight_belongs_to_the_new_day()
    {
        Assert.Equal(Utc(2026, 8, 24), Resets.LastDaily(Utc(2026, 8, 24, 0, 1)));
    }

    [Fact]
    public void Midnight_exactly_counts_as_the_new_day()
    {
        // The boundary is the reset, not the instant after it.
        Assert.Equal(Utc(2026, 8, 24), Resets.LastDaily(Utc(2026, 8, 24)));
    }

    [Fact]
    public void A_local_time_is_judged_by_its_utc_instant()
    {
        // The phone is in Germany; the game's clock is not. 01:30 CEST on the 24th is 23:30
        // UTC on the 23rd, and so still belongs to the previous day.
        var berlinJustBeforeReset = new DateTimeOffset(2026, 8, 24, 1, 30, 0, TimeSpan.FromHours(2));

        Assert.Equal(Utc(2026, 8, 23), Resets.LastDaily(berlinJustBeforeReset));
    }

    // --- weekly ------------------------------------------------------------

    [Fact]
    public void The_weekly_reset_is_monday_half_past_seven_utc()
    {
        // Wednesday looks back to Monday.
        Assert.Equal(Utc(2026, 8, 24, 7, 30), Resets.LastWeekly(Utc(2026, 8, 26, 12, 0)));
    }

    [Fact]
    public void Monday_before_half_past_seven_is_still_last_week()
    {
        // The trap: it is Monday, but the reset has not happened yet, so the current cycle
        // began a week ago. Taking "the most recent Monday" alone would date it to today and
        // make genuinely stale data look current for seven and a half hours every week.
        Assert.Equal(Utc(2026, 8, 17, 7, 30), Resets.LastWeekly(Utc(2026, 8, 24, 7, 29)));
    }

    [Fact]
    public void Monday_at_exactly_half_past_seven_has_reset()
    {
        Assert.Equal(Utc(2026, 8, 24, 7, 30), Resets.LastWeekly(Utc(2026, 8, 24, 7, 30)));
    }

    [Fact]
    public void Sunday_night_looks_back_almost_a_full_week()
    {
        Assert.Equal(Utc(2026, 8, 17, 7, 30), Resets.LastWeekly(Utc(2026, 8, 23, 23, 59)));
    }

    [Fact]
    public void The_next_weekly_is_always_a_week_after_the_last()
    {
        foreach (var hour in Enumerable.Range(0, 24))
        {
            var now = Utc(2026, 8, 24, hour);

            Assert.Equal(TimeSpan.FromDays(7), Resets.NextWeekly(now) - Resets.LastWeekly(now));
            Assert.True(Resets.LastWeekly(now) <= now, $"last weekly is in the future at {hour}:00");
            Assert.True(Resets.NextWeekly(now) > now, $"next weekly is in the past at {hour}:00");
        }
    }

    [Fact]
    public void Every_hour_of_a_week_lands_inside_its_own_cycle()
    {
        // Walks a whole week hour by hour, over a month boundary, checking the invariant that
        // makes the staleness test meaningful at all.
        var start = Utc(2026, 8, 28);

        for (var hour = 0; hour < 24 * 9; hour++)
        {
            var now = start.AddHours(hour);

            Assert.True(Resets.LastDaily(now) <= now);
            Assert.True(Resets.NextDaily(now) > now);
            Assert.Equal(DayOfWeek.Monday, Resets.LastWeekly(now).UtcDateTime.DayOfWeek);
        }
    }

    // --- which clock a track answers to ------------------------------------

    [Fact]
    public void Each_vault_track_answers_to_its_own_clock()
    {
        var now = Utc(2026, 8, 26, 12, 0);

        Assert.Equal(Resets.LastDaily(now), Resets.LastFor("daily", now));
        Assert.Equal(Resets.LastWeekly(now), Resets.LastFor("weekly", now));
    }

    [Fact]
    public void The_special_track_has_no_cycle_to_be_stale_against()
    {
        // It runs the length of a season, so "last cycle's objectives" means nothing for it
        // and the app must never warn about it.
        Assert.Null(Resets.LastFor("special", Utc(2026, 8, 26, 12, 0)));
    }
}
