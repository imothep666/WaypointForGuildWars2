using GuildWarsCompanion.Services;
using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// What colour a pack asks for.
///
/// Every value here is one the two real packs carry: Metal Marker Myriad's Okabe-Ito hexes on 251
/// trails and its one malformed <c>#fffffff</c>, and Teh's Trails' category alphas.
/// </summary>
public class RouteColourTests
{
    private static IReadOnlyDictionary<string, string> Map(params (string Key, string Value)[] pairs) =>
        pairs.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyDictionary<string, string> None() => Map();

    [Fact]
    public void Reads_the_hex_a_pack_writes()
    {
        Assert.Equal(unchecked((int)0xFF009E73), RouteColour.Parse("#009E73"));
        Assert.Equal(unchecked((int)0xFFCC79A7), RouteColour.Parse("#cc79a7"));
    }

    [Fact]
    public void Reads_a_hex_without_its_hash()
    {
        Assert.Equal(unchecked((int)0xFFF0E442), RouteColour.Parse("f0e442"));
    }

    [Fact]
    public void Reads_eight_digits_as_carrying_their_own_alpha()
    {
        Assert.Equal(unchecked((int)0x80FF0000), RouteColour.Parse("#80FF0000"));
    }

    /// <summary>
    /// Metal Marker Myriad carries <c>#fffffff</c> — seven digits, which is not a colour in any
    /// reading. Treated as unstated, because the route is still worth drawing.
    /// </summary>
    [Theory]
    [InlineData("#fffffff")]
    [InlineData("")]
    [InlineData("red")]
    [InlineData("#gggggg")]
    [InlineData(null)]
    public void Answers_nothing_for_a_value_that_is_not_a_colour(string? text)
    {
        Assert.Null(RouteColour.Parse(text));
    }

    [Fact]
    public void Reads_the_alphas_a_pack_writes()
    {
        Assert.Equal(1.0, RouteColour.Alpha("1"));
        Assert.Equal(0.7, RouteColour.Alpha("0.7"));
    }

    /// <summary>A pack writing 255 means something this reader does not understand, and guessing
    /// at it would be inventing a value the author did not write.</summary>
    [Theory]
    [InlineData("255")]
    [InlineData("-1")]
    [InlineData("opaque")]
    public void Refuses_an_alpha_it_cannot_honour(string text)
    {
        Assert.Null(RouteColour.Alpha(text));
    }

    // --- resolving ---------------------------------------------------------

    [Fact]
    public void Says_nothing_when_the_pack_says_nothing()
    {
        Assert.Equal(RouteColour.None, RouteColour.Resolve(null, "tt.mc.mt", None(), None()));
    }

    [Fact]
    public void Takes_the_trails_own_colour_first()
    {
        var argb = RouteColour.Resolve("#009E73", "mm.a.b", Map(("b", "#CC79A7")), None());

        Assert.Equal(unchecked((int)0xFF009E73), argb);
    }

    /// <summary>
    /// A pack's attributes inherit down its category tree, and a type path carries its ancestry —
    /// so the nearest category that states a colour is the one that applies.
    /// </summary>
    [Fact]
    public void Falls_back_to_the_nearest_category_that_states_one()
    {
        var argb = RouteColour.Resolve(
            null, "mm.outer.inner", Map(("outer", "#009E73"), ("inner", "#CC79A7")), None());

        Assert.Equal(unchecked((int)0xFFCC79A7), argb);
    }

    [Fact]
    public void Reaches_further_up_when_the_nearest_says_nothing()
    {
        var argb = RouteColour.Resolve(null, "mm.outer.inner", Map(("outer", "#009E73")), None());

        Assert.Equal(unchecked((int)0xFF009E73), argb);
    }

    /// <summary>
    /// Teh's Trails fades a category to 0.7 and never says what colour it is, which still means
    /// something: the app's own brass, faded.
    /// </summary>
    [Fact]
    public void Applies_a_stated_alpha_to_the_apps_own_colour()
    {
        var argb = RouteColour.Resolve(null, "tt.mc.mt", None(), Map(("mt", "0.7")));

        Assert.Equal(0xB2, (argb >> 24) & 0xFF);
        Assert.Equal(0xC9A227, argb & 0x00FFFFFF);
    }

    [Fact]
    public void Applies_a_stated_alpha_to_a_stated_colour()
    {
        var argb = RouteColour.Resolve("#009E73", "tt.mc.mt", None(), Map(("mc", "0.5")));

        Assert.Equal(0x7F, (argb >> 24) & 0xFF);
        Assert.Equal(0x009E73, argb & 0x00FFFFFF);
    }

    [Fact]
    public void Ignores_a_category_that_is_not_on_this_path()
    {
        Assert.Equal(
            RouteColour.None,
            RouteColour.Resolve(null, "tt.mc.mt", Map(("elsewhere", "#009E73")), None()));
    }
}
