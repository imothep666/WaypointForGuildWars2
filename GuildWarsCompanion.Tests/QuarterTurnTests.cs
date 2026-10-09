using GuildWarsCompanion.Services;

using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// The quarter turn the map screen is drawn through.
///
/// Worth its own tests because two pieces of code have to agree about it exactly and neither can
/// see the other: <c>MapCanvas</c> turns the canvas one way and <c>MapViewModel</c> turns a tap
/// the other. When they disagree the map looks perfect and picks the wrong marker, which is not a
/// failure anybody notices until they are already confused.
///
/// A 1000 x 2000 screen throughout, so portrait and landscape cannot be confused for each other.
/// </summary>
public class QuarterTurnTests
{
    private const double Width = 1000;
    private const double Height = 2000;

    [Fact]
    public void Upright_and_upside_down_keep_the_screen_shape()
    {
        Assert.Equal((Width, Height), QuarterTurn.Extent(0, Width, Height));
        Assert.Equal((Width, Height), QuarterTurn.Extent(2, Width, Height));
    }

    [Fact]
    public void A_sideways_phone_swaps_the_sides()
    {
        Assert.Equal((Height, Width), QuarterTurn.Extent(1, Width, Height));
        Assert.Equal((Height, Width), QuarterTurn.Extent(3, Width, Height));
    }

    /// <summary>The middle is the middle whichever way up it is: the canvas turns about it.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void The_middle_of_the_screen_is_the_middle_of_the_map(int quarter)
    {
        var (width, height) = QuarterTurn.Extent(quarter, Width, Height);

        var at = QuarterTurn.FromScreen(quarter, Width / 2, Height / 2, Width, Height);

        Assert.Equal(width / 2, at.X, 6);
        Assert.Equal(height / 2, at.Y, 6);
    }

    [Fact]
    public void Upright_changes_nothing()
    {
        var at = QuarterTurn.FromScreen(0, 250, 500, Width, Height);

        Assert.Equal(250, at.X, 6);
        Assert.Equal(500, at.Y, 6);
    }

    /// <summary>
    /// One quarter: the canvas is turned anticlockwise on screen, so the map's right edge comes
    /// up to the top and its top-right corner lands in the screen's top-left.
    ///
    /// This is the case that was wrong on the phone — it read as the map panning away from the
    /// finger. A y-down screen is why; see <see cref="QuarterTurn.Unturn"/>.
    /// </summary>
    [Fact]
    public void One_quarter_puts_the_screens_top_left_at_the_maps_top_right()
    {
        var at = QuarterTurn.FromScreen(1, 0, 0, Width, Height);

        Assert.Equal(Height, at.X, 6);
        Assert.Equal(0, at.Y, 6);
    }

    /// <summary>
    /// A finger dragged down the screen has to move the map down the screen whichever way up the
    /// phone is. Sideways that means the movement lands on the map's other axis, and the sign is
    /// the whole point: the half of this that was inverted is exactly what "the pan is reversed"
    /// looked like in the hand.
    /// </summary>
    [Fact]
    public void A_drag_down_the_screen_is_a_drag_along_the_map_the_same_way_round()
    {
        Assert.Equal((0.0, 10.0), QuarterTurn.Unturn(0, 0, 10));
        Assert.Equal((-10.0, 0.0), QuarterTurn.Unturn(1, 0, 10));
        Assert.Equal((0.0, -10.0), QuarterTurn.Unturn(2, 0, 10));
        Assert.Equal((10.0, 0.0), QuarterTurn.Unturn(3, 0, 10));
    }

    [Fact]
    public void Two_quarters_puts_the_screens_top_left_at_the_maps_bottom_right()
    {
        var at = QuarterTurn.FromScreen(2, 0, 0, Width, Height);

        Assert.Equal(Width, at.X, 6);
        Assert.Equal(Height, at.Y, 6);
    }

    [Fact]
    public void Three_quarters_puts_the_screens_top_left_at_the_maps_bottom_left()
    {
        var at = QuarterTurn.FromScreen(3, 0, 0, Width, Height);

        Assert.Equal(0, at.X, 6);
        Assert.Equal(Width, at.Y, 6);
    }

    /// <summary>
    /// Four quarters is where it started. The one property that catches a swapped pair or a
    /// flipped sign in any single case, without asserting a corner by hand four times.
    /// </summary>
    [Fact]
    public void Four_turns_come_back_round()
    {
        var (x, y) = (123.0, -45.0);

        for (var i = 0; i < 4; i++) (x, y) = QuarterTurn.Unturn(1, x, y);

        Assert.Equal(123, x, 6);
        Assert.Equal(-45, y, 6);
    }

    /// <summary>A movement is turned and never translated: a finger that does not move moves
    /// nothing, whichever way up the phone is.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void A_still_finger_moves_nothing(int quarter)
    {
        Assert.Equal((0.0, 0.0), QuarterTurn.Unturn(quarter, 0, 0));
    }

    /// <summary>Quarters outside 0 to 3 are folded rather than falling through to upright, since
    /// the sensor's own counting is not this class's business.</summary>
    [Fact]
    public void A_quarter_outside_the_range_is_folded()
    {
        Assert.Equal(QuarterTurn.Unturn(1, 10, 20), QuarterTurn.Unturn(5, 10, 20));
        Assert.Equal(QuarterTurn.Unturn(3, 10, 20), QuarterTurn.Unturn(-1, 10, 20));
    }
}
