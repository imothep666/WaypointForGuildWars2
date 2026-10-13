using GuildWarsCompanion.Gw2;

namespace GuildWarsCompanion.Services;

/// <summary>One map as a picker shows it: what it is called, and where it belongs.</summary>
/// <param name="InTheOpenWorld">
/// True for a map somebody walks around in, false for a story chapter, dungeon or fractal. The
/// picker browses the first and searches both — see <see cref="MapPicks.Browsable(MapInfo)"/>.
/// </param>
/// <param name="English">
/// What the map is called in English, when that differs from <paramref name="Name"/>. Guides and
/// the wiki say "Siren's Landing" whatever language the phone reads, so the picker finds and shows
/// both.
/// </param>
public sealed record MapChoice(
    int Id, string Name, string Region, string Continent, bool InTheOpenWorld = true,
    string? English = null)
{
    /// <summary>The English name and where the map belongs, the line under the name.</summary>
    public string Under => English is null ? Where : $"{English} · {Where}";

    /// <summary>"Shiverpeak Mountains · Tyria", the line under the name.</summary>
    public string Where => Continent.Length == 0 ? Region : $"{Region} · {Continent}";
}

/// <summary>
/// Which maps are worth offering by name, and what a typed name matches.
///
/// Pure and separate from <see cref="MapCatalogue"/> for the reason <see cref="TileGeometry"/>
/// is separate from the screen that draws with it: the two decisions here — what counts as a
/// place, and what counts as a match — are the whole behaviour of the picker, and they are
/// testable without a database or a network.
/// </summary>
public static class MapPicks
{
    /// <summary>
    /// The map types worth offering.
    ///
    /// <c>Public</c> is the open world. <c>Instance</c> covers story chapters, dungeons and
    /// fractals, which are places with tiles and landmarks like anywhere else. Everything left
    /// out — <c>Pvp</c>, <c>Tutorial</c>, <c>Center</c>, the three borderland homes,
    /// <c>EdgeOfTheMists</c>, <c>Unknown</c> — is a match or scaffolding.
    /// </summary>
    private static readonly HashSet<string> Drawable =
        new(StringComparer.OrdinalIgnoreCase) { "Public", "Instance" };

    /// <summary>
    /// A map with a footprint, a name, and a type somebody can stand in.
    ///
    /// The type is allowed to be missing: a row stored before this field existed, or an API that
    /// stops publishing one, should leave a map in the list rather than silently empty it. The
    /// footprint is not — a map with no <c>continent_rect</c> cannot be drawn at all.
    ///
    /// The name check is not defensive programming. The live API publishes five maps called
    /// <c>???</c> or nothing at all, and a picker row somebody cannot read is a row they cannot
    /// choose on purpose.
    /// </summary>
    public static bool Offerable(MapInfo map) =>
        map.ContinentArea is not null &&
        Named(map) &&
        (map.Type is null || Drawable.Contains(map.Type));

    /// <summary>
    /// The open world: the 123 maps a player walks around in, against 919 instances.
    ///
    /// This is what the picker shows before anybody types, and the split was made by counting
    /// rather than by taste. Listing everything put "A Bold Strategy: Alliance Camp" at the top
    /// of an alphabetical list of a thousand story steps, which is a list nobody scrolls — while
    /// leaving instances out altogether would make "Ascalonian Catacombs" unfindable. So: browse
    /// the open world, search everything. See <see cref="Search"/>.
    /// </summary>
    public static bool Browsable(MapInfo map) =>
        Offerable(map) && map.Type is null or "Public";

    /// <summary>Everything offerable, as the picker's own rows, sorted by name.</summary>
    /// <param name="english">Every map's English name by id, when the phone reads another
    /// language and the names have been stored. Null or missing ids leave a row with one name.</param>
    public static IReadOnlyList<MapChoice> Choices(
        IEnumerable<MapInfo> maps, IReadOnlyDictionary<int, string>? english = null) =>
        [.. maps
            .Where(Offerable)
            .Select(map => new MapChoice(
                map.Id, map.Name, map.RegionName ?? L.T("Elsewhere"), map.ContinentName ?? string.Empty,
                Browsable(map), EnglishFor(map, english)))
            .OrderBy(choice => choice.Name, StringComparer.CurrentCultureIgnoreCase)];

    /// <summary>The English name only where it says something the name itself does not:
    /// "Draconis Mons" is "Draconis Mons" in German too, and saying it twice is noise.</summary>
    private static string? EnglishFor(MapInfo map, IReadOnlyDictionary<int, string>? english) =>
        english?.GetValueOrDefault(map.Id) is { Length: > 0 } name &&
        !name.Equals(map.Name, StringComparison.OrdinalIgnoreCase)
            ? name
            : null;

    /// <summary>What the picker lists with an empty search box: the open world alone.</summary>
    public static IReadOnlyList<MapChoice> Browsable(IReadOnlyList<MapChoice> all) =>
        [.. all.Where(choice => choice.InTheOpenWorld)];

    private static bool Named(MapInfo map) =>
        !string.IsNullOrWhiteSpace(map.Name) && map.Name.Trim() != "???";

    /// <summary>
    /// Maps whose name contains what was typed, best match first.
    ///
    /// Contains rather than starts-with, because "gorge" is how somebody looks for Frostgorge
    /// Sound and a prefix search answers nothing at all for it. A name that does start with the
    /// term is still the better answer, so it is ordered first.
    ///
    /// An empty term is the open world rather than everything: it is what the box says before
    /// anybody has typed, and a thousand story instances in alphabetical order is not a list
    /// anybody reads. Type one letter and the instances join in.
    ///
    /// The English name counts as much as the phone's own: "Siren" finds Zaubersängerlände,
    /// because that is the name in every guide.
    /// </summary>
    public static IReadOnlyList<MapChoice> Search(IReadOnlyList<MapChoice> all, string term)
    {
        var wanted = term.Trim();

        if (wanted.Length == 0) return Browsable(all);

        bool Has(string? name) => name?.Contains(wanted, StringComparison.CurrentCultureIgnoreCase) == true;
        bool Starts(string? name) => name?.StartsWith(wanted, StringComparison.CurrentCultureIgnoreCase) == true;

        return [.. all
            .Where(choice => Has(choice.Name) || Has(choice.English))
            .OrderByDescending(choice => Starts(choice.Name) || Starts(choice.English))
            .ThenBy(choice => choice.Name, StringComparer.CurrentCultureIgnoreCase)];
    }
}
