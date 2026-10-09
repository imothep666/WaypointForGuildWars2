using GuildWarsCompanion.Services;

using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// The map meta timetable, read the way the wiki's own widget reads it: the partial sequence once
/// from 00:00 UTC, then the pattern for the rest of the day.
///
/// The fixture is an excerpt of <c>Widget:Event timer/data.json</c> as it stood on 2026-10-05, so
/// the shape is the real one. Auric Basin's Octovine at the odd hours is the check that the
/// reading is right: it is the meta every player knows the time of.
/// </summary>
public class MetaScheduleTests
{
    private const string Data =
        """
        {
          "config": { "version": "v5.4" },
          "events": {
            "t": { "category": "", "name": "", "segments": { "1": { "name": "" } },
                   "sequences": { "partial": [], "pattern": [ { "r": 1, "d": 15 } ] } },
            "core-wb": { "category": "Core Tyria", "name": "World bosses",
                   "segments": { "0": { "name": "Admiral Taidha Covington" } },
                   "sequences": { "partial": [], "pattern": [ { "r": 0, "d": 15 } ] } },
            "hot-ab": { "category": "Heart of Thorns", "name": "Auric Basin",
                   "segments": {
                     "0": { "name": "Defending Tarir (Pylons)" }, "1": { "name": "Challenges" },
                     "2": { "name": "Battle in Tarir (Octovine)" }, "3": { "name": "Reset" } },
                   "sequences": {
                     "partial": [ { "r": 0, "d": 45 }, { "r": 1, "d": 15 }, { "r": 2, "d": 20 }, { "r": 3, "d": 10 } ],
                     "pattern": [ { "r": 0, "d": 75 }, { "r": 1, "d": 15 }, { "r": 2, "d": 20 }, { "r": 3, "d": 10 } ] } },
            "pof-dh": { "category": "Path of Fire", "name": "Desert Highlands",
                   "segments": { "0": { "name": "" }, "1": { "name": "Buried Treasure" } },
                   "sequences": {
                     "partial": [ { "r": 0, "d": 60 }, { "r": 1, "d": 20 } ],
                     "pattern": [ { "r": 0, "d": 100 }, { "r": 1, "d": 20 } ] } },
            "hot-ds": { "category": "Heart of Thorns", "name": "Dragon's Stand",
                   "segments": { "0": { "name": "(continued)" }, "1": { "name": "Start advancing on the Blighting Towers" } },
                   "sequences": { "partial": [ { "r": 0, "d": 90 } ], "pattern": [ { "r": 1, "d": 120 } ] } },
            "festival-ha": { "category": "Special Events", "name": "Halloween",
                   "segments": { "1": { "name": "Mad King Says" } },
                   "sequences": { "partial": [], "pattern": [ { "r": 1, "d": 10 } ] } }
          }
        }
        """;

    private static readonly IReadOnlyList<MetaMap> Maps = MetaSchedule.Parse(Data);

    private static MetaMap Map(string name) => Maps.Single(map => map.Name == name);

    private static DateTimeOffset Utc(int hour, int minute) => new(2026, 10, 5, hour, minute, 0, TimeSpan.Zero);

    [Fact]
    public void Only_maps_that_always_run_are_kept()
    {
        // Not the timeline row, not the world bosses (the timetable has them), not a festival the
        // data lists all year.
        Assert.Equal(["Auric Basin", "Desert Highlands", "Dragon's Stand"], Maps.Select(map => map.Name));
    }

    [Fact]
    public void A_segment_repeated_back_to_back_is_a_new_run_each_time()
    {
        // Seen on the phone: joined, Dragon's Stand ran "until 02:00", the whole day as one meta.
        var on = MetaSchedule.Current(Map("Dragon's Stand"), Utc(11, 16))!.Value;

        Assert.Equal(Utc(9, 30), on.Start);
        Assert.Equal(Utc(11, 30), on.End);
    }

    [Fact]
    public void Continued_at_midnight_is_the_run_carried_over_not_an_event_of_that_name()
    {
        var stand = Map("Dragon's Stand");

        Assert.Equal("Start advancing on the Blighting Towers", MetaSchedule.Current(stand, Utc(0, 30))!.Value.Name);
        Assert.DoesNotContain("(continued)", MetaSchedule.Events(stand));
    }

