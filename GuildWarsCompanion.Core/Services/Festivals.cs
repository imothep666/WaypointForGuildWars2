namespace GuildWarsCompanion.Services;

/// <summary>
/// One of the game's yearly festivals.
/// </summary>
/// <param name="Key">The app's own name for it, which a calendar entry refers to.</param>
/// <param name="English">Its English name, as the wiki and the guides call it.</param>
/// <param name="TimerKey">Its entry in the wiki's event timer data, for those that have one.</param>
/// <param name="Categories">The achievement categories it brings back each year.</param>
public sealed record Festival(string Key, string English, string? TimerKey, IReadOnlyList<int> Categories)
{
    /// <summary>In the phone's language.</summary>
    public string Name => L.T(English);
}

/// <summary>A festival, and when it runs this time.</summary>
public sealed record FestivalPeriod(Festival Festival, DateTimeOffset Start, DateTimeOffset End)
{
    public bool IsOn(DateTimeOffset utcNow) => utcNow >= Start && utcNow < End;

    /// <summary>"until Tuesday 3 November", the last day it runs.</summary>
    public string Until => L.F("until {0:dddd d MMMM}", End.ToLocalTime().AddSeconds(-1));
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
    /// names, read 2026-10-29. Lunar New Year and Wintersday have no timer entry.
    /// </summary>
    public static readonly IReadOnlyList<Festival> All =
    [
        new("lunar", "Lunar New Year", null, [202]),
        new("sab", "Super Adventure Festival", null, [205, 351]),
        new("dragonbash", "Dragon Bash", "festival-db", [231, 232]),
        new("fourwinds", "Festival of the Four Winds", "festival-lc", [210, 212]),
        new("halloween", "Halloween", "festival-ha", [191, 193]),
        new("wintersday", "Wintersday", null, [197])
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

    /// <summary>The festivals on at <paramref name="utcNow"/>.</summary>
    public static IReadOnlyList<FestivalPeriod> On(IEnumerable<FestivalPeriod> periods, DateTimeOffset utcNow) =>
        periods.Where(period => period.IsOn(utcNow)).ToList();
}
