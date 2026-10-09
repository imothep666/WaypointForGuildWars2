using System.Text.Json;

namespace GuildWarsCompanion.Services;

/// <summary>One stretch of a map's day: when it starts, in minutes after 00:00 UTC, how long it
/// lasts, and what it is called. An empty name is a lull, not an event.</summary>
public readonly record struct MetaStretch(int Start, int Length, string Name)
{
    public int End => Start + Length;

    public bool IsEvent => Name.Length > 0;
}

/// <summary>A map's meta events through one UTC day; every day is the same.</summary>
public sealed record MetaMap(string Key, string Category, string Name, IReadOnlyList<MetaStretch> Day);

/// <summary>One occurrence of a meta event, in real time.</summary>
public readonly record struct MetaMoment(string Name, DateTimeOffset Start, DateTimeOffset End);

/// <summary>
/// The map meta timetable, read from the Guild Wars 2 Wiki's event timer data.
///
/// The API publishes no schedule for metas any more than for world bosses. The wiki's
/// <c>Widget:Event timer/data.json</c> is what its own timer runs on, kept up by its editors, and
/// it covers every expansion to date — typing it in here from memory would be the "recall
/// presented as data" this app refuses. The shape is the wiki's: per map, named segments, a
/// <c>partial</c> sequence played once from 00:00 UTC and a <c>pattern</c> repeated after it for
/// the rest of the day. That reading is the widget's own <c>fullPatternGenerator</c>, not a guess.
/// </summary>
public static class MetaSchedule
{
    private const int DayMinutes = 24 * 60;

    /// <summary>
    /// Entries that are not a map's metas, or not always running: the timeline row, the world
    /// bosses (the timetable has its own, with kills), and festivals, which the data lists all
    /// year whether or not the festival is on.
    /// </summary>
    private static readonly HashSet<string> Skipped = new(StringComparer.Ordinal) { "t", "core-wb", "core-hwb" };

    private const string Festivals = "Special Events";

    /// <summary>Every map the data describes, in the data's own order — release order.</summary>
    public static IReadOnlyList<MetaMap> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);

        var maps = new List<MetaMap>();

        foreach (var entry in document.RootElement.GetProperty("events").EnumerateObject())
        {
            var meta = entry.Value;
            var category = Text(meta, "category");

            if (Skipped.Contains(entry.Name) || category == Festivals) continue;

            var names = new Dictionary<string, string>();

            if (meta.TryGetProperty("segments", out var segments))
                foreach (var segment in segments.EnumerateObject())
                    names[segment.Name] = Text(segment.Value, "name");

            var sequences = meta.GetProperty("sequences");
            var partial = Steps(sequences, "partial");
            var pattern = Steps(sequences, "pattern");

            var day = Lay(partial, pattern, names);

            if (day.Any(stretch => stretch.IsEvent))
                maps.Add(new MetaMap(entry.Name, category, Text(meta, "name"), day));
        }

        return maps;
    }

    /// <summary>The event running at <paramref name="utcNow"/>, or null in a lull.</summary>
    public static MetaMoment? Current(MetaMap map, DateTimeOffset utcNow)
    {
        var midnight = Midnight(utcNow);
        var minute = (utcNow - midnight).TotalMinutes;

        foreach (var stretch in map.Day)
            if (stretch.Start <= minute && minute < stretch.End)
                return stretch.IsEvent ? Moment(stretch, midnight) : null;

        return null;
    }

    /// <summary>The next event to start after <paramref name="utcNow"/>, today or tomorrow.</summary>
    public static MetaMoment? Next(MetaMap map, DateTimeOffset utcNow)
    {
        var midnight = Midnight(utcNow);

        foreach (var day in new[] { midnight, midnight.AddDays(1) })
            foreach (var stretch in map.Day)
            {
                if (!stretch.IsEvent) continue;

                var start = day.AddMinutes(stretch.Start);

                if (start > utcNow) return Moment(stretch, day);
            }

        return null;
    }

    /// <summary>The next start of one particular event after <paramref name="utcNow"/>, today or
    /// tomorrow — for a reminder about that event and no other.</summary>
    public static MetaMoment? NextOf(MetaMap map, string name, DateTimeOffset utcNow)
    {
        var midnight = Midnight(utcNow);

        foreach (var day in new[] { midnight, midnight.AddDays(1) })
            foreach (var stretch in map.Day)
            {
                if (stretch.Name != name) continue;

                var start = day.AddMinutes(stretch.Start);

                if (start > utcNow) return Moment(stretch, day);
            }

        return null;
    }

    /// <summary>The distinct events of a map, in the order they come in a day.</summary>
    public static IReadOnlyList<string> Events(MetaMap map) =>
        map.Day.Where(stretch => stretch.IsEvent).Select(stretch => stretch.Name).Distinct().ToList();

    /// <summary>The wiki's name for the start of the day when a run began before midnight.</summary>
    private const string Continued = "(continued)";

    /// <summary>
    /// The day from 00:00 UTC: the partial sequence once, then the pattern until the day is full.
    /// A stretch that runs past midnight is cut there, since the next day starts the partial again.
    ///
    /// Stretches are never joined, even with the same name either side: Dragon's Stand repeats one
    /// two-hour segment all day, and each repetition is a new run. Joining them made the whole day
    /// one meta, "until 02:00", which is what the phone showed until this said so.
    ///
    /// "(continued)" at the start of a day is the previous day's last run carrying over midnight,
    /// so it takes that run's name — the last named segment of the pattern — rather than being
    /// announced as an event called "(continued)".
    /// </summary>
    private static List<MetaStretch> Lay(
        List<(string Segment, int Minutes)> partial, List<(string Segment, int Minutes)> pattern,
        Dictionary<string, string> names)
    {
        var day = new List<MetaStretch>();
        var at = 0;

        var carried = pattern
            .Select(step => names.GetValueOrDefault(step.Segment, string.Empty))
            .LastOrDefault(name => name.Length > 0 && name != Continued) ?? string.Empty;

        void Add((string Segment, int Minutes) step)
        {
            if (at >= DayMinutes || step.Minutes <= 0) return;

            var length = Math.Min(step.Minutes, DayMinutes - at);
            var name = names.GetValueOrDefault(step.Segment, string.Empty);

            if (name == Continued) name = carried;

            day.Add(new MetaStretch(at, length, name));

            at += length;
        }

        foreach (var step in partial) Add(step);

        // A pattern with no length would never fill the day; the wiki's widget guards the same way.
        if (pattern.Sum(step => step.Minutes) > 0)
            while (at < DayMinutes)
                foreach (var step in pattern)
                    Add(step);

        return day;
    }

    private static List<(string Segment, int Minutes)> Steps(JsonElement sequences, string name) =>
        sequences.TryGetProperty(name, out var steps) && steps.ValueKind == JsonValueKind.Array
            ? steps.EnumerateArray()
                .Select(step => (Number(step, "r").ToString(), Number(step, "d")))
                .ToList()
            : [];

    private static MetaMoment Moment(MetaStretch stretch, DateTimeOffset midnight) =>
        new(stretch.Name, midnight.AddMinutes(stretch.Start), midnight.AddMinutes(stretch.End));

    private static DateTimeOffset Midnight(DateTimeOffset utcNow) =>
        new(utcNow.UtcDateTime.Date, TimeSpan.Zero);

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static int Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : 0;
}