    [Fact]
    public void Octovine_is_at_the_odd_hours()
    {
        var auric = Map("Auric Basin");

        var first = MetaSchedule.Next(auric, Utc(0, 50))!.Value;
        Assert.Equal("Battle in Tarir (Octovine)", first.Name);
        Assert.Equal(Utc(1, 0), first.Start);
        Assert.Equal(Utc(1, 20), first.End);

        // After the partial sequence, the pattern: Octovine again two hours later.
        var octovines = Enumerable.Range(0, 24)
            .Select(hour => MetaSchedule.Current(auric, Utc(hour, 5)))
            .Where(moment => moment?.Name == "Battle in Tarir (Octovine)")
            .Select(moment => moment!.Value.Start.Hour);

        Assert.Equal([1, 3, 5, 7, 9, 11, 13, 15, 17, 19, 21, 23], octovines);
    }

    [Fact]
    public void Now_says_what_is_running_and_until_when()
    {
        var on = MetaSchedule.Current(Map("Auric Basin"), Utc(13, 10))!.Value;

        Assert.Equal("Battle in Tarir (Octovine)", on.Name);
        Assert.Equal(Utc(13, 20), on.End);
    }

    [Fact]
    public void A_lull_is_not_an_event()
    {
        var highlands = Map("Desert Highlands");

        // 00:00 to 01:00 is the unnamed gap before the first Buried Treasure.
        Assert.Null(MetaSchedule.Current(highlands, Utc(0, 30)));
        Assert.Equal(Utc(1, 0), MetaSchedule.Next(highlands, Utc(0, 30))!.Value.Start);
        Assert.Equal(Utc(3, 0), MetaSchedule.Next(highlands, Utc(1, 30))!.Value.Start);
    }

    [Fact]
    public void The_next_one_can_be_tomorrows()
    {
        var next = MetaSchedule.Next(Map("Auric Basin"), Utc(23, 30))!.Value;

        // The day starts its partial sequence again at midnight: Pylons first.
        Assert.Equal("Defending Tarir (Pylons)", next.Name);
        Assert.Equal(Utc(0, 0).AddDays(1), next.Start);
    }

    [Fact]
    public void The_day_is_exactly_a_day_long()
    {
        foreach (var map in Maps)
        {
            Assert.Equal(0, map.Day[0].Start);
            Assert.Equal(24 * 60, map.Day[^1].End);
        }
    }

    [Fact]
    public void A_reminder_is_for_the_chosen_event_only_and_inside_the_window()
    {
        string[] chosen = [MetaReminderStore.For("hot-ab", "Battle in Tarir (Octovine)")];

        // 12:30 UTC: Octovine at 13:00, thirty minutes out — inside the boss window of 35.
        var call = Assert.Single(MetaAlerts.Due(Maps, chosen, Utc(12, 30)));
        Assert.Equal(Utc(13, 0), call.At);
        Assert.Equal("Battle in Tarir (Octovine) in 30 minutes", MetaAlerts.Title(call));

        // 12:00: an hour out, not yet. And the pylons at 13:30 were not asked for.
        Assert.Empty(MetaAlerts.Due(Maps, chosen, Utc(12, 0)));
        Assert.Empty(MetaAlerts.Due(Maps, [], Utc(12, 30)));
    }

    [Fact]
    public void One_reminder_per_occurrence()
    {
        string[] chosen = [MetaReminderStore.For("hot-ab", "Battle in Tarir (Octovine)")];

        var early = MetaAlerts.Due(Maps, chosen, Utc(12, 30)).Single();
        var late = MetaAlerts.Due(Maps, chosen, Utc(12, 50)).Single();
        var next = MetaAlerts.Due(Maps, chosen, Utc(14, 30)).Single();

        Assert.Equal(early.Key, late.Key);
        Assert.NotEqual(early.Key, next.Key);
    }

    [Fact]
    public void A_maps_events_are_listed_once_each_in_the_order_of_a_day()
    {
        Assert.Equal(
            ["Defending Tarir (Pylons)", "Challenges", "Battle in Tarir (Octovine)", "Reset"],
            MetaSchedule.Events(Map("Auric Basin")));
    }

    [Fact]
    public void The_here_line_says_a_meta_on_now_and_one_coming()
    {
        Assert.Equal("Here: Battle in Tarir (Octovine) on now",
            HereLine.Describe(new HereFacts(null, 0, null, [], new HereMeta("Battle in Tarir (Octovine)", true, TimeSpan.Zero))));
        Assert.Equal("Here: Challenges on now · Battle in Tarir (Octovine) in 12 min",
            HereLine.Describe(new HereFacts(null, 0, null, [],
                new HereMeta("Challenges", true, TimeSpan.Zero, ("Battle in Tarir (Octovine)", TimeSpan.FromMinutes(12))))));
        Assert.Equal("Here: map chest waiting · Battle in Tarir (Octovine) in 20 min",
            HereLine.Describe(new HereFacts(false, 0, null, [], new HereMeta("Battle in Tarir (Octovine)", false, TimeSpan.FromMinutes(20)))));
    }
}
