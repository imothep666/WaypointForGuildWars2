namespace GuildWarsCompanion.Services;

/// <summary>One row of the route chooser: a category, what it is called, and how much of it is
/// on this map.</summary>
public sealed record RouteChoice(string Type, string Name, int Lines, bool Shown)
{
    /// <summary>
    /// The inverse of <see cref="Shown"/>, for the row's own drawing.
    ///
    /// An inverted boolean rather than a converter in the view, for the reason
    /// <c>FindViewModel.NotLoaded</c> exists: a converter in XAML forces a reflection binding
    /// the trimmer cannot follow.
    /// </summary>
    public bool Hidden => !Shown;
}

/// <summary>Where the chosen categories are kept between runs.</summary>
public interface IRouteChoiceStore
{
    IReadOnlySet<string> Read();

    void Write(IReadOnlySet<string> chosen);
}

/// <summary>
/// Which of a pack's routes are drawn.
///
/// <b>Off until asked for, which is the opposite of the markers.</b> A marker is one symbol at one
/// point and showing every one is what a map is for. A route covers the whole map: Teh's Trails
/// files 22 categories — main trails, highlight trails, hero point trails, heart trails — and
/// several of them cover the same zone, so drawing them all at once is a thicket with the
/// artwork underneath it. Nothing is drawn until a category is chosen, and the choice is
/// remembered, so the cost of having a pack installed is zero until somebody wants a route.
///
/// <b>Chosen by category rather than by route.</b> A pack has 1334 individual runs and 22
/// categories; the categories are what the author grouped by and what a person can actually read
/// off a list.
/// </summary>
public static class RoutePicks
{
    /// <summary>
    /// The chooser's rows for one map: every category with a route on it, named, counted, and
    /// marked with whether it is on.
    ///
    /// Only what this map has. A list of every category in the pack would offer twenty rows on a
    /// map that has routes for two of them, and choosing one would change nothing on screen.
    /// </summary>
    public static IReadOnlyList<RouteChoice> Options(
        IEnumerable<RouteLine> routes, IReadOnlySet<string> chosen)
    {
        var counts = new Dictionary<string, (string Name, int Lines)>(StringComparer.Ordinal);

        foreach (var route in routes)
        {
            var (name, lines) = counts.GetValueOrDefault(route.Type, (route.Name, 0));

            counts[route.Type] = (name, lines + 1);
        }

        return [.. counts
            .Select(entry => new RouteChoice(
                entry.Key, entry.Value.Name, entry.Value.Lines, chosen.Contains(entry.Key)))
            .OrderBy(choice => choice.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    /// <summary>The routes to draw.</summary>
    public static IReadOnlyList<RouteLine> Shown(
        IEnumerable<RouteLine> routes, IReadOnlySet<string> chosen) =>
        chosen.Count == 0 ? [] : [.. routes.Where(route => chosen.Contains(route.Type))];

    /// <summary>
    /// Turns one category on or off.
    ///
    /// Returns a new set rather than mutating: the caller holds it as state, and a set that
    /// changes underneath a screen that is drawing from it is the sort of thing that only goes
    /// wrong occasionally.
    /// </summary>
    public static IReadOnlySet<string> Toggle(IReadOnlySet<string> chosen, string type)
    {
        var next = new HashSet<string>(chosen, StringComparer.Ordinal);

        if (!next.Add(type)) next.Remove(type);

        return next;
    }
}
