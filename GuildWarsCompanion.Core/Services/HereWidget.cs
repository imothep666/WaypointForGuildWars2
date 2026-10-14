namespace GuildWarsCompanion.Services;

/// <summary>
/// One reading of where a character is and what is left there, as the Here widget keeps it: the
/// character, the map, and <see cref="HereLine"/>'s words for it. Stored whenever someone is in
/// the game - by Today when it is opened, and by the widget's own poll - and never cleared when
/// the game closes, so the widget can say where they were last.
/// </summary>
/// <param name="English">The map's English name, which the wiki files its loading screen and meta
/// timetable under, and the map chest's id is made from; null in a reading stored before the
/// widget showed the painting.</param>
/// <param name="MapId">The map's id, which places its world bosses; 0 in an older reading.</param>
/// <param name="Calling">"Chronomancer", or the profession without an elite; null until the API
/// has named it.</param>
/// <param name="Emblem">The key <see cref="Callings"/> keeps the calling's emblem under.</param>
public sealed record HereReading(
    string Character, string? Map, string? Line, string? English = null,
    int MapId = 0, string? Calling = null, string? Emblem = null);

/// <summary>
/// What is true of today, read each time the widget is drawn rather than kept in the reading:
/// a map's meta, chest and bosses are about the map, not about when someone stood in it, so they
/// are as true an evening later as they were then. Each part is null when it was not read since
/// the last reset, and is then left out rather than guessed.
/// </summary>
/// <param name="Meta">The reading's map in the wiki's meta timetable.</param>
/// <param name="Chests">Every map chest id the API knows.</param>
/// <param name="ChestsLooted">The chests looted since the reset.</param>
/// <param name="BossesKilled">The world bosses killed since the reset.</param>
public sealed record HereDay(
    MetaMap? Meta, IReadOnlyList<string>? Chests, IReadOnlySet<string>? ChestsLooted,
    IReadOnlySet<string>? BossesKilled);

/// <summary>How a fact line is coloured: brass for something waiting to be taken, grey for done.</summary>
public enum HereTone { Plain, Waiting, Done }

public sealed record HereFact(string Text, HereTone Tone = HereTone.Plain);

/// <summary>The map's meta, as the widget's main line says it.</summary>
/// <param name="StartsAt">When it starts, for the launcher to count down to; null while it is on.</param>
/// <param name="Note">The line under it: when it starts, or until when it runs and what follows.</param>
public sealed record HereMetaLine(string Name, DateTimeOffset? StartsAt, string Note)
{
    public bool IsOn => StartsAt is null;
}

/// <summary>What the Here widget draws.</summary>
/// <param name="Map">The heading: the map, or what to do to get one.</param>
/// <param name="Who">Who is there, or when they were last.</param>
/// <param name="Lines">What is left on the map, one fact a line, at most three.</param>
/// <param name="IsLive">Whether the reading is recent enough to be about now.</param>
/// <param name="Art">The map whose loading screen goes behind it, by English name; null for none.
/// Kept when the reading is old: it is still the place they were last.</param>
/// <param name="Meta">The map's meta, on now or next; null for a map without one.</param>
/// <param name="Emblem">The calling's emblem key, for <see cref="Callings.Held"/>.</param>
public sealed record HerePanel(
    string Map, string Who, IReadOnlyList<HereFact> Lines, bool IsLive, string? Art = null,
    HereMetaLine? Meta = null, string? Emblem = null);

public static class HereWidget
{
    /// <summary>
    /// How long a reading counts as now. A little over the widget's quarter-hour poll, so a
    /// character still in the game never flickers to "last seen" between two readings.
    /// </summary>
    public static readonly TimeSpan LiveFor = TimeSpan.FromMinutes(20);

    private const int MostLines = 3;

