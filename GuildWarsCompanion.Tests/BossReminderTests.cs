using GuildWarsCompanion.Bridge.Protocol;

using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// Which world boss the PC interrupts somebody for.
///
/// The phone's rule lives in <see cref="GuildWarsCompanion.Services.BossAlerts"/> and is
/// deliberately different: it knows what the account has killed today and this does not, because
/// the bridge has no API key. What is pinned here is the narrower promise — the timetable, a ten
/// minute lead, one at a time, once each.
/// </summary>
public class BossReminderTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);

    private static (string, string, string, int, DateTimeOffset) Boss(
        string name, int minutes) =>
        (name, name.ToLowerInvariant(), "Queensdale", 15, Now.AddMinutes(minutes));

    private static readonly HashSet<string> Nothing = [];

    [Fact]
    public void A_boss_inside_the_lead_is_mentioned()
    {
        var due = BossReminder.Next([Boss("Shadow Behemoth", 8)], Nothing, Now);

        Assert.NotNull(due);
        Assert.Equal("Shadow Behemoth in 8 minutes", due.Title);
        Assert.Equal("Queensdale", due.Where);
    }

    [Fact]
    public void A_boss_beyond_the_lead_waits()
    {
        Assert.Null(BossReminder.Next([Boss("Shadow Behemoth", 11)], Nothing, Now));
    }

    /// <summary>Ten minutes exactly is inside; eleven is not. The boundary is pinned because it
    /// is the whole of the rule.</summary>
    [Fact]
    public void The_lead_is_ten_minutes()
    {
        Assert.Equal(TimeSpan.FromMinutes(10), BossReminder.Lead);
        Assert.NotNull(BossReminder.Next([Boss("A", 10)], Nothing, Now));
    }

    /// <summary>A boss that has already started is not news: the phone's alert covers the
    /// "up now" case, and a panel saying it as you arrive is noise.</summary>
    [Fact]
    public void A_boss_already_begun_is_not_mentioned()
    {
        Assert.Null(BossReminder.Next([Boss("Shadow Behemoth", -1)], Nothing, Now));
        Assert.Null(BossReminder.Next([Boss("Shadow Behemoth", 0)], Nothing, Now));
    }

    /// <summary>
    /// One at a time. A notification shade stacks; a panel over a fight does not, and two of them
    /// would be worse than missing one.
    /// </summary>
    [Fact]
    public void Only_the_soonest_is_mentioned()
    {
        var due = BossReminder.Next(
            [Boss("Later", 9), Boss("Sooner", 3)], Nothing, Now);

        Assert.Equal("Sooner in 3 minutes", due!.Title);
    }

    /// <summary>Said once per appearance, so a check every twenty seconds does not repeat
    /// itself for ten minutes.</summary>
    [Fact]
    public void An_appearance_already_said_is_skipped()
    {
        var first = BossReminder.Next([Boss("Shadow Behemoth", 8)], Nothing, Now);

        var said = new HashSet<string> { first!.Key };

        Assert.Null(BossReminder.Next([Boss("Shadow Behemoth", 8)], said, Now));
    }

    /// <summary>The same boss tomorrow is news again — the key is the appearance, not the
    /// boss.</summary>
    [Fact]
    public void The_same_boss_at_a_different_time_is_news_again()
    {
        var today = BossReminder.Next([Boss("Shadow Behemoth", 8)], Nothing, Now);

        var said = new HashSet<string> { today!.Key };

        var tomorrow = BossReminder.Next(
            [("Shadow Behemoth", "shadow_behemoth", "Queensdale", 15, Now.AddDays(1).AddMinutes(8))],
            said, Now.AddDays(1));

        Assert.NotNull(tomorrow);
    }

    /// <summary>
    /// Rounded rather than truncated: a hundred seconds is nearer two minutes than one, and
    /// truncation would call it one for another forty seconds.
    /// </summary>
    [Fact]
    public void The_minutes_are_rounded()
    {
        var up = BossReminder.Next(
            [("A", "a", "Queensdale", 15, Now.AddSeconds(100))], Nothing, Now);

        Assert.Equal("A in 2 minutes", up!.Title);

        var down = BossReminder.Next(
            [("A", "a", "Queensdale", 15, Now.AddSeconds(80))], Nothing, Now);

        Assert.Equal("A in a minute", down!.Title);
    }

    [Fact]
    public void One_minute_is_said_in_words()
    {
        var due = BossReminder.Next(
            [("A", "a", "Queensdale", 15, Now.AddSeconds(50))], Nothing, Now);

        Assert.Equal("A in a minute", due!.Title);
    }

    [Fact]
    public void An_empty_timetable_says_nothing()
    {
        Assert.Null(BossReminder.Next([], Nothing, Now));
    }

    /// <summary>
    /// The real timetable, run through the real rule.
    ///
    /// Bosses appear on fifteen-minute boundaries, so from a quarter past any hour the next one
    /// is always inside a ten minute lead — which is what makes this a useful check that the two
    /// pieces fit together rather than only that each works alone.
    /// </summary>
    [Fact]
    public void The_real_timetable_produces_something_to_say()
    {
        var at = new DateTimeOffset(2026, 9, 18, 12, 6, 0, TimeSpan.Zero);

        var due = BossReminder.Next(
            GuildWarsCompanion.Services.EventSchedule.NextAppearances(at), Nothing, at);

        Assert.NotNull(due);
        Assert.InRange(due.MinutesAway, 1, 10);
    }
}
