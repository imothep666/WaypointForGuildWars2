using GuildWarsCompanion.Gw2;
using GuildWarsCompanion.Services;
using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// The shape check that decides whether a scanned QR code holds an API key. Its job is to
/// reject obvious non-keys so the user gets "that is not a key" rather than a puzzling
/// rejection two screens later.
/// </summary>
public class Gw2ApiKeyTests
{
    // Correct shape, not a real key: upper-case hex in dash-separated blocks.
    private const string KeyShaped =
        "A1B2C3D4-5E6F-4071-8A9B-0C1D2E3F4A5B6C7D8E9F-0A1B-4C2D-9E3F-5A6B7C8D9E0F";

    [Fact]
    public void Accepts_a_correctly_shaped_key() => Assert.True(Gw2ApiKey.LooksValid(KeyShaped));

    [Fact]
    public void Accepts_a_key_with_surrounding_whitespace() =>
        Assert.True(Gw2ApiKey.LooksValid($"  {KeyShaped}\n"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("    ")]
    [InlineData("https://wiki.guildwars2.com/wiki/API:Main")]   // the QR I tested with
    [InlineData("WIFI:S=network;T=WPA;P=hunter2;;")]
    [InlineData("A1B2C3D4-5E6F")]                               // right alphabet, too short
    [InlineData("A1B2C3D4X5E6F4071Y8A9BZ0C1D2E3F4A5B6C7D8E9F0A1B4C2D9E3F5A6B7C8D9E0F")] // no dashes
    [InlineData("G1H2I3J4-5K6L-4071-8M9N-0O1P2Q3R4S5T6U7V8W9X-0Y1Z-4C2D-9E3F-5A6B7C8D9E0F")]
    public void Rejects_what_is_not_a_key(string? candidate) =>
        Assert.False(Gw2ApiKey.LooksValid(candidate));
}

/// <summary>
/// Where equipment tiles sit on each body plan. Positions are fractions, so an out-of-range
/// value silently parks a tile off the edge of the figure.
/// </summary>
public class LoadoutLayoutTests
{
    private static readonly string[] Races =
        ["Human", "Norn", "Sylvari", "Charr", "Asura", "Unknown"];

    [Fact]
    public void Every_race_places_every_pinned_slot()
    {
        foreach (var race in Races)
        foreach (var slot in LoadoutLayout.PlacedSlots)
            Assert.NotNull(LoadoutLayout.PositionFor(race, slot));
    }

    [Fact]
    public void Positions_stay_inside_the_figure_box()
    {
        foreach (var race in Races)
        foreach (var slot in LoadoutLayout.PlacedSlots)
        {
            var position = LoadoutLayout.PositionFor(race, slot)!.Value;

            Assert.InRange(position.X, 0d, 1d);
            Assert.InRange(position.Y, 0d, 1d);
        }
    }

    [Fact]
    public void Upright_races_share_one_plan_and_the_others_do_not()
    {
        // Norn and sylvari are built like humans; charr and asura hang armour differently,
        // which is the whole reason there are three body drawings.
        var human = LoadoutLayout.PositionFor("Human", "Coat");

        Assert.Equal(human, LoadoutLayout.PositionFor("Norn", "Coat"));
        Assert.Equal(human, LoadoutLayout.PositionFor("Sylvari", "Coat"));
        Assert.NotEqual(human, LoadoutLayout.PositionFor("Charr", "Coat"));
        Assert.NotEqual(human, LoadoutLayout.PositionFor("Asura", "Coat"));
    }

    [Fact]
    public void An_unknown_race_falls_back_to_the_upright_plan()
    {
        // A new playable race must not blank the doll.
        Assert.Equal(
            LoadoutLayout.PositionFor("Human", "Helm"),
            LoadoutLayout.PositionFor("Kodan", "Helm"));

        Assert.Equal("body_humanoid.png", LoadoutLayout.ImageFor("Kodan"));
    }

    [Fact]
    public void Each_race_gets_its_own_drawing()
    {
        Assert.Equal("body_charr.png", LoadoutLayout.ImageFor("Charr"));
        Assert.Equal("body_asura.png", LoadoutLayout.ImageFor("Asura"));
        Assert.Equal("body_humanoid.png", LoadoutLayout.ImageFor("Sylvari"));
    }

    [Fact]
    public void Trinkets_are_not_pinned_to_the_body()
    {
        // Nothing about a torso says "second ring", so trinkets live in a strip instead.
        foreach (var slot in LoadoutLayout.StripSlots)
            Assert.DoesNotContain(slot, LoadoutLayout.PlacedSlots);
    }
}

/// <summary>
/// Tiles must not sit on top of each other. This is the property the eye catches instantly on
/// a device and that no amount of reading the coordinate table reveals: the gloves tile and
/// the swap-weapon tile overlapped by about thirteen pixels for a week before anyone saw it.
/// </summary>
public class LoadoutOverlapTests
{
    private static readonly string[] Races = ["Human", "Charr", "Asura"];

    /// <summary>Matches the view: a fixed box with proportionally positioned fixed-size tiles.</summary>
    private const double BoxWidth = 320;
    private const double BoxHeight = 430;

    private static (double Left, double Top, double Right, double Bottom) Rect(string race, string slot)
    {
        var position = LoadoutLayout.PositionFor(race, slot)!.Value;
        var size = LoadoutLayout.TileSize;

        // AbsoluteLayout's proportional positioning spans the free space, not the whole box.
        var left = position.X * (BoxWidth - size);
        var top = position.Y * (BoxHeight - size);

        return (left, top, left + size, top + size);
    }

    [Fact]
    public void No_two_tiles_overlap_on_any_body()
    {
        var collisions = new List<string>();

        foreach (var race in Races)
        {
            var slots = LoadoutLayout.PlacedSlots;

            for (var i = 0; i < slots.Count; i++)
            for (var j = i + 1; j < slots.Count; j++)
            {
                var a = Rect(race, slots[i]);
                var b = Rect(race, slots[j]);

                // A gap, not merely no overlap: flush tiles read as one blob on a phone.
                const double gap = 2;

                var overlaps = a.Left < b.Right + gap && b.Left < a.Right + gap &&
                               a.Top < b.Bottom + gap && b.Top < a.Bottom + gap;

                if (overlaps)
                    collisions.Add($"{race}: {slots[i]} overlaps {slots[j]}");
            }
        }

        Assert.Empty(collisions);
    }

    [Fact]
    public void Every_tile_stays_inside_the_box()
    {
        foreach (var race in Races)
        foreach (var slot in LoadoutLayout.PlacedSlots)
        {
            var rect = Rect(race, slot);

            Assert.InRange(rect.Left, 0, BoxWidth - LoadoutLayout.TileSize);
            Assert.InRange(rect.Top, 0, BoxHeight - LoadoutLayout.TileSize);
        }
    }
}
