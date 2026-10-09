namespace GuildWarsCompanion.Services;

/// <summary>
/// The world boss timetable.
///
/// This is hardcoded because the Guild Wars 2 API does not publish one: there is no timer
/// endpoint, and /v2/worldbosses gives only the list of bosses, not when they appear. Every
/// timer site does the same. The upside is that the schedule is genuinely fixed to UTC; the
/// cost is that a rotation change has to be edited here.
///
/// The core bosses fill every quarter-hour slot by interleaving two cycles: the :00 and :30
/// slots rotate on three hours, the :15 and :45 slots on two.
/// </summary>
public static class EventSchedule
{
    /// <summary>A boss that appears on a fixed repeating cycle anchored to 00:00 UTC.</summary>
    private sealed record Repeating(
        string Name, string ApiId, string Location, int Level,
        int IntervalHours, int OffsetHours, int OffsetMinutes);

    /// <summary>A boss with its own irregular list of daily times.</summary>
    private sealed record Fixed(
        string Name, string ApiId, string Location, int Level, (int Hour, int Minute)[] Times);

    private static readonly Repeating[] Rotating =
    [
        // :00 — three-hour cycle
        new("Admiral Taidha Covington", "admiral_taidha_covington", "Bloodtide Coast", 45, 3, 0, 0),
        new("The Shatterer", "the_shatterer", "Blazeridge Steppes", 55, 3, 1, 0),
        new("Inquest Golem Mark II", "inquest_golem_mark_ii", "Mount Maelstrom", 70, 3, 2, 0),

        // :15 — two-hour cycle
        new("Svanir Shaman Chief", "svanir_shaman_chief", "Wayfarer Foothills", 25, 2, 0, 15),
        new("Great Jungle Wurm", "great_jungle_wurm", "Caledon Forest", 25, 2, 1, 15),

        // :30 — three-hour cycle
        new("Megadestroyer", "megadestroyer", "Mount Maelstrom", 70, 3, 0, 30),
        new("Modniir Ulgoth", "modniir_ulgoth", "Harathi Hinterlands", 55, 3, 1, 30),
        new("Claw of Jormag", "claw_of_jormag", "Frostgorge Sound", 70, 3, 2, 30),

        // :45 — two-hour cycle
        new("Fire Elemental", "fire_elemental", "Metrica Province", 15, 2, 0, 45),
        new("Shadow Behemoth", "shadow_behemoth", "Queensdale", 15, 2, 1, 45)
    ];

    private static readonly Fixed[] Irregular =
    [
        new("Karka Queen", "karka_queen", "Southsun Cove", 80,
            [(2, 0), (6, 0), (10, 30), (15, 0), (18, 0), (23, 0)]),
        new("Tequatl the Sunless", "tequatl_the_sunless", "Sparkfly Fen", 80,
            [(0, 0), (3, 0), (7, 0), (11, 30), (16, 0), (19, 0)]),
        new("Triple Trouble Wurm", "triple_trouble_wurm", "Bloodtide Coast", 80,
            [(1, 0), (4, 0), (8, 0), (12, 30), (17, 0), (20, 0)])
    ];

    /// <summary>
    /// Bosses that count for the daily but whose schedule is not encoded here — Drakkar and
    /// the Mists and Monsters titans. Listing them with a guessed time would be worse than
    /// leaving them out, and the screen says so.
    /// </summary>
    public static IReadOnlyList<string> Unscheduled { get; } = ["drakkar", "mists_and_monsters_titans"];

    /// <summary>Every boss with its next appearance strictly after <paramref name="utcNow"/>.</summary>
    public static IReadOnlyList<(string Name, string ApiId, string Location, int Level, DateTimeOffset Next)>
        NextAppearances(DateTimeOffset utcNow)
    {
        var results = new List<(string, string, string, int, DateTimeOffset)>();

        foreach (var boss in Rotating)
        {
            var start = boss.OffsetHours * 60 + boss.OffsetMinutes;
            var step = boss.IntervalHours * 60;

            // 24 divides both 2 and 3, so the pattern repeats cleanly each UTC day; walking
            // today and tomorrow is enough to always find the next one.
            var candidates = Enumerable
                .Range(0, 24 * 60 / step)
                .SelectMany(k => new[] { start + k * step, start + k * step + 24 * 60 });

            results.Add((boss.Name, boss.ApiId, boss.Location, boss.Level, FirstAfter(utcNow, candidates)));
        }

        foreach (var boss in Irregular)
        {
            var minutes = boss.Times.Select(t => t.Hour * 60 + t.Minute).ToList();
            var candidates = minutes.Concat(minutes.Select(m => m + 24 * 60));

            results.Add((boss.Name, boss.ApiId, boss.Location, boss.Level, FirstAfter(utcNow, candidates)));
        }

        return results.OrderBy(r => r.Item5).ToList();
    }

    private static DateTimeOffset FirstAfter(DateTimeOffset utcNow, IEnumerable<int> minutesFromMidnight)
    {
        var midnight = new DateTimeOffset(utcNow.UtcDateTime.Date, TimeSpan.Zero);

        return minutesFromMidnight
            .Select(m => midnight.AddMinutes(m))
            .Where(t => t > utcNow)
            .Min();
    }
}
