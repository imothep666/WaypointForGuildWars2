using GuildWarsCompanion.Services;

using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// The zoom pad's layout, which the drawing and the hit test both read.
///
/// Same reason <see cref="QuarterTurnTests"/> exists: two files that cannot see each other have to
/// agree about where a button is, and when they do not the pad looks right and answers a tap the
/// player did not make.
/// </summary>
public class ZoomPadTests
{
    private const double Width = 1080;
    private const double Height = 2000;

    /// <summary>The phone the pad was tuned on: 375 dpi, 2.34 pixels a unit.</summary>
    private const double Density = 375 / 160.0;

    [Fact]
    public void Plus_is_above_minus()
    {
        Assert.True(ZoomPad.Plus(Width, Height, Density).Y < ZoomPad.Minus(Width, Height, Density).Y);
    }

    [Fact]
    public void The_two_buttons_share_an_edge_and_do_not_overlap()
    {
        var plus = ZoomPad.Plus(Width, Height, Density);
        var minus = ZoomPad.Minus(Width, Height, Density);

        Assert.Equal(plus.X, minus.X, 6);
        Assert.True(minus.Y >= plus.Y + plus.Side);
    }

    /// <summary>The level sits in the gap, and nowhere else: a number drawn over a button is a
    /// number nobody can press through.</summary>
    [Fact]
    public void The_level_sits_between_the_buttons()
    {
        var plus = ZoomPad.Plus(Width, Height, Density);
        var minus = ZoomPad.Minus(Width, Height, Density);
        var (_, y, _, height) = ZoomPad.Level(Width, Height, Density);

        Assert.True(y >= plus.Y + plus.Side);
        Assert.True(y + height <= minus.Y);
    }

    [Fact]
    public void The_pad_is_inside_the_map()
    {
        var plus = ZoomPad.Plus(Width, Height, Density);
        var minus = ZoomPad.Minus(Width, Height, Density);

        Assert.True(plus.X > 0);
        Assert.True(plus.X + plus.Side < Width);
        Assert.True(plus.Y > 0);
        Assert.True(minus.Y + minus.Side < Height);
    }

    /// <summary>Against the right-hand edge rather than floating: the margin is the only thing
    /// between it and the corner.</summary>
    [Fact]
    public void The_pad_hugs_the_right_edge()
    {
        var plus = ZoomPad.Plus(Width, Height, Density);

        Assert.Equal(Width - ZoomPad.Margin(Density) - ZoomPad.Side(Density), plus.X, 6);
    }

    [Fact]
    public void A_button_holds_its_own_middle()
    {
        var plus = ZoomPad.Plus(Width, Height, Density);

        Assert.True(plus.Holds(plus.CentreX, plus.CentreY));
    }

    [Fact]
    public void A_button_holds_its_corners_and_nothing_past_them()
    {
        var plus = ZoomPad.Plus(Width, Height, Density);

        Assert.True(plus.Holds(plus.X, plus.Y));
        Assert.True(plus.Holds(plus.X + plus.Side, plus.Y + plus.Side));

        Assert.False(plus.Holds(plus.X - 1, plus.Y));
        Assert.False(plus.Holds(plus.X, plus.Y + plus.Side + 1));
    }

    /// <summary>A tap on one button is not a tap on the other, which is the whole reason for the
    /// gap between them.</summary>
    [Fact]
    public void Neither_button_holds_the_others_middle()
    {
        var plus = ZoomPad.Plus(Width, Height, Density);
        var minus = ZoomPad.Minus(Width, Height, Density);

        Assert.False(plus.Holds(minus.CentreX, minus.CentreY));
        Assert.False(minus.Holds(plus.CentreX, plus.CentreY));
    }

    /// <summary>
    /// The same size wherever it is drawn. Sized in device pixels it was right on the phone it was
    /// tuned on and twice as large on a desktop at 125 %; in units it is one size everywhere.
    /// </summary>
    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(2.625)]
    [InlineData(3.5)]
    public void A_button_is_the_same_size_in_units_at_any_density(double density)
    {
        var plus = ZoomPad.Plus(Width, Height, density);

        Assert.Equal(ZoomPad.SideUnits, plus.Side / density, 6);
    }

    /// <summary>On the phone it was tuned on, the pad keeps the size it had in pixels.</summary>
    [Fact]
    public void On_the_tuning_phone_a_button_is_still_about_120_pixels()
    {
        Assert.InRange(ZoomPad.Plus(Width, Height, Density).Side, 118, 122);
    }

    /// <summary>
    /// Sideways the map's rectangle has its sides swapped, and the pad follows it rather than the
    /// handset — which is what makes it turn with the map. Nothing here does the turning; it is
    /// enough that the pad is laid out in the map's space. See <see cref="QuarterTurn"/>.
    /// </summary>
    [Fact]
    public void A_landscape_map_puts_the_pad_on_the_maps_own_right_edge()
    {
        var plus = ZoomPad.Plus(Height, Width, Density);

        Assert.Equal(Height - ZoomPad.Margin(Density) - ZoomPad.Side(Density), plus.X, 6);
        Assert.True(plus.Y > 0);
        Assert.True(plus.Y + ZoomPad.Height(Density) <= Width);
    }
}
