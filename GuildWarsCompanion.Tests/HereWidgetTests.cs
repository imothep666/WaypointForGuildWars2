using GuildWarsCompanion.Services;

using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// The Here widget: what is left on the map someone is playing, on the home screen.
///
/// The way it can quietly lie is the one worth pinning: an evening-old "map chest waiting" or
/// "Shadow Behemoth in 25 min" shown as if it were about now.
/// </summary>
public class HereWidgetTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 18, 0, 0, TimeSpan.Zero);

    private static readonly HereReading Playing =
        new("Sweet Child O Night", "Crystal Oasis", "Here: map chest waiting · Casino Blitz in 25 min · Shadow Behemoth in 1h 05m · Tequatl the Sunless in 1h 50m");

    [Fact]
    public void A_fresh_reading_says_what_is_left_one_fact_a_line()
    {
        var panel = HereWidget.Panel(Playing, Now.AddMinutes(-3), Now);

        Assert.True(panel.IsLive);
        Assert.Equal("Crystal Oasis", panel.Map);
        Assert.Equal("Sweet Child O Night", panel.Who);
        Assert.Equal(["Map chest waiting", "Casino Blitz in 25 min", "Shadow Behemoth in 1h 05m"], panel.Lines);
    }

    [Fact]
    public void An_old_reading_says_where_they_were_and_nothing_about_now()
    {
        var panel = HereWidget.Panel(Playing, Now.AddHours(-3), Now);

        Assert.False(panel.IsLive);
        Assert.Equal("Crystal Oasis", panel.Map);
        Assert.Equal("Sweet Child O Night · last seen 3 hours ago", panel.Who);
        Assert.Empty(panel.Lines);
    }

    [Fact]
    public void A_reading_just_past_the_poll_still_counts_as_now()
    {
        // The poll runs every quarter hour; a character still playing must not flicker between.
        Assert.True(HereWidget.Panel(Playing, Now.AddMinutes(-17), Now).IsLive);
        Assert.False(HereWidget.Panel(Playing, Now.AddMinutes(-21), Now).IsLive);
    }

    [Fact]
    public void Nothing_ever_read_asks_for_the_bridge()
    {
        var panel = HereWidget.Panel(null, null, Now);

        Assert.False(panel.IsLive);
        Assert.Equal("Not in the game", panel.Map);
        Assert.Empty(panel.Lines);
    }

    [Fact]
    public void A_map_with_nothing_said_about_it_still_names_the_map()
    {
        var panel = HereWidget.Panel(new HereReading("Imosan", "Lion's Arch", null), Now, Now);

        Assert.True(panel.IsLive);
        Assert.Equal("Lion's Arch", panel.Map);
        Assert.Empty(panel.Lines);
    }

    [Theory]
    [InlineData("Here: nothing left", "Nothing left")]
    [InlineData("Here: Battle in Tarir (Octovine) on now", "Battle in Tarir (Octovine) on now")]
    public void One_fact_is_one_line(string line, string expected) =>
        Assert.Equal([expected], HereWidget.Facts(line));
}
