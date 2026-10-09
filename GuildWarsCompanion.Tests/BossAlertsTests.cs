using GuildWarsCompanion.Services;
using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// Which world bosses are worth a notification.
///
/// The failure modes here are both bad and both silent: notify about a boss already killed
/// and the alerts become noise to be swiped away, miss one entirely and the feature may as
/// well not exist.
/// </summary>
public class BossAlertsTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

    private static (string, string, string, int, DateTimeOffset) Boss(
        string id, int minutesAway) =>
        ($"The {id}", id, "Somewhere", 80, Now.AddMinutes(minutesAway));

    private static IReadOnlySet<string> Remaining(params string[] ids) => ids.ToHashSet();

    [Fact]
    public void A_boss_you_still_need_inside_the_window_is_announced()
    {
        var due = BossAlerts.Due([Boss("jormag", 12)], Remaining("jormag"), Now);

        Assert.Single(due);
        Assert.Equal(12, due[0].MinutesAway);
    }

    [Fact]
    public void A_boss_already_killed_today_is_not_announced()
    {
        // The whole point of reading the account: silence about what is already done.
        Assert.Empty(BossAlerts.Due([Boss("jormag", 12)], Remaining(), Now));
    }

    [Fact]
    public void A_boss_beyond_the_window_waits_for_a_later_poll() =>
        Assert.Empty(BossAlerts.Due([Boss("jormag", 90)], Remaining("jormag"), Now));

    [Fact]
    public void The_window_covers_the_gap_between_two_polls()
    {
        // The poll runs every 30 minutes. A window shorter than that would drop any
        // appearance landing in the gap, and it would never be mentioned at all.
        Assert.True(BossAlerts.Window >= TimeSpan.FromMinutes(30),
            "a window under the poll interval silently loses appearances");

        Assert.Single(BossAlerts.Due([Boss("jormag", 30)], Remaining("jormag"), Now));
    }

    [Fact]
    public void Something_already_started_is_not_announced_as_upcoming()
    {
        // Strictly in the future: a boss that spawned two minutes ago is not news.
        Assert.Empty(BossAlerts.Due([Boss("jormag", -2)], Remaining("jormag"), Now));
    }

    [Fact]
    public void The_soonest_come_first()
    {
        var due = BossAlerts.Due(
            [Boss("late", 25), Boss("soon", 5)], Remaining("late", "soon"), Now);

        Assert.Equal("soon", due[0].ApiId);
    }

    [Fact]
    public void Only_a_couple_are_raised_at_once()
    {
        // Early in the day nothing is killed yet and half the timetable is inside the window.
        var everything = Enumerable.Range(1, 10).Select(i => Boss($"boss{i}", i)).ToList();
        var remaining = Remaining(everything.Select(b => b.Item2).ToArray());

        Assert.Equal(BossAlerts.MostAtOnce, BossAlerts.Due(everything, remaining, Now).Count);
    }

    [Fact]
    public void An_unscheduled_boss_never_appears()
    {
        // Drakkar and the Mists titans have no timetable, so they are absent from the
        // appearance list and exclude themselves even while "not done today".
        Assert.Empty(BossAlerts.Due([], Remaining("drakkar"), Now));
    }

    // --- one alert per appearance ------------------------------------------

    [Fact]
    public void The_key_is_stable_across_polls_for_the_same_appearance()
    {
        // Two polls twenty minutes apart both see the same spawn; it must be announced once.
        var first = BossAlerts.Due([Boss("jormag", 30)], Remaining("jormag"), Now)[0];
        var second = BossAlerts.Due(
            [("The jormag", "jormag", "Somewhere", 80, Now.AddMinutes(30))],
            Remaining("jormag"), Now.AddMinutes(20))[0];

        Assert.Equal(first.Key, second.Key);
    }

    [Fact]
    public void The_same_boss_tomorrow_is_news_again()
    {
        var today = BossAlerts.Due([Boss("jormag", 10)], Remaining("jormag"), Now)[0];
        var tomorrow = BossAlerts.Due(
            [("The jormag", "jormag", "Somewhere", 80, Now.AddDays(1).AddMinutes(10))],
            Remaining("jormag"), Now.AddDays(1))[0];

        Assert.NotEqual(today.Key, tomorrow.Key);
    }

    // --- wording ------------------------------------------------------------

    [Fact]
    public void Minutes_are_rounded_not_truncated()
    {
        // 110 seconds is 1.83 minutes: rounding says two, truncating says one. Truncating
        // makes every countdown read a minute short, which matters when the number is small.
        var due = BossAlerts.Due(
            [("Claw of Jormag", "jormag", "Frostgorge Sound", 80, Now.AddSeconds(110))],
            Remaining("jormag"), Now);

        Assert.Equal(2, due[0].MinutesAway);
    }

    [Theory]
    [InlineData(12, "Claw of Jormag in 12 minutes")]
    [InlineData(1, "Claw of Jormag in a minute")]
    [InlineData(0, "Claw of Jormag is up now")]
    public void The_title_reads_as_a_sentence(int minutes, string expected)
    {
        var call = new BossCall("Claw of Jormag", "jormag", "Frostgorge Sound", Now, minutes);

        Assert.Equal(expected, BossAlerts.Title(call));
    }

    [Fact]
    public void The_body_says_where_and_why() =>
        Assert.Equal(
            "Frostgorge Sound — you have not done this one today.",
            BossAlerts.Body(new BossCall("Claw of Jormag", "jormag", "Frostgorge Sound", Now, 12)));
}
