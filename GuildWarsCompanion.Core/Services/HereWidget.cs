namespace GuildWarsCompanion.Services;

/// <summary>
/// One reading of where a character is and what is left there, as the Here widget keeps it: the
/// character, the map, and <see cref="HereLine"/>'s words for it. Stored whenever someone is in
/// the game - by Today when it is opened, and by the widget's own poll - and never cleared when
/// the game closes, so the widget can say where they were last.
/// </summary>
public sealed record HereReading(string Character, string? Map, string? Line);

/// <summary>What the Here widget draws.</summary>
/// <param name="Map">The heading: the map, or what to do to get one.</param>
/// <param name="Who">Who is there, or when they were last.</param>
/// <param name="Lines">What is left on the map, one fact a line, at most three; empty when the
/// reading is too old to be about now.</param>
/// <param name="IsLive">Whether the reading is recent enough to be about now.</param>
public sealed record HerePanel(string Map, string Who, IReadOnlyList<string> Lines, bool IsLive);

public static class HereWidget
{
    /// <summary>
    /// How long a reading counts as now. A little over the widget's quarter-hour poll, so a
    /// character still in the game never flickers to "last seen" between two readings.
    /// </summary>
    public static readonly TimeSpan LiveFor = TimeSpan.FromMinutes(20);

    private const int MostLines = 3;

    /// <summary>
    /// The panel for a stored reading. What is left on a map is only said while the reading is
    /// live: its boss times and its "map chest waiting" were about then, and a widget repeating
    /// them an evening later would be stating old news as today's.
    /// </summary>
    public static HerePanel Panel(HereReading? reading, DateTimeOffset? readAt, DateTimeOffset now)
    {
        if (reading is null || readAt is not { } at)
            return new HerePanel(L.T("Not in the game"), L.T("Play with the bridge running on your PC"), [], false);

        var map = reading.Map is { Length: > 0 } named ? named : L.T("Somewhere in Tyria");

        if (now - at > LiveFor)
            return new HerePanel(map, L.F("{0} · last seen {1}", reading.Character, Stale.Age(at, now)), [], false);

        return new HerePanel(map, reading.Character, Facts(reading.Line), true);
    }

    /// <summary>"Here: map chest waiting · Shadow Behemoth in 25 min" as one fact a line, the way
    /// a widget has room for them: down, rather than along.</summary>
    public static IReadOnlyList<string> Facts(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return [];

        // The prefix in the phone's language, or in English for a reading stored before it had one.
        var text = line.StartsWith(HereLine.Prefix, StringComparison.Ordinal) ? line[HereLine.Prefix.Length..]
            : line.StartsWith("Here: ", StringComparison.Ordinal) ? line[6..]
            : line;

        var facts = text.Split(" · ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return facts.Length == 0
            ? []
            : facts.Take(MostLines).Select(fact => char.ToUpperInvariant(fact[0]) + fact[1..]).ToList();
    }
}
