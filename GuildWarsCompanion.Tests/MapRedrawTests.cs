using GuildWarsCompanion.Services;

using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>Which bridge reports are worth a frame of the map.</summary>
public class MapRedrawTests
{
    [Fact]
    public void Standing_still_costs_no_frame()
    {
        Assert.False(MapRedraw.PlayerMoved((100, 200), (100, 200), 16));
    }

    /// <summary>At the whole-map zoom a pixel is sixteen units, so a few units of shuffling is a
    /// frame that would come out the same.</summary>
    [Fact]
    public void Less_than_a_pixel_costs_no_frame()
    {
        Assert.False(MapRedraw.PlayerMoved((100, 200), (110, 205), 16));
    }

    [Fact]
    public void A_pixel_or_more_is_drawn()
    {
        Assert.True(MapRedraw.PlayerMoved((100, 200), (116, 200), 16));
        Assert.True(MapRedraw.PlayerMoved((100, 200), (100, 199), 1));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Arriving_on_or_leaving_the_map_is_drawn(bool before, bool after)
    {
        (double, double)? Here(bool on) => on ? (100, 200) : null;

        Assert.True(MapRedraw.PlayerMoved(Here(before), Here(after), 16));
    }

    /// <summary>
    /// Measured from the dot as last drawn, not from the last report: half a step a report adds up
    /// to a redraw every second report, where comparing reports would never redraw at all.
    /// </summary>
    [Fact]
    public void Small_steps_add_up_to_a_frame()
    {
        (double X, double Y)? drawn = (0, 0);
        var frames = 0;

        for (var report = 1; report <= 10; report++)
        {
            (double X, double Y) now = (report * 0.5, 0);

            if (!MapRedraw.PlayerMoved(drawn, now, 1)) continue;

            drawn = now;
            frames++;
        }

        Assert.Equal(5, frames);
    }

    [Fact]
    public void Nobody_before_or_after_costs_no_frame()
    {
        Assert.False(MapRedraw.PlayerMoved(null, null, 16));
    }
}
