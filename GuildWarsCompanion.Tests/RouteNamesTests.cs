using GuildWarsCompanion.Services;
using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// Writing a pack's dotted category path out in the author's own words.
///
/// The fixture is Teh's Trails as it actually reads: <c>tt.mc.mt.th.t</c>, with a separator
/// category displayed as <c>     |=====Core Tyria=====|     </c>.
/// </summary>
public class RouteNamesTests
{
    private static IReadOnlyDictionary<string, string> Tehs() =>
        RouteNames.Lookup([
            new PackCategory("tt", "Teh's Trails - Map Completion", false),
            new PackCategory("mapcomp", "     |=====Core Tyria=====|     ", true),
            new PackCategory("mc", "Map Completion", false),
            new PackCategory("mt", "Main Trail", false),
            new PackCategory("th", "Trail Highlighting", false)
        ]);

    /// <summary>
    /// The tail, because every row of a pack shares the branch above it: written in full, six
    /// categories of Teh's Trails differed only after "Teh's Trails - Map Completion · Map
    /// Completion ·", which is exactly where a phone truncates the line.
    /// </summary>
    [Fact]
    public void Says_the_end_of_a_path_in_the_authors_words()
    {
        Assert.Equal("Map Completion · Main Trail", RouteNames.Describe("tt.mc.mt", Tehs()));
    }

    [Fact]
    public void Says_the_whole_path_when_asked_for_it()
    {
        Assert.Equal(
            "Teh's Trails - Map Completion · Map Completion · Main Trail",
            RouteNames.Describe("tt.mc.mt", Tehs(), deep: 9));
    }

    [Fact]
    public void Says_a_short_path_whole()
    {
        Assert.Equal("Main Trail", RouteNames.Describe("mt", Tehs()));
    }

    /// <summary>A segment the pack never named keeps its own name rather than vanishing: an
    /// author's omission should look like one.</summary>
    [Fact]
    public void Keeps_an_unnamed_segment_as_it_is()
    {
        Assert.Equal("Main Trail · t1", RouteNames.Describe("mt.t1", Tehs()));
    }

    [Fact]
    public void Leaves_separators_out_of_the_lookup()
    {
        Assert.DoesNotContain("mapcomp", Tehs().Keys);
    }

    [Fact]
    public void Trims_the_padding_authors_put_around_a_name()
    {
        var names = RouteNames.Lookup([new PackCategory("x", "   Spaced   ", false)]);

        Assert.Equal("Spaced", names["x"]);
    }

    /// <summary>Packs reuse short segment names under different parents. First wins, which is at
    /// least stable between runs.</summary>
    [Fact]
    public void Keeps_the_first_meaning_of_a_repeated_segment()
    {
        var names = RouteNames.Lookup([
            new PackCategory("t", "Trail", false),
            new PackCategory("t", "Something else", false)
        ]);

        Assert.Equal("Trail", names["t"]);
    }

    [Fact]
    public void Has_words_for_a_trail_filed_under_nothing()
    {
        Assert.Equal("Unfiled route", RouteNames.Describe(string.Empty, Tehs()));
        Assert.Equal("Unfiled route", RouteNames.Describe("...", Tehs()));
    }

    // --- telling two rows apart ---------------------------------------------

    /// <summary>
    /// Found on the phone: two branches ending in the same two segments gave the chooser two rows
    /// reading "Main Trail · Normal Trail (Disable if using trail highlighting)", 16 runs and 12.
    /// </summary>
    [Fact]
    public void Lengthens_only_the_names_that_would_read_the_same()
    {
        var names = RouteNames.Lookup([
            new PackCategory("mm", "Metal", false),
            new PackCategory("core", "Core Tyria", false),
            new PackCategory("hot", "Heart of Thorns", false),
            new PackCategory("mt", "Main Trail", false),
            new PackCategory("nt", "Normal Trail", false),
            new PackCategory("ch", "Chests", false),
            new PackCategory("arcane", "Arcane Chest", false)
        ]);

        var said = RouteNames.Distinguish(["mm.core.mt.nt", "mm.hot.mt.nt", "mm.ch.arcane"], names);

        Assert.NotEqual(said["mm.core.mt.nt"], said["mm.hot.mt.nt"]);
        Assert.Contains("Core Tyria", said["mm.core.mt.nt"]);
        Assert.Contains("Heart of Thorns", said["mm.hot.mt.nt"]);

        // The one that never clashed keeps the short form.
        Assert.Equal("Chests · Arcane Chest", said["mm.ch.arcane"]);
    }

    [Fact]
    public void Leaves_a_set_with_no_clashes_alone()
    {
        var said = RouteNames.Distinguish(["mt.nt", "mt.th"], Tehs());

        Assert.Equal("Main Trail · nt", said["mt.nt"]);
        Assert.Equal("Main Trail · Trail Highlighting", said["mt.th"]);
    }

    /// <summary>
    /// Two paths that really are the same category share one name, which is what the chooser's
    /// grouping expects — lengthening them would split one row into two saying the same thing.
    /// </summary>
    [Fact]
    public void Keeps_one_name_for_one_path()
    {
        var said = RouteNames.Distinguish(["mt.th", "mt.th"], Tehs());

        Assert.Single(said);
    }
}
