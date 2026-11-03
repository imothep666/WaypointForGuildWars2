using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

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
/// data, the categories are the API's — and when it runs is on the festival's wiki page, which
/// lists every year's run by its first and last day once ArenaNet has announced it.
/// <see cref="FestivalSource"/> reads those pages; <see cref="Shipped"/> is what the app was built with.
/// </summary>
public static partial class Festivals
{
    /// <summary>
    /// The timer keys are the wiki's (festival-ha, -db, -lc); the category ids are /v2's, by their
    /// names, read 2026-10-29. Lunar New Year and Wintersday have no timer entry. The seasons are
    /// the pattern of the last five years on the wiki, not a promise: Lunar New Year follows the
    /// lunar calendar, and a festival has skipped a year before. The English names are the titles
    /// of the festivals' wiki pages.
    /// </summary>
    public static readonly IReadOnlyList<Festival> All =
    [
        new("lunar", "Lunar New Year", null, [202], "usually late January to mid-February", (1, 25)),
        new("sab", "Super Adventure Festival", null, [205, 351], "usually in April", (4, 1)),
        new("dragonbash", "Dragon Bash", "festival-db", [231, 232], "usually in June", (6, 5)),
        new("fourwinds", "Festival of the Four Winds", "festival-lc", [210, 212], "usually July into August", (7, 25)),
        new("halloween", "Halloween", "festival-ha", [191, 193], "usually mid-October to early November", (10, 13)),
        new("wintersday", "Wintersday", null, [197], "usually mid-December to early January", (12, 9))
    ];

    public static Festival? ByKey(string key) => All.FirstOrDefault(festival => festival.Key == key);

    /// <summary>
    /// When festivals run as the app was built, from the wiki on 2026-11-03: the one on and the one
    /// announced. Used until the wiki's pages have been read once, and for a festival whose page
    /// no longer lists the year.
    /// </summary>
    public static readonly IReadOnlyList<FestivalPeriod> Shipped =
    [
        Run(ByKey("halloween")!, new DateOnly(2026, 10, 13), new DateOnly(2026, 11, 3)),
        Run(ByKey("wintersday")!, new DateOnly(2026, 12, 8), new DateOnly(2027, 1, 5))
    ];

    /// <summary>
    /// The wiki gives days, not hours: a festival is taken to run from the start of its first day
    /// to the end of its last, in Central European time, the game's European servers' own. That
    /// shows it a few hours after the patch that ends it, never before the one that starts it.
    /// </summary>
    public static FestivalPeriod Run(Festival festival, DateOnly first, DateOnly last) =>
        new(festival, Midnight(first), Midnight(last.AddDays(1)));

    private static readonly TimeZoneInfo? Central = CentralEurope();

    private static TimeZoneInfo? CentralEurope()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return null;
        }
    }

    private static DateTimeOffset Midnight(DateOnly day)
    {
        var local = day.ToDateTime(TimeOnly.MinValue);

        return new DateTimeOffset(local, Central?.GetUtcOffset(local) ?? TimeSpan.FromHours(1));
    }

    /// <summary>"* [[A Very Merry Wintersday 2026]] &lt;small&gt;(2026-12-08 – 2027-01-05)&lt;/small&gt;"</summary>
    [GeneratedRegex(@"<small>\(\s*(\d{4}-\d{2}-\d{2})\s*[^\d)<]{1,3}\s*(\d{4}-\d{2}-\d{2})\s*\)</small>")]
    private static partial Regex Edition();

    /// <summary>
    /// Every run the festivals' wiki pages list, from the wiki API's answer for their wikitext.
    /// A page that cannot be found or lists nothing gives nothing; when none gives anything the
    /// answer is not what was asked for, and that throws rather than say no festival ever runs.
    /// </summary>
    public static IReadOnlyList<FestivalPeriod> ParseWiki(string json)
    {
        using var document = JsonDocument.Parse(json);

        var periods = new List<FestivalPeriod>();

        foreach (var page in document.RootElement.GetProperty("query").GetProperty("pages").EnumerateArray())
        {
            var title = page.TryGetProperty("title", out var name) ? name.GetString() : null;

            if (All.FirstOrDefault(festival => festival.English == title) is not { } festival) continue;
            if (!page.TryGetProperty("revisions", out var revisions) || revisions.GetArrayLength() == 0) continue;

            var text = revisions[0].GetProperty("slots").GetProperty("main").GetProperty("content").GetString() ?? string.Empty;

            foreach (Match match in Edition().Matches(text))
            {
                if (Day(match.Groups[1].Value) is { } first && Day(match.Groups[2].Value) is { } last && last >= first)
                    periods.Add(Run(festival, first, last));
            }
        }

        if (periods.Count == 0)
            throw new InvalidOperationException("The festivals' wiki pages list no runs.");

        return periods;
    }

    private static DateOnly? Day(string text) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
            ? day
            : null;

    /// <summary>
    /// The wiki's runs, and a shipped one only for a festival and year the wiki does not list:
    /// the wiki is the later word, and may have moved a day.
    /// </summary>
    public static IReadOnlyList<FestivalPeriod> Merge(IReadOnlyList<FestivalPeriod> wiki, IReadOnlyList<FestivalPeriod> shipped) =>
    [
        .. wiki,
        .. shipped.Where(own => !wiki.Any(period =>
            period.Festival.Key == own.Festival.Key && period.Start.Year == own.Start.Year))
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
