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

        var during = MetaSource.WithFestivals(maps, festivals, Festivals.Shipped, DuringHalloween);
        Assert.Equal(["Halloween", "Auric Basin"], during.Select(map => map.Name));
        Assert.StartsWith("Halloween · ", during[0].Category);

        Assert.Same(maps, MetaSource.WithFestivals(maps, festivals, Festivals.Shipped, new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.Zero)));
        Assert.Same(maps, MetaSource.WithFestivals(maps, festivals, Festivals.Shipped, new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero)));
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
        var period = Festivals.Shipped.Single(run => run.Festival.Key == "halloween");

        Assert.True(period.IsOn(DuringHalloween));
        Assert.True(period.IsOn(new DateTimeOffset(2026, 11, 3, 22, 0, 0, TimeSpan.Zero)));
        Assert.False(period.IsOn(new DateTimeOffset(2026, 11, 3, 23, 0, 0, TimeSpan.Zero)));
        Assert.False(period.IsOn(new DateTimeOffset(2026, 10, 12, 21, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void Every_announced_festival_is_one_the_app_knows()
    {
        foreach (var period in Festivals.Shipped)
            Assert.Contains(period.Festival, Festivals.All);
    }

    [Fact]
    public void The_calendar_puts_the_festival_on_first_and_the_rest_by_when_they_start()
    {
        var calendar = Festivals.Calendar(Festivals.Shipped, DuringHalloween);

        Assert.Equal(
            ["halloween", "wintersday", "lunar", "sab", "dragonbash", "fourwinds"],
            calendar.Select(entry => entry.Festival.Key));

        Assert.True(calendar[0].IsOn);
        Assert.True(calendar[1].IsAnnounced);
        Assert.Equal(new DateTime(2026, 12, 8), calendar[1].Next);
        Assert.All(calendar.Skip(2), entry => Assert.False(entry.IsAnnounced));
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
        var entry = Festivals.Calendar([Festivals.Shipped[0], wintersday], now)[0];

        Assert.Equal("wintersday", entry.Festival.Key);
        Assert.True(entry.IsAnnounced);
        Assert.False(entry.IsOn);
        Assert.Equal(new DateTime(2026, 12, 8), entry.Next);
        Assert.Equal(wintersday.Span, entry.When);
    }

    [Fact]
    public void A_festival_that_ended_waits_for_next_year()
    {
        var calendar = Festivals.Calendar(Festivals.Shipped, new DateTimeOffset(2027, 1, 10, 12, 0, 0, TimeSpan.Zero));

        Assert.DoesNotContain(calendar, entry => entry.IsOn);
        Assert.Equal("wintersday", calendar[^1].Festival.Key);
        Assert.Equal(new DateTime(2027, 12, 9), calendar[^1].Next);
    }

    /// <summary>The wiki API's answer for two of the pages, cut to the lines that matter.</summary>
    private const string WikiAnswer =
        """
        {"batchcomplete":true,"query":{"pages":[
          {"pageid":1,"ns":0,"title":"Wintersday","revisions":[{"slots":{"main":{"contentmodel":"wikitext","content":
            "== Releases ==\n* [[A Very Merry Wintersday 2025]] <small>(2025-12-09 – 2026-01-06)</small>\n* [[A Very Merry Wintersday 2026]] <small>(2026-12-08 – 2027-01-05)</small>\n"}}}]},
          {"pageid":2,"ns":0,"title":"Lunar New Year","revisions":[{"slots":{"main":{"contentmodel":"wikitext","content":
            "* [[Lunar New Year 2026]]: Year of the Horse <small>(2026-02-03 – 2026-02-24)</small>\n* [[Lunar New Year 2027]] <small>(2027-02-02)</small>"}}}]},
          {"ns":0,"title":"Not a festival","missing":true}
        ]}}
        """;

    [Fact]
    public void The_wiki_pages_give_each_run_by_its_first_and_last_day()
    {
        var periods = Festivals.ParseWiki(WikiAnswer);

        Assert.Equal(
            ["wintersday 2025-12-09", "wintersday 2026-12-08", "lunar 2026-02-03"],
            periods.Select(period => $"{period.Festival.Key} {period.Start:yyyy-MM-dd}"));

        var wintersday = periods[1];
        Assert.Equal(new DateTimeOffset(2026, 12, 7, 23, 0, 0, TimeSpan.Zero), wintersday.Start.ToUniversalTime());
        Assert.Equal(new DateTimeOffset(2027, 1, 5, 23, 0, 0, TimeSpan.Zero), wintersday.End.ToUniversalTime());
    }

    [Fact]
    public void Summer_days_are_counted_in_summer_time()
    {
        var dragonBash = Festivals.Run(Festivals.ByKey("dragonbash")!, new DateOnly(2026, 6, 2), new DateOnly(2026, 6, 23));

        Assert.Equal(new DateTimeOffset(2026, 6, 1, 22, 0, 0, TimeSpan.Zero), dragonBash.Start.ToUniversalTime());
        Assert.Equal(new DateTimeOffset(2026, 6, 23, 22, 0, 0, TimeSpan.Zero), dragonBash.End.ToUniversalTime());
    }

    [Theory]
    [InlineData("""{"batchcomplete":true,"query":{"pages":[{"ns":0,"title":"Halloween","revisions":[{"slots":{"main":{"content":"No list here."}}}]}]}}""")]
    [InlineData("""{"error":{"code":"readonly","info":"The wiki is in read-only mode"}}""")]
    public void An_answer_that_lists_no_runs_is_not_taken_for_one(string json)
    {
        Assert.ThrowsAny<Exception>(() => Festivals.ParseWiki(json));
    }

    [Fact]
    public void The_wiki_overrules_the_shipped_dates_for_a_festival_and_year_it_lists()
    {
        var halloween = Festivals.ByKey("halloween")!;
        var moved = Festivals.Run(halloween, new DateOnly(2026, 10, 13), new DateOnly(2026, 11, 10));

        var merged = Festivals.Merge([moved], Festivals.Shipped);

        Assert.Equal(new DateOnly(2026, 11, 10), DateOnly.FromDateTime(merged.Single(run => run.Festival == halloween).End.AddDays(-1).DateTime));
        Assert.Contains(merged, run => run.Festival.Key == "wintersday");
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
