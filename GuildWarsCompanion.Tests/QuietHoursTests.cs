using GuildWarsCompanion.Services;
using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// When notifications are allowed to arrive.
///
/// Both ways of getting this wrong are silent. Allow too much and the phone goes off at three
/// in the morning; allow too little and the app simply stops telling you things, with nothing
/// on screen to say why. The second is worse, so every ambiguous case here resolves towards
/// letting a notification through.
/// </summary>
public class QuietHoursTests
{
    private static DateTimeOffset At(int hour, int minute = 0, DayOfWeek day = DayOfWeek.Wednesday)
    {
        // 26 Aug 2026 is a Wednesday; the offsets below land on the day asked for.
        var wednesday = new DateTimeOffset(2026, 8, 26, hour, minute, 0, TimeSpan.Zero);
        var shift = ((int)day - (int)DayOfWeek.Wednesday + 7) % 7;

        return wednesday.AddDays(shift);
    }

    private static NotifyWindow Evening(bool weekends = false) =>
        new(true, new TimeOnly(17, 0), new TimeOnly(22, 30), weekends);

    [Fact]
    public void Inside_the_window_a_notification_goes_through()
    {
        Assert.True(QuietHours.Allows(Evening(), At(17, 0)));
        Assert.True(QuietHours.Allows(Evening(), At(20, 15)));
    }

    [Fact]
    public void The_end_of_the_window_closes_it()
    {
        // Half past ten means half past ten, not one more notification at 22:30.
        Assert.False(QuietHours.Allows(Evening(), At(22, 30)));
    }

    [Fact]
    public void The_working_day_and_the_night_are_both_outside_it()
    {
        // The whole point of the setting, in the two places it was asked for.
        Assert.False(QuietHours.Allows(Evening(), At(11, 0)));
        Assert.False(QuietHours.Allows(Evening(), At(3, 0)));
    }

    [Fact]
    public void A_window_that_crosses_midnight_is_one_window()
    {
        // 20:00 to 02:00 is the normal shape for an evening player, and reads backwards to a
        // naive comparison — which would allow exactly the hours it is meant to silence.
        var late = new NotifyWindow(true, new TimeOnly(20, 0), new TimeOnly(2, 0), false);

        Assert.True(QuietHours.Allows(late, At(21, 0)));
        Assert.True(QuietHours.Allows(late, At(1, 30)));
        Assert.False(QuietHours.Allows(late, At(3, 0)));
        Assert.False(QuietHours.Allows(late, At(12, 0)));
    }

    [Fact]
    public void Two_identical_times_mean_no_restriction_rather_than_no_notifications()
    {
        // Easy to set by accident with two pickers. Reading it as a zero-length window would
        // silence the app for good, with nothing on screen to explain it.
        var zero = new NotifyWindow(true, new TimeOnly(9, 0), new TimeOnly(9, 0), false);

        Assert.True(QuietHours.Allows(zero, At(3, 0)));
        Assert.Equal("Notifications arrive at any hour.", QuietHours.Describe(zero));
    }

    [Fact]
    public void Turning_the_window_off_allows_every_hour()
    {
        var off = Evening() with { Enabled = false };

        Assert.True(QuietHours.Allows(off, At(3, 0)));
    }

    [Fact]
    public void Weekends_can_ignore_the_window_entirely()
    {
        Assert.True(QuietHours.Allows(Evening(weekends: true), At(10, 0, DayOfWeek.Saturday)));
        Assert.True(QuietHours.Allows(Evening(weekends: true), At(10, 0, DayOfWeek.Sunday)));

        // Friday is still a working day.
        Assert.False(QuietHours.Allows(Evening(weekends: true), At(10, 0, DayOfWeek.Friday)));
    }

    [Fact]
    public void Without_the_weekend_exception_a_saturday_is_like_any_other_day()
    {
        Assert.False(QuietHours.Allows(Evening(), At(10, 0, DayOfWeek.Saturday)));
        Assert.True(QuietHours.Allows(Evening(), At(19, 0, DayOfWeek.Saturday)));
    }

    [Fact]
    public void The_setting_reads_back_as_a_sentence()
    {
        // Four controls do not say what they add up to.
        Assert.Equal("Only between 17:00 and 22:30.", QuietHours.Describe(Evening()));

        Assert.Equal("Only between 17:00 and 22:30, and all day at weekends.",
            QuietHours.Describe(Evening(weekends: true)));

        Assert.Equal("Notifications arrive at any hour.",
            QuietHours.Describe(Evening() with { Enabled = false }));
    }

    [Fact]
    public void The_default_is_no_restriction_at_all()
    {
        // An app that silently withholds notifications is worse than one that sends them at a
        // bad hour, because the second is at least visible. So this is opt-in.
        Assert.False(NotifyWindow.Always.Enabled);
        Assert.True(QuietHours.Allows(NotifyWindow.Always, At(3, 0)));
    }
}
