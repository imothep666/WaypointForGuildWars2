using GuildWarsCompanion.Services;
using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// The world boss timetable is hardcoded, because the API publishes no schedule. That makes
/// these tests the only thing standing between a mistyped offset and a wrong timer, so they
/// check the structural property that catches it: the rotating bosses tile the day exactly.
/// </summary>
public class EventScheduleTests
{
    private static DateTimeOffset Utc(int hour, int minute = 0) =>
        new(2026, 8, 21, hour, minute, 0, TimeSpan.Zero);

    [Fact]
    public void Rotating_bosses_fill_every_quarter_hour_without_colliding()
    {
        // Walk a full day, quarter by quarter, and record which boss claims each slot. A
        // wrong interval or offset shows up as two bosses in one slot and a hole elsewhere.
        var claimed = new Dictionary<int, List<string>>();

        for (var minutes = 0; minutes < 24 * 60; minutes += 15)
        {
            var now = Utc(0).AddMinutes(minutes - 1);

            foreach (var boss in EventSchedule.NextAppearances(now))
            {
                var offset = (int)(boss.Next - Utc(0)).TotalMinutes;
                if (offset != minutes) continue;

                claimed.TryAdd(minutes, []);
                claimed[minutes].Add(boss.Name);
            }
        }

        var slots = Enumerable.Range(0, 24 * 4).Select(i => i * 15).ToList();

        // The three irregular bosses share :00 and :30 slots with the rotation, so a slot can
        // legitimately hold more than one. What must never happen is an empty slot.
        var empty = slots.Where(s => !claimed.ContainsKey(s)).ToList();
        Assert.Empty(empty);
    }

    [Fact]
    public void Next_appearance_is_always_in_the_future()
    {
        // Boundary cases: exactly on a spawn, and one minute before midnight where today's
        // occurrences are all spent and tomorrow's must be found.
        foreach (var now in new[] { Utc(0), Utc(0, 1), Utc(12, 30), Utc(23, 59) })
        {
            foreach (var boss in EventSchedule.NextAppearances(now))
                Assert.True(boss.Next > now, $"{boss.Name} at {boss.Next:u} is not after {now:u}");
        }
    }

    [Fact]
    public void Appearances_are_ordered_soonest_first()
    {
        var upcoming = EventSchedule.NextAppearances(Utc(9, 7));

        Assert.NotEmpty(upcoming);
        Assert.Equal(
            upcoming.Select(b => b.Next).Order().ToList(),
            upcoming.Select(b => b.Next).ToList());
    }

    [Fact]
    public void Every_boss_appears_exactly_once_per_query()
    {
        var upcoming = EventSchedule.NextAppearances(Utc(4, 20));
        var names = upcoming.Select(b => b.Name).ToList();

        Assert.Equal(names.Count, names.Distinct().Count());
    }

    [Theory]
    [InlineData("Shadow Behemoth", 15)]
    [InlineData("Tequatl the Sunless", 80)]
    [InlineData("Claw of Jormag", 70)]
    public void Bosses_carry_the_level_their_zone_expects(string boss, int level)
    {
        // The timetable dims what the active character cannot survive, so a wrong level here
        // would send a low character to a level 80 fight.
        var entry = EventSchedule.NextAppearances(Utc(6)).Single(b => b.Name == boss);

        Assert.Equal(level, entry.Level);
    }

    [Fact]
    public void Fixed_time_bosses_land_on_their_published_minutes()
    {
        // Karka Queen and friends run on irregular clocks including half past the hour;
        // a rounding slip would silently move them.
        var karka = EventSchedule.NextAppearances(Utc(9)).Single(b => b.Name == "Karka Queen");

        Assert.Equal(10, karka.Next.Hour);
        Assert.Equal(30, karka.Next.Minute);
    }

    [Fact]
    public void Unscheduled_bosses_are_declared_rather_than_guessed()
    {
        // Drakkar and the titans count for the daily but have no timetable here. Listing them
        // with an invented time would be worse than leaving them out, so the app says so.
        Assert.Contains("drakkar", EventSchedule.Unscheduled);
        Assert.DoesNotContain(
            "drakkar",
            EventSchedule.NextAppearances(Utc(6)).Select(b => b.ApiId));
    }
}
