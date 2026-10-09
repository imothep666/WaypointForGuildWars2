using GuildWarsCompanion.Services.Alerts;

using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// The reminder in the hour before the daily reset. It must come once, only in that hour, and
/// only about what was actually read.
/// </summary>
public class ResetReminderTests
{
    private static readonly DateOnly Day = new(2026, 10, 6);

    [Fact]
    public void Inside_the_hour_it_names_what_is_left()
    {
        var alert = ResetReminder.Describe(TimeSpan.FromMinutes(42), 1, 3, Day);

        Assert.NotNull(alert);
        Assert.Equal("Daily reset in 42 min", alert.Title);
        Assert.Equal("1 Wizard's Vault daily left · 3 time-gated crafts not done.", alert.Body);
    }

    [Fact]
    public void Before_the_hour_there_is_nothing_yet()
    {
        Assert.Null(ResetReminder.Describe(TimeSpan.FromMinutes(61), 3, 3, Day));
        Assert.NotNull(ResetReminder.Describe(TimeSpan.FromMinutes(60), 3, 3, Day));
    }

    [Fact]
    public void All_done_is_no_reminder()
    {
        Assert.Null(ResetReminder.Describe(TimeSpan.FromMinutes(30), 0, 0, Day));
    }

    [Fact]
    public void What_could_not_be_read_is_left_out()
    {
        // A vault still showing yesterday's list comes in as null, not as "5 left".
        Assert.Equal("2 time-gated crafts not done.",
            ResetReminder.Describe(TimeSpan.FromMinutes(30), null, 2, Day)!.Body);
        Assert.Null(ResetReminder.Describe(TimeSpan.FromMinutes(30), null, null, Day));
    }

    [Fact]
    public void One_reminder_a_day_however_many_polls_land_in_the_hour()
    {
        var early = ResetReminder.Describe(TimeSpan.FromMinutes(55), 2, 1, Day)!;
        var late = ResetReminder.Describe(TimeSpan.FromMinutes(25), 1, 1, Day)!;

        // AlertService shows a key once; the same key for the whole hour is what makes it once.
        Assert.Equal(early.Key, late.Key);
        Assert.NotEqual(early.Key, ResetReminder.Describe(TimeSpan.FromMinutes(25), 1, 1, Day.AddDays(1))!.Key);
    }

    [Fact]
    public void Plurals_read_as_words()
    {
        Assert.Equal("3 Wizard's Vault dailies left · 1 time-gated craft not done.",
            ResetReminder.Describe(TimeSpan.FromMinutes(10), 3, 1, Day)!.Body);
    }
}