    /// <summary>
    /// The panel for a stored reading, and what is true of its map today.
    ///
    /// With <paramref name="day"/> the map's meta, chest and bosses are worked out now, from the
    /// timetable and today's lists, so they hold however old the reading is. Without it - a reading
    /// stored before the widget knew the map's English name, or a cold draw with no services -
    /// only the reading's own line is said, and only while it is live: its boss times and its
    /// "map chest waiting" were about then, and a widget repeating them an evening later would be
    /// stating old news as today's.
    /// </summary>
    public static HerePanel Panel(HereReading? reading, DateTimeOffset? readAt, DateTimeOffset now, HereDay? day = null)
    {
        if (reading is null || readAt is not { } at)
            return new HerePanel(L.T("Not in the game"), L.T("Play with the bridge running on your PC"), [], false);

        var map = reading.Map is { Length: > 0 } named ? named : L.T("Somewhere in Tyria");

        var art = reading.English is { Length: > 0 } english ? english : null;

        var live = now - at <= LiveFor;

        // The calling only while live: last seen, the line has the age to say, and the emblem
        // beside it still says what they were playing.
        var who = !live
            ? L.F("{0} · last seen {1}", reading.Character, Stale.Age(at, now))
            : reading.Calling is { Length: > 0 } calling
                ? $"{reading.Character} · {calling}"
                : reading.Character;

        if (day is null || art is null)
        {
            var facts = live ? Facts(reading.Line).Select(fact => new HereFact(fact)).ToList() : [];

            return new HerePanel(map, who, facts, live, art, null, reading.Emblem);
        }

        return new HerePanel(map, who, Today(reading, art, day, now), live, art, Meta(day.Meta, now), reading.Emblem);
    }

    /// <summary>The map's meta on now, or the next to start.</summary>
    public static HereMetaLine? Meta(MetaMap? map, DateTimeOffset now)
    {
        if (map is null) return null;

        var next = MetaSchedule.Next(map, now);

        if (MetaSchedule.Current(map, now) is { } on)
        {
            var until = on.End.ToLocalTime();

            // A map that runs one meta over and over - Desert Highlands' Buried Treasure - says when
            // it comes round again, rather than naming the same event twice.
            return new HereMetaLine(on.Name, null, next switch
            {
                { } again when again.Name == on.Name => L.F("until {0:HH:mm} · again at {1:HH:mm}", until, again.Start.ToLocalTime()),
                { } then => L.F("until {0:HH:mm} · then {1}", until, then.Name),
                null => L.F("until {0:HH:mm}", until)
            });
        }

        return next is { } coming
            ? new HereMetaLine(coming.Name, coming.Start, L.F("starts at {0:HH:mm}", coming.Start.ToLocalTime()))
            : null;
    }

    /// <summary>
    /// The map chest, then the map's world bosses by their next appearance. A boss killed today
    /// says so, rather than dropping out, so a map whose bosses are all done does not look like
    /// a map that has none.
    /// </summary>
    private static List<HereFact> Today(HereReading reading, string english, HereDay day, DateTimeOffset now)
    {
        var facts = new List<HereFact>();

        if (day is { Chests: { } chests, ChestsLooted: { } looted } && HereLine.ChestFor(english, chests) is { } chest)
            facts.Add(looted.Contains(chest)
                ? new HereFact(L.T("Map chest looted"), HereTone.Done)
                : new HereFact(L.T("Map chest waiting"), HereTone.Waiting));

        if (reading.MapId > 0)
        {
            var bosses = EventSchedule.NextAppearances(now)
                .Where(boss => BossMaps.For(boss.Location) == reading.MapId)
                .OrderBy(boss => boss.Next);

            foreach (var boss in bosses)
                facts.Add(day.BossesKilled?.Contains(boss.ApiId) == true
                    ? new HereFact(L.F("{0} · done today", GameNames.Boss(boss.Name)), HereTone.Done)
                    : new HereFact(L.F("{0} at {1:HH:mm}", GameNames.Boss(boss.Name), boss.Next.ToLocalTime())));
        }

        return facts.Take(MostLines).ToList();
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
