using GuildWarsCompanion.Services;
using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// When a following map has to move.
///
/// The rule exists so that walking does not slide the map continuously under the eye: the bridge
/// reports ten times a second, and a map centred on every report never stands still.
/// </summary>
public class FollowBoxTests
{
    private const double Width = 1080;
    private const double Height = 2000;

    [Fact]
    public void Stays_put_while_the_character_is_in_the_middle()
    {
        Assert.False(FollowBox.Escaped(Width / 2, Height / 2, Width, Height));
    }

    /// <summary>Half the viewport, so a quarter of a side either way from the middle.</summary>
    [Theory]
    [InlineData(0.26, 0.5)]
    [InlineData(0.74, 0.5)]
    [InlineData(0.5, 0.26)]
    [InlineData(0.5, 0.74)]
    public void Stays_put_just_inside_the_box(double alongWidth, double alongHeight)
    {
        Assert.False(FollowBox.Escaped(
            Width * alongWidth, Height * alongHeight, Width, Height));
    }

    [Theory]
    [InlineData(0.24, 0.5)]
    [InlineData(0.76, 0.5)]
    [InlineData(0.5, 0.24)]
    [InlineData(0.5, 0.76)]
    public void Follows_once_the_character_leaves_the_box(double alongWidth, double alongHeight)
    {
        Assert.True(FollowBox.Escaped(
            Width * alongWidth, Height * alongHeight, Width, Height));
    }

    /// <summary>
    /// A character who has walked off the screen entirely — which is what happens on the map the
    /// screen was left on while somebody ran somewhere else.
    /// </summary>
    [Fact]
    public void Follows_a_character_who_is_off_the_screen()
    {
        Assert.True(FollowBox.Escaped(-400, Height / 2, Width, Height));
        Assert.True(FollowBox.Escaped(Width + 400, Height / 2, Width, Height));
    }

    /// <summary>
    /// The box is a share of each side, so a sideways phone gets a box the shape of the sideways
    /// viewport rather than a portrait one rotated into it.
    /// </summary>
    [Fact]
    public void Measures_the_box_against_the_viewport_it_is_given()
    {
        // 800 across: inside the box up to 200 either side of the middle.
        Assert.False(FollowBox.Escaped(599, 300, 800, 600));
        Assert.True(FollowBox.Escaped(601, 300, 800, 600));
    }
}
