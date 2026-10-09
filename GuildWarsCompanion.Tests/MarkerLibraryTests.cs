using System.Globalization;

using GuildWarsCompanion.Gw2;
using GuildWarsCompanion.Services;

using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// What a downloaded pack leaves on the device, and how it becomes something to point at.
///
/// The interesting one is <see cref="A_marker_further_north_is_further_up_the_continent"/>. The
/// sign of z was reasoned out, implemented backwards, and then measured against a real pack; see
/// <see cref="MapSpace.FromMetres"/> for the numbers that settled it.
/// </summary>
public class MarkerLibraryTests
{
    /// <summary>Queensdale, both rectangles as /v2/maps/15 publishes them.</summary>
    private static readonly MapInfo Queensdale = new(
        15, "Queensdale", 4, 1, 1,
        [[42624, 28032], [46208, 30464]],
        [[-43008, -27648], [43008, 30720]]);

    private static StoredMarker Marker(double x, double z, TargetKind kind = TargetKind.Chest) =>
        new(15, x, z, kind, "Hidden chest");

    [Fact]
    public void A_written_marker_reads_back_the_same()
    {
        var markers = new[]
        {
            new StoredMarker(15, -12.5, 40.75, TargetKind.Chest, "Hidden chest"),
            new StoredMarker(38, 8, -16, TargetKind.JumpingPuzzle, "Urmaug's Secret")
        };

        var read = MarkerLibrary.Read(MarkerLibrary.Write(markers));

        Assert.Equal(markers, read);
    }

