using GuildWarsCompanion.Services;

using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// A bearing as a point of the compass, for the map screen.
///
/// The dial says "bear left" because it is read while walking and the target cannot be seen; the
/// map is north-up with the marker on it, so it says "north-east". Same arithmetic underneath —
/// what is pinned here is that the names line up with the continent's axes, which grow east and
/// <b>south</b>, the one convention in this app that catches people out.
/// </summary>
public class CardinalTests
{
    private static string Way(double fromX, double fromY, double toX, double toY) =>
        Bearing.Cardinal(Bearing.To(fromX, fromY, toX, toY));

    [Fact]
    public void The_four_quarters_are_named_from_the_continents_axes()
    {
        Assert.Equal("north", Way(1000, 1000, 1000, 500));
        Assert.Equal("east", Way(1000, 1000, 1500, 1000));
        Assert.Equal("south", Way(1000, 1000, 1000, 1500));
        Assert.Equal("west", Way(1000, 1000, 500, 1000));
    }

    [Fact]
    public void The_corners_are_named_too()
    {
        Assert.Equal("north-east", Way(1000, 1000, 1500, 500));
        Assert.Equal("south-east", Way(1000, 1000, 1500, 1500));
        Assert.Equal("south-west", Way(1000, 1000, 500, 1500));
        Assert.Equal("north-west", Way(1000, 1000, 500, 500));
    }

    /// <summary>Each name covers its own forty-five degrees, centred on the direction rather than
    /// starting at it — so a hair either side of due north is still north.</summary>
    [Theory]
    [InlineData(-22, "north")]
    [InlineData(22, "north")]
    [InlineData(-23, "north-west")]
    [InlineData(23, "north-east")]
    public void A_name_is_centred_on_its_own_direction(double degrees, string expected)
    {
        Assert.Equal(expected, Bearing.Cardinal(degrees * Math.PI / 180));
    }

    /// <summary>Due south arrives as either sign depending on which way the angle was folded, and
    /// both are south.</summary>
    [Fact]
    public void Half_a_turn_is_south_whichever_way_it_was_reached()
    {
        Assert.Equal("south", Bearing.Cardinal(Math.PI));
        Assert.Equal("south", Bearing.Cardinal(-Math.PI));
    }

    /// <summary>An angle that has wrapped round more than once is still a direction.</summary>
    [Fact]
    public void A_wrapped_angle_still_has_a_name()
    {
        Assert.Equal("east", Bearing.Cardinal(Math.PI / 2 + Math.Tau));
        Assert.Equal("west", Bearing.Cardinal(-Math.PI / 2 - Math.Tau));
    }
}
