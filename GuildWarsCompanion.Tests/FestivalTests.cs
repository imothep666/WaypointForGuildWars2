using GuildWarsCompanion.Services;

using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// Festivals: their timer entries are in the wiki's data all year, and join the maps only while
/// the festival is on.
/// </summary>
public class FestivalTests
{
    private const string Data =
        """
        {
          "events": {
            "hot-ab": { "category": "Heart of Thorns", "name": "Auric Basin",
                   "segments": { "0": { "name": "Defending Tarir (Pylons)" } },
                   "sequences": { "partial": [], "pattern": [ { "r": 0, "d": 120 } ] } },
            "festival-ha": { "category": "Special Events", "name": "Halloween",
                   "segments": { "0": { "name": "" }, "1": { "name": "Mad King Says" } },
                   "sequences": { "partial": [], "pattern": [ { "r": 1, "d": 10 }, { "r": 0, "d": 110 } ] } }
          }
        }
        """;

    private static readonly DateTimeOffset DuringHalloween = new(2026, 10, 29, 17, 5, 0, TimeSpan.Zero);

    [Fact]
    public void Festival_entries_are_read_apart_from_the_maps()
    {
        Assert.Equal(["Auric Basin"], MetaSchedule.Parse(Data).Select(map => map.Name));
        Assert.Equal(["festival-ha"], MetaSchedule.ParseFestivals(Data).Select(map => map.Key));
    }

    [Fact]
    public void A_festival_joins_the_maps_only_while_it_is_on()
    {
        var maps = MetaSchedule.Parse(Data);
        var festivals = MetaSchedule.ParseFestivals(Data);

        var during = MetaSource.WithFestivals(maps, festivals, DuringHalloween);
        Assert.Equal(["Halloween", "Auric Basin"], during.Select(map => map.Name));
        Assert.StartsWith("Halloween · ", during[0].Category);

        Assert.Same(maps, MetaSource.WithFestivals(maps, festivals, new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.Zero)));
        Assert.Same(maps, MetaSource.WithFestivals(maps, festivals, new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void Mad_King_Says_comes_every_two_hours_from_midnight()
    {
        var halloween = MetaSchedule.ParseFestivals(Data).Single();

        var next = MetaSchedule.Next(halloween, DuringHalloween)!.Value;

        Assert.Equal("Mad King Says", next.Name);
        Assert.Equal(new DateTimeOffset(2026, 10, 29, 18, 0, 0, TimeSpan.Zero), next.Start);
    }

    [Fact]
    public void Halloween_2026_runs_through_the_third_of_November()
    {
        var period = Assert.Single(Festivals.Announced);

        Assert.True(period.IsOn(DuringHalloween));
        Assert.True(period.IsOn(new DateTimeOffset(2026, 11, 3, 22, 0, 0, TimeSpan.Zero)));
        Assert.False(period.IsOn(new DateTimeOffset(2026, 11, 3, 23, 0, 0, TimeSpan.Zero)));
        Assert.False(period.IsOn(new DateTimeOffset(2026, 10, 12, 21, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void Every_announced_festival_is_one_the_app_knows()
    {
        foreach (var period in Festivals.Announced)
            Assert.Contains(period.Festival, Festivals.All);
    }

    [Fact]
    public void The_calendar_puts_the_festival_on_first_and_the_rest_by_their_usual_start()
    {
        var calendar = Festivals.Calendar(Festivals.Announced, DuringHalloween);

        Assert.Equal(
            ["halloween", "wintersday", "lunar", "sab", "dragonbash", "fourwinds"],
            calendar.Select(entry => entry.Festival.Key));

        Assert.True(calendar[0].IsOn);
        Assert.All(calendar.Skip(1), entry => Assert.False(entry.IsAnnounced));
        Assert.Equal(new DateTime(2026, 12, 12), calendar[1].Next);
        Assert.Equal(new DateTime(2027, 1, 25), calendar[2].Next);
    }

    [Fact]
    public void An_announced_festival_shows_its_dates_and_moves_ahead_of_its_season()
    {
        // Noon UTC, so the days are the same in every time zone the tests run in.
        var wintersday = new FestivalPeriod(Festivals.ByKey("wintersday")!,
            new DateTimeOffset(2026, 12, 8, 12, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2027, 1, 5, 12, 0, 0, TimeSpan.Zero));

        var now = new DateTimeOffset(2026, 11, 28, 12, 0, 0, TimeSpan.Zero);
        var entry = Festivals.Calendar([..Festivals.Announced, wintersday], now)[0];

        Assert.Equal("wintersday", entry.Festival.Key);
        Assert.True(entry.IsAnnounced);
        Assert.False(entry.IsOn);
        Assert.Equal(new DateTime(2026, 12, 8), entry.Next);
        Assert.Equal(wintersday.Span, entry.When);
    }

    [Fact]
    public void A_festival_that_ended_waits_for_next_year()
    {
        var calendar = Festivals.Calendar(Festivals.Announced, new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero));

        Assert.DoesNotContain(calendar, entry => entry.IsOn);
        Assert.Equal("halloween", calendar[^1].Festival.Key);
        Assert.Equal(new DateTime(2027, 10, 13), calendar[^1].Next);
    }

    [Fact]
    public void A_row_with_a_time_has_no_bar_and_shows_the_time()
    {
        var row = new TodayRow("Mad King Says", 0, 0, "next festival event", Figure: "18:00");

        Assert.False(row.HasBar);
        Assert.Equal("18:00", row.Progress);
        Assert.False(row.IsQuiet);
        Assert.True(new TodayRow("Halloween Rituals", 5, 23, null).HasBar);
    }
}
