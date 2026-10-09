using GuildWarsCompanion.Services;
using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// The two home screen widgets.
///
/// A widget is the one surface in the app nobody watches being built — it is drawn by the
/// launcher, from a process that may be cold, hours after anyone last opened the app. So the
/// two ways it can quietly lie are the ones worth pinning: showing yesterday's completions as
/// today's, and showing an old reading as if it were current.
/// </summary>
public class WidgetContentTests
{
    private static readonly DateTimeOffset Noon =
        new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

    private static (string, string, string, int, DateTimeOffset) Boss(
        string name, string id, int hour, int minute) =>
        (name, id, "Somewhere", 80, new DateTimeOffset(2026, 9, 4, hour, minute, 0, TimeSpan.Zero));

    private static readonly (string, string, string, int, DateTimeOffset)[] Three =
    [
        Boss("Shadow Behemoth", "shadow_behemoth", 12, 45),
        Boss("Tequatl the Sunless", "tequatl_the_sunless", 16, 0),
        Boss("The Shatterer", "the_shatterer", 19, 0)
    ];

    private static IReadOnlySet<string> Killed(params string[] ids) => new HashSet<string>(ids);

    // --- world bosses -------------------------------------------------------

    [Fact]
    public void The_next_boss_leads_and_the_two_after_it_follow()
    {
        var panel = WidgetContent.Bosses(Three, null, null, Noon);

        Assert.Equal("Shadow Behemoth", panel.Next!.Name);
        Assert.Equal(["Tequatl the Sunless", "The Shatterer"], panel.Later.Select(l => l.Name));
    }

    [Fact]
    public void A_boss_already_killed_today_is_skipped()
    {
        var panel = WidgetContent.Bosses(Three, Killed("shadow_behemoth"), Noon, Noon);

        Assert.Equal("Tequatl the Sunless", panel.Next!.Name);
        Assert.Equal("2 of 3 timed bosses left", panel.Footnote);
    }

    [Fact]
    public void Completions_read_before_the_last_reset_are_not_trusted()
    {
        // Dailies turn over at 00:00 UTC and the endpoint empties with them. A set read
        // yesterday evening still lists yesterday's kills, and believing it would hide a boss
        // the player has not killed today — the widget would simply skip Shadow Behemoth.
        var yesterday = Noon.AddHours(-20);

        var panel = WidgetContent.Bosses(Three, Killed("shadow_behemoth"), yesterday, Noon);

        Assert.Equal("Shadow Behemoth", panel.Next!.Name);
        Assert.Null(panel.Next.Needed);
        Assert.Equal(string.Empty, panel.Footnote);
    }

    [Fact]
    public void Completions_read_after_the_last_reset_are_trusted()
    {
        // The boundary the previous test sits the other side of.
        var thisMorning = Noon.AddHours(-6);

        var panel = WidgetContent.Bosses(Three, Killed("shadow_behemoth"), thisMorning, Noon);

        Assert.Equal("Tequatl the Sunless", panel.Next!.Name);
    }

    [Fact]
    public void Nothing_known_marks_nothing_rather_than_marking_it_undone()
    {
        var panel = WidgetContent.Bosses(Three, null, null, Noon);

        Assert.All(panel.Later.Prepend(panel.Next!), line => Assert.Null(line.Needed));
        Assert.Equal(string.Empty, panel.Footnote);
    }

    [Fact]
    public void An_account_that_has_killed_everything_still_gets_a_timetable()
    {
        // The day is not over, only the checklist. An empty widget would read as broken.
        var panel = WidgetContent.Bosses(
            Three, Killed("shadow_behemoth", "tequatl_the_sunless", "the_shatterer"), Noon, Noon);

        Assert.Equal("Shadow Behemoth", panel.Next!.Name);
        Assert.False(panel.Next.Needed);
        Assert.Equal("0 of 3 timed bosses left", panel.Footnote);
    }

    // --- Wizard's Vault -----------------------------------------------------

    [Fact]
    public void The_vault_counts_the_meta_and_what_is_waiting()
    {
        var panel = WidgetContent.Vault(
            new TrackCounts(2, 4, 1, false), new TrackCounts(6, 8, 0, true), Noon, Noon);

        Assert.Equal("2 / 4", panel.Daily);
        Assert.Equal("6 / 8", panel.Weekly);

        // One finished daily objective, plus the weekly meta reward itself.
        Assert.Equal("2 to claim", panel.Claim);
        Assert.True(panel.HasClaim);
    }

    [Fact]
    public void Nothing_waiting_says_nothing()
    {
        var panel = WidgetContent.Vault(
            new TrackCounts(4, 4, 0, false), new TrackCounts(0, 8, 0, false), Noon, Noon);

        Assert.Equal(string.Empty, panel.Claim);
        Assert.False(panel.HasClaim);
    }

    [Fact]
    public void A_track_never_read_is_a_dash_not_a_zero()
    {
        var panel = WidgetContent.Vault(null, null, null, Noon);

        Assert.Equal("—", panel.Daily);
        Assert.Equal("—", panel.Weekly);
        Assert.Null(panel.Age);
    }

    [Fact]
    public void Age_is_printed_only_once_it_is_worth_printing()
    {
        var fresh = WidgetContent.Vault(null, null, Noon.AddMinutes(-20), Noon);
        var old = WidgetContent.Vault(null, null, Noon.AddHours(-5), Noon);

        // A widget refreshed twenty minutes ago saying so is noise; one showing this morning's
        // dailies in silence is not.
        Assert.Null(fresh.Age);
        Assert.Equal("5 hours ago", old.Age);
    }

    [Fact]
    public void The_reset_countdown_reads_as_time_left()
    {
        Assert.Equal("Resets in 12h 0m", WidgetContent.Vault(null, null, null, Noon).Reset);
    }

    [Fact]
    public void The_countdown_drops_the_hours_once_there_are_none()
    {
        Assert.Equal("11h 42m", WidgetContent.Until(TimeSpan.FromMinutes(702)));
        Assert.Equal("42m", WidgetContent.Until(TimeSpan.FromMinutes(42)));
        Assert.Equal("now", WidgetContent.Until(TimeSpan.Zero));
        Assert.Equal("now", WidgetContent.Until(TimeSpan.FromMinutes(-5)));
    }
}
