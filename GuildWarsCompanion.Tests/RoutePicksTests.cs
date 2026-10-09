using GuildWarsCompanion.Services;
using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// Which of a pack's routes get drawn.
///
/// The rule that matters is the default: nothing. A pack carries dozens of routes per zone —
/// Teh's Trails files main trails, highlight trails and hero point trails separately — and all of
/// them at once is a thicket over the artwork the map exists to show.
/// </summary>
public class RoutePicksTests
{
    private static RouteLine Line(string type, string name) =>
        new(15, type, name, [(0, 0), (10, 10)]);

    private static IReadOnlySet<string> Chosen(params string[] types) =>
        new HashSet<string>(types, StringComparer.Ordinal);

    [Fact]
    public void Draws_nothing_until_a_category_is_chosen()
    {
        var routes = new[] { Line("tt.mc.mt", "Main Trail"), Line("tt.mc.hp", "Hero Points") };

        Assert.Empty(RoutePicks.Shown(routes, Chosen()));
    }

    [Fact]
    public void Draws_only_the_categories_chosen()
    {
        var routes = new[]
        {
            Line("tt.mc.mt", "Main Trail"),
            Line("tt.mc.mt", "Main Trail"),
            Line("tt.mc.hp", "Hero Points")
        };

        var shown = RoutePicks.Shown(routes, Chosen("tt.mc.mt"));

        Assert.Equal(2, shown.Count);
        Assert.All(shown, route => Assert.Equal("tt.mc.mt", route.Type));
    }

    /// <summary>
    /// The chooser counts runs, because that is what a map draws — one category can be fifty
    /// separate lines and saying "1" about it would be wrong about the work.
    /// </summary>
    [Fact]
    public void Counts_the_runs_in_each_category()
    {
        var routes = new[]
        {
            Line("tt.mc.mt", "Main Trail"),
            Line("tt.mc.mt", "Main Trail"),
            Line("tt.mc.hp", "Hero Points")
        };

        var options = RoutePicks.Options(routes, Chosen());

        Assert.Equal(2, options.Count);
        Assert.Equal(2, options.Single(o => o.Type == "tt.mc.mt").Lines);
        Assert.Equal(1, options.Single(o => o.Type == "tt.mc.hp").Lines);
    }

    [Fact]
    public void Marks_the_categories_that_are_on()
    {
        var options = RoutePicks.Options(
            [Line("tt.mc.mt", "Main Trail"), Line("tt.mc.hp", "Hero Points")],
            Chosen("tt.mc.hp"));

        Assert.True(options.Single(o => o.Type == "tt.mc.hp").Shown);
        Assert.False(options.Single(o => o.Type == "tt.mc.mt").Shown);
    }

    /// <summary>
    /// Only what this map has. Listing every category in the pack would offer twenty rows on a
    /// map with routes for two, and choosing one would change nothing on screen.
    /// </summary>
    [Fact]
    public void Offers_nothing_for_a_map_with_no_routes()
    {
        Assert.Empty(RoutePicks.Options([], Chosen("tt.mc.mt")));
    }

    [Fact]
    public void Sorts_the_rows_by_name()
    {
        var options = RoutePicks.Options(
            [Line("b", "Zephyr"), Line("a", "Ascalon"), Line("c", "Metrica")], Chosen());

        Assert.Equal(["Ascalon", "Metrica", "Zephyr"], options.Select(o => o.Name));
    }

    [Fact]
    public void Turns_a_category_on_and_off_again()
    {
        var once = RoutePicks.Toggle(Chosen(), "tt.mc.mt");

        Assert.Contains("tt.mc.mt", once);

        Assert.DoesNotContain("tt.mc.mt", RoutePicks.Toggle(once, "tt.mc.mt"));
    }

    /// <summary>A set that changes underneath a screen drawing from it goes wrong occasionally,
    /// which is the worst kind.</summary>
    [Fact]
    public void Leaves_the_set_it_was_given_alone()
    {
        var before = Chosen("tt.mc.hp");

        RoutePicks.Toggle(before, "tt.mc.mt");

        Assert.Single(before);
    }
}