    /// <summary>The file is written on the phone and read on the phone, so its numbers must not
    /// depend on what the phone's language is set to.</summary>
    [Fact]
    public void The_file_does_not_depend_on_the_phones_language()
    {
        var was = Thread.CurrentThread.CurrentCulture;

        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");

            var text = MarkerLibrary.Write([Marker(-12.5, 40.75)]);

            Assert.Contains("-12.5", text);
            Assert.Equal(-12.5, MarkerLibrary.Read(text)[0].MetresX, 6);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = was;
        }
    }

    /// <summary>A name with a tab in it must not become a marker with its fields shifted along
    /// by one.</summary>
    [Fact]
    public void A_name_cannot_break_the_line_it_is_written_on()
    {
        var read = MarkerLibrary.Read(
            MarkerLibrary.Write([new StoredMarker(15, 1, 2, TargetKind.Chest, "a\tb\nc")]));

        Assert.Single(read);
        Assert.Equal("a b c", read[0].Name);
    }

    /// <summary>A height is kept for drawing in the game, and its absence is kept as absence.</summary>
    [Fact]
    public void A_markers_height_survives_and_so_does_having_none()
    {
        var markers = new[]
        {
            new StoredMarker(15, 1, 2, TargetKind.Chest, "Up high", 512.25),
            new StoredMarker(15, 3, 4, TargetKind.Chest, "Somewhere")
        };

        var read = MarkerLibrary.Read(MarkerLibrary.Write(markers));

        Assert.Equal(markers, read);
    }

    /// <summary>A pack stored before heights were kept still works, without them, until it is
    /// re-read.</summary>
    [Fact]
    public void A_file_from_before_heights_is_still_read()
    {
        var read = Assert.Single(MarkerLibrary.Read("markers 1\n15\t1\t2\t6\tOld chest\n"));

        Assert.Equal("Old chest", read.Name);
        Assert.Null(read.MetresUp);
    }

    [Fact]
    public void A_file_from_a_format_this_does_not_know_is_refused()
    {
        Assert.Empty(MarkerLibrary.Read("markers 99\n15\t1\t2\t6\tChest"));
        Assert.Empty(MarkerLibrary.Read(string.Empty));
    }

    [Fact]
    public void A_damaged_line_costs_only_itself()
    {
        var text = MarkerLibrary.Write([Marker(1, 2), Marker(3, 4)]) + "nonsense\n15\tx\ty\t6\tBad\n";

        Assert.Equal(2, MarkerLibrary.Read(text).Count);
    }

    [Fact]
    public void Only_the_maps_own_markers_are_placed()
    {
        var targets = MarkerLibrary.Targets(
            [Marker(0, 0), new StoredMarker(38, 0, 0, TargetKind.Chest, "Elsewhere")], Queensdale);

        Assert.Single(targets);
        Assert.Equal("Hidden chest", targets[0].Name);
    }

    [Fact]
    public void A_placed_marker_lands_in_continent_coordinates()
    {
        var targets = MarkerLibrary.Targets([Marker(0, -39.0)], Queensdale);

        Assert.Single(targets);

        var target = targets[0];

        // Inside Queensdale's own rectangle, which is the only claim being made.
        Assert.InRange(target.X, 42624, 46208);
        Assert.InRange(target.Y, 28032, 30464);
        Assert.Equal(TargetKind.Chest, target.Kind);
        Assert.True(target.Precise);
    }

    /// <summary>
    /// z is carried straight through, not negated.
    ///
    /// The reasoning said it should be negated — avatar z is south, map y is north — and the
    /// measurement said otherwise. Against Teh's Trails and the floor endpoint's landmarks, +z
    /// puts 114 of Queensdale's 334 markers within 40 units of a published landmark and -z puts
    /// 9. The flip belongs to ToContinent alone; applying it twice was the bug. See
    /// <see cref="MapSpace.FromMetres"/>.
    /// </summary>
    [Fact]
    public void A_packs_z_is_north_in_map_space()
    {
        var (_, north) = MapSpace.FromMetres(0, 100);
        var (_, south) = MapSpace.FromMetres(0, -100);

        Assert.True(north > 0);
        Assert.True(south < 0);
        Assert.Equal(100 * MapSpace.InchesPerMetre, north, 6);
    }

    /// <summary>
    /// Going north in a pack's coordinates goes up the continent, once both steps are applied.
    ///
    /// The property that actually matters, and the one that was wrong: two flips cancelled and
    /// put every marker on the wrong side of its map.
    /// </summary>
    [Fact]
    public void A_marker_further_north_is_further_up_the_continent()
    {
        var south = MarkerLibrary.Targets([Marker(0, -100)], Queensdale)[0];
        var north = MarkerLibrary.Targets([Marker(0, 100)], Queensdale)[0];

        Assert.True(north.Y < south.Y);
    }

    /// <summary>
    /// The import-time check, and what it is and is not good for.
    ///
    /// It does not catch a mirrored map: measured against a real pack, 329 of Queensdale's 334
    /// markers stay inside the rectangle when north and south are swapped. It catches junk — a
    /// marker from another map, a coordinate in the wrong unit.
    ///
    /// Queensdale's map rectangle runs to about 43000 inches, which is a thousand metres. A marker
    /// at a hundred thousand metres is either a pack's mistake or a conversion this app has got
    /// wrong, and either way it is not drawn.
    /// </summary>
    [Fact]
    public void A_marker_outside_its_own_map_is_dropped()
    {
        Assert.Empty(MarkerLibrary.Targets([Marker(100000, 0)], Queensdale));
        Assert.Equal(1, MarkerLibrary.Outside([Marker(100000, 0)], Queensdale));
    }

    [Fact]
    public void Markers_inside_the_map_are_not_counted_as_outside_it()
    {
        Assert.Equal(0, MarkerLibrary.Outside([Marker(0, 0), Marker(10, -10)], Queensdale));
    }

    /// <summary>A map with no rectangles cannot place anything, and must say so by placing
    /// nothing rather than by throwing.</summary>
    [Fact]
    public void A_map_without_rectangles_places_nothing()
    {
        var bare = new MapInfo(15, "Queensdale", 4, 1, 1, null);

        Assert.Empty(MarkerLibrary.Targets([Marker(0, 0)], bare));
    }

    [Fact]
    public void A_marker_with_no_name_is_called_what_it_is()
    {
        var targets = MarkerLibrary.Targets(
            [new StoredMarker(15, 0, 0, TargetKind.JumpingPuzzle, string.Empty)], Queensdale);

        Assert.Equal("Jumping puzzle", targets[0].Name);
    }
}
