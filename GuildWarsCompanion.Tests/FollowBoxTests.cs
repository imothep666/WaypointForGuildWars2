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

    [Theory]
    [InlineData(4, 4, 0.3)]
    [InlineData(3, 4, 0.4)]
    [InlineData(2, 4, FollowBox.Share)]
    [InlineData(1, 4, FollowBox.Share)]
    [InlineData(1, 1, 0.3)]
    public void TheBoxTightensAtTheTopTwoLevels(int level, int levels, double share) =>
        Assert.Equal(share, FollowBox.ShareAt(level, levels));

    [Fact]
    public void ATighterBoxLetsGoSooner()
    {
        // A quarter of the width off the middle: inside the half box, outside the 0.3 one.
        var x = Width / 2 + Width * 0.2;

        Assert.False(FollowBox.Escaped(x, Height / 2, Width, Height));
        Assert.True(FollowBox.Escaped(x, Height / 2, Width, Height, FollowBox.ShareAt(4, 4)));
    }

    [Fact]
    public void AQuickGlideClosesMoreOfTheDistanceInTheSameTime()
    {
        var slow = new MapMotion();
        var quick = new MapMotion();

        slow.GlideTo(100, 0);
        quick.GlideTo(100, 0, MapMotion.GlideSecondsAt(4, 4));

        var (slowX, _) = slow.Advance(0.1, 0, 0, 0.01);
        var (quickX, _) = quick.Advance(0.1, 0, 0, 0.01);

        Assert.True(quickX > slowX);
        Assert.InRange(MapMotion.GlideSecondsAt(4, 4), 0.01, MapMotion.GlideSeconds);
    }
}
