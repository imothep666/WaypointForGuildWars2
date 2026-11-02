namespace GuildWarsCompanion.Services;

/// <summary>
/// One of the game's yearly festivals.
/// </summary>
/// <param name="Key">The app's own name for it, which a calendar entry refers to.</param>
/// <param name="English">Its English name, as the wiki and the guides call it.</param>
/// <param name="TimerKey">Its entry in the wiki's event timer data, for those that have one.</param>
/// <param name="Categories">The achievement categories it brings back each year.</param>
/// <param name="Season">When it has run in past years, in English: "usually mid-December to early January".</param>
/// <param name="Usually">The month and day it has usually started on, which orders the calendar.</param>
public sealed record Festival(
    string Key, string English, string? TimerKey, IReadOnlyList<int> Categories, string Season, (int Month, int Day) Usually)
{
    /// <summary>In the phone's language.</summary>
    public string Name => L.T(English);

    /// <summary>The day it usually starts next, from <paramref name="today"/> on: this year's or next year's.</summary>
    public DateTime UsualStartAfter(DateTime today)
    {
        var start = new DateTime(today.Year, Usually.Month, Usually.Day);

        return start < today.Date ? start.AddYears(1) : start;
    }
}

/// <summary>A festival, and when it runs this time.</summary>
public sealed record FestivalPeriod(Festival Festival, DateTimeOffset Start, DateTimeOffset End)
{
    public bool IsOn(DateTimeOffset utcNow) => utcNow >= Start && utcNow < End;

    /// <summary>"until Tuesday 3 November", the last day it runs.</summary>
    public string Until => L.F("until {0:dddd d MMMM}", End.ToLocalTime().AddSeconds(-1));

    /// <summary>"13 October to 3 November", its first and last day.</summary>
    public string Span => L.F("{0:d MMMM} to {1:d MMMM}", Start.ToLocalTime(), End.ToLocalTime().AddSeconds(-1));
}

/// <summary>A festival on the calendar, and when it runs as far as that is known.</summary>
/// <param name="When">"until Tuesday 3 November", "13 October to 3 November", or its usual season.</param>
/// <param name="Note">"on now", "in 12 days", or "not announced yet".</param>
/// <param name="Next">When it starts, or usually starts, next: what the calendar is ordered by.</param>
public sealed record FestivalEntry(Festival Festival, string When, string Note, bool IsOn, bool IsAnnounced, DateTime Next)
{
    public string Name => Festival.Name;
}

/// <summary>
/// The festivals the app knows, and when they run.
///
/// The API says nothing about either: there is no flag for a running festival, and every festival
/// daily category still lists its achievements years after the dailies ended (checked 2026-10-29),
/// so none of them is a sign. What a festival brings is known — the timer entries are in the wiki's
/// data, the categories are the API's — but when it runs comes from ArenaNet's announcement.
/// </summary>
public static class Festivals
{
    /// <summary>
    /// The timer keys are the wiki's (festival-ha, -db, -lc); the category ids are /v2's, by their
    /// names, read 2026-10-29. Lunar New Year and Wintersday have no timer entry. The seasons are
    /// the pattern of past years, not a promise: Lunar New Year follows the lunar calendar, and a
    /// festival has skipped a year before.
    /// </summary>
    public static readonly IReadOnlyList<Festival> All =
    [
        new("lunar", "Lunar New Year", null, [202], "usually late January to mid-February", (1, 25)),
        new("sab", "Super Adventure Festival", null, [205, 351], "usually in April", (4, 1)),
        new("dragonbash", "Dragon Bash", "festival-db", [231, 232], "usually June into July", (6, 20)),
        new("fourwinds", "Festival of the Four Winds", "festival-lc", [210, 212], "usually July into August", (7, 25)),
        new("halloween", "Halloween", "festival-ha", [191, 193], "usually mid-October to early November", (10, 13)),
        new("wintersday", "Wintersday", null, [197], "usually mid-December to early January", (12, 12))
    ];

    public static Festival? ByKey(string key) => All.FirstOrDefault(festival => festival.Key == key);

    /// <summary>
    /// When festivals run, as ArenaNet announced them. The announcements give days, not hours:
    /// a festival is taken to run from the start of its first day to the end of its last, in
    /// Central European time, which the German press gave. Read from the wiki once it can be;
    /// until then this list is what there is, and a festival not on it is not shown.
    /// </summary>
    public static readonly IReadOnlyList<FestivalPeriod> Announced =
    [
        // Shadow of the Mad King 2026: 13 October to 3 November (MassivelyOP, jeu.video).
        new(ByKey("halloween")!,
            new DateTimeOffset(2026, 10, 13, 0, 0, 0, TimeSpan.FromHours(2)),
            new DateTimeOffset(2026, 11, 4, 0, 0, 0, TimeSpan.FromHours(1)))
    ];

    /// <summary>
    /// Every festival, the ones on first and then by when each starts next: its announced dates
    /// when there are some, otherwise when it usually starts, said as a season and marked so.
    /// </summary>
    public static IReadOnlyList<FestivalEntry> Calendar(IEnumerable<FestivalPeriod> periods, DateTimeOffset utcNow)
    {
        var known = periods.ToList();
        var today = utcNow.ToLocalTime().Date;

        return All
            .Select(festival =>
            {
                var own = known.Where(period => period.Festival.Key == festival.Key).OrderBy(period => period.Start).ToList();

                if (own.FirstOrDefault(period => period.IsOn(utcNow)) is { } on)
                    return new FestivalEntry(festival, on.Until, L.T("on now"), true, true, today);

                if (own.FirstOrDefault(period => period.Start > utcNow) is { } coming)
                {
                    var start = coming.Start.ToLocalTime().Date;
                    var days = (start - today).Days;

                    return new FestivalEntry(festival, coming.Span,
                        days switch
                        {
                            <= 0 => L.T("starts today"),
                            1 => L.T("starts tomorrow"),
                            _ => L.F("in {0} days", days)
                        }, false, true, start);
                }

                return new FestivalEntry(festival, L.T(festival.Season), L.T("not announced yet"), false, false,
                    festival.UsualStartAfter(today));
            })
            .OrderByDescending(entry => entry.IsOn)
            .ThenBy(entry => entry.Next)
            .ToList();
    }

    /// <summary>The festivals on at <paramref name="utcNow"/>.</summary>
    public static IReadOnlyList<FestivalPeriod> On(IEnumerable<FestivalPeriod> periods, DateTimeOffset utcNow) =>
        periods.Where(period => period.IsOn(utcNow)).ToList();
}
