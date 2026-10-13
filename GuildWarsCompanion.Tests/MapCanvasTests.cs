using GuildWarsCompanion.Drawing;
using GuildWarsCompanion.Services;

using Microsoft.Maui.Graphics;

using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// The map's frame, drawn onto a canvas that records instead of painting.
///
/// The drawing was device-verified only for as long as it took a test to need a game stream, a
/// tile cache and the API before it could see a frame. <see cref="IMapScene"/> took that away;
/// these tests stand where a screenshot used to. The first of them is the one a screenshot
/// caught: the dimmed minus at the outermost zoom drew its plate see-through and vanished into
/// busy terrain.
/// </summary>
public class MapCanvasTests
{
    /// <summary>A portrait phone's worth of canvas, at one device pixel per unit, so the
    /// recorded coordinates are the zoom pad's own.</summary>
    private static readonly RectF Screen = new(0, 0, 400, 600);

    private const char Minus = '−';

    [Fact]
    public void A_dimmed_button_keeps_a_solid_plate()
    {
        var calls = Draw(new Scene { CanZoomOut = false });
        var minus = ButtonCalls(calls, ZoomPad.Minus(Screen.Width, Screen.Height, 1));

        Assert.Equal(0.88f, minus.Single(c => c.Name == "FillRoundedRectangle").Alpha);
        Assert.Equal(0.3f, minus.Single(c => c.Name == "DrawRoundedRectangle").Alpha);
        Assert.Equal(0.3f, calls.Single(c => c.Text == Minus.ToString()).Alpha);
    }

    [Fact]
    public void A_live_button_is_drawn_at_full_strength()
    {
        var calls = Draw(new Scene());
        var plus = ButtonCalls(calls, ZoomPad.Plus(Screen.Width, Screen.Height, 1));

        Assert.All(plus, call => Assert.Equal(0.88f, call.Alpha));
        Assert.Equal(0.88f, calls.Single(c => c.Text == "+").Alpha);
    }

    [Fact]
    public void The_pad_says_which_step_of_how_many()
    {
        var calls = Draw(new Scene { Level = 2, Levels = 3 });

        Assert.Contains(calls, c => c.Text == "2/3");
    }

    /// <summary>Nothing may be drawn over the pad: a tile that arrives late lands under it.</summary>
    [Fact]
    public void The_pad_is_drawn_after_every_tile_and_the_player()
    {
        var calls = Draw(new Scene { Held = { (0, 0), (1, 1) }, Player = (100, 100) });

        var lastTile = calls.FindLastIndex(c => c.Name == "DrawImage");
        var player = calls.FindLastIndex(c => c.Name == "FillEllipse");
        var firstPad = calls.FindIndex(c => c.Text == "+") - 2;

        Assert.True(lastTile >= 0 && player >= 0);
        Assert.True(lastTile < firstPad, "a tile was drawn after the zoom pad");
        Assert.True(player < firstPad, "the player was drawn after the zoom pad");
    }

    [Fact]
    public void A_tile_in_hand_is_drawn_and_a_missing_one_is_asked_for()
    {
        var scene = new Scene { Held = { (0, 0) } };

        var calls = Draw(scene);

        Assert.Single(calls, c => c.Name == "DrawImage");
        Assert.DoesNotContain((0, 0), scene.Wanted);
        Assert.Contains((1, 1), scene.Wanted);
    }

    /// <summary>The cache is told about every tile on screen, so none of them is the one evicted
    /// next; see Gw2Tiles.Keep.</summary>
    [Fact]
    public void Every_tile_on_the_grid_near_the_screen_is_kept()
    {
        var scene = new Scene { Held = { (0, 0) } };

        Draw(scene);

        Assert.NotNull(scene.Kept);
        Assert.Equal(4, scene.Kept!.Count);
    }

    /// <summary>While a zoom eases the tiles are drawn at the eased size, not the level's own.</summary>
    [Fact]
    public void A_zoom_under_way_draws_its_tiles_scaled()
    {
        var calls = Draw(new Scene { Scale = 0.5, Held = { (0, 0) } });

        var tile = calls.Single(c => c.Name == "DrawImage");

        Assert.Equal(128, tile.Width);
        Assert.Equal(64, tile.X + tile.Width / 2);
    }

    /// <summary>A square of the new level not yet in hand shows the old level there, scaled,
    /// under the new tiles that have arrived.</summary>
    [Fact]
    public void The_last_level_shows_under_a_missing_tile()
    {
        var scene = new Scene { Underlay = 2, Held = { (1, 1) }, HeldAt = { (2, 0, 0) } };

        var calls = Draw(scene).Where(c => c.Name == "DrawImage").ToList();

        Assert.Equal(2, calls.Count);

        // One tile of level 2 covers four of level 3, so it is twice the size and drawn first.
        Assert.Equal(512, calls[0].Width);
        Assert.Equal(256, calls[1].Width);
    }

    [Fact]
    public void No_underlay_is_drawn_when_nothing_is_missing()
    {
        var scene = new Scene { Underlay = 2, Held = { (0, 0), (0, 1), (1, 0), (1, 1) }, HeldAt = { (2, 0, 0) } };

        var calls = Draw(scene).Where(c => c.Name == "DrawImage").ToList();

        Assert.Equal(4, calls.Count);
        Assert.All(calls, c => Assert.Equal(256, c.Width));
    }

    /// <summary>The underlay is never fetched and never kept: the cache lets it go first as the
    /// new level arrives.</summary>
    [Fact]
    public void The_underlay_is_neither_asked_for_nor_kept()
    {
        var scene = new Scene { Underlay = 2, HeldAt = { (2, 0, 0) } };

        Draw(scene);

        Assert.Equal(4, scene.Kept!.Count);
        Assert.All(scene.Kept, key => Assert.Equal(3, key.Zoom));
        Assert.Equal(4, scene.Wanted.Count);
    }

    // --- markers -------------------------------------------------------------

    [Fact]
    public void A_marker_with_art_is_stamped_where_it_is()
    {
        var calls = Draw(new Scene { Marks = [Heart], Art = { TargetKind.Heart } });

        var stamp = calls.Single(c => c.Name == "DrawImage");

        Assert.Equal((89f, 189f, 22f), (stamp.X, stamp.Y, stamp.Width));
    }

    /// <summary>Chests and puzzles have no art of their own; see TargetDot.</summary>
    [Fact]
    public void A_marker_with_no_art_is_a_dot_in_the_colour_of_its_kind()
    {
        var calls = Draw(new Scene { Marks = [Chest] });

        var dot = calls.Single(c => c.Name == "FillEllipse");

        Assert.Equal((295f, 395f, 10f), (dot.X, dot.Y, dot.Width));
        Assert.Equal(Hex(TargetDot.For(TargetKind.Chest)), Hex(dot.Colour));
    }

    /// <summary>The objective is drawn last, ringed, so nothing covers the thing being aimed at.</summary>
    [Fact]
    public void The_objective_is_ringed_and_drawn_over_the_rest()
    {
        var calls = Draw(new Scene { Marks = [Chest, Heart], Chosen = Chest });

        var ring = calls.FindIndex(c => c.Name == "DrawEllipse" && c.X == 300 - 22 * 0.85f);
        var chest = calls.FindIndex(c => c.Name == "FillEllipse" && c.X == 295);
        var heart = calls.FindIndex(c => c.Name == "FillEllipse" && c.X == 95);

        Assert.True(heart >= 0 && ring > heart && chest > ring);
        Assert.Equal(Hex(Brass), Hex(calls[ring].Colour));
    }

    /// <summary>An objective two maps away is not anywhere on these tiles.</summary>
    [Fact]
    public void An_objective_on_another_map_draws_no_ring()
    {
        var calls = Draw(new Scene { Marks = [Chest], Chosen = Heart with { MapId = 30 } });

        Assert.DoesNotContain(calls, c => c.Name == "DrawEllipse");
    }

    [Fact]
    public void A_marker_off_the_screen_is_not_drawn()
    {
        var calls = Draw(new Scene { Marks = [Chest with { X = 5000 }] });

        Assert.DoesNotContain(calls, c => c.Name == "FillEllipse");
    }

    // --- routes and the guide ------------------------------------------------

    /// <summary>A route is the ground; the guide is a route singled out; a marker is the thing to
    /// reach, and the character stands on all of it.</summary>
    [Fact]
    public void Tiles_then_routes_then_the_guide_then_markers_then_the_player()
    {
        var calls = Draw(new Scene
        {
            Held = { (0, 0) }, Routes = [Line], Guide = Along(), Marks = [Chest], Player = (50, 50)
        });

        var tile = calls.FindLastIndex(c => c.Name == "DrawImage");
        var route = calls.FindIndex(c => c.Name == "DrawPath");
        var guide = calls.FindLastIndex(c => c.Name == "DrawPath");
        var marker = calls.FindIndex(c => c.Name == "FillEllipse" && c.Width == 10);
        var player = calls.FindIndex(c => c.Name == "FillEllipse" && c.Width == 14);

        Assert.True(tile >= 0 && tile < route && route < guide && guide < marker && marker < player);
    }

    /// <summary>Continent units into pixels at the drawn zoom: at zoom 3 of reference 7 a pixel is
    /// sixteen units.</summary>
    [Fact]
    public void A_route_is_drawn_through_its_points_at_the_zoom()
    {
        var calls = Draw(new Scene { Routes = [Line] });

        var path = calls.Single(c => c.Name == "DrawPath").Path!;

        Assert.Equal([new PointF(0, 0), new PointF(10, 0), new PointF(20, 0), new PointF(30, 0)], path.Points);
    }

    [Fact]
    public void A_route_with_no_colour_of_its_own_is_faded_brass()
    {
        var calls = Draw(new Scene { Routes = [Line, Line with { Argb = unchecked((int)0xFF3060C0) }] });

        var paths = calls.Where(c => c.Name == "DrawPath").ToList();

        Assert.Equal(Hex(Brass.WithAlpha(0.65f)), Hex(paths[0].Colour));
        Assert.Equal("#FF3060C0", Hex(paths[1].Colour));
    }

    /// <summary>A pan does not change what a unit is worth, so the paths are moved, not rebuilt; a
    /// zoom does, and so does a new choice of routes.</summary>
    [Fact]
    public void Route_paths_are_built_again_only_for_a_new_zoom_or_new_routes()
    {
        var scene = new Scene { Routes = [Line] };
        var canvas = new MapCanvas { Model = scene };

        var first = Path(canvas);
        var again = Path(canvas);

        scene.Zoom = 4;
        var zoomed = Path(canvas);

        scene.Routes = [Line];
        var chosen = Path(canvas);

        Assert.Same(first, again);
        Assert.NotSame(again, zoomed);
        Assert.NotSame(zoomed, chosen);
    }

    /// <summary>An ink edge under full brass, so the way reads against sand and snow alike and never
    /// looks like one more faded route.</summary>
    [Fact]
    public void The_guide_is_brass_over_a_wider_ink_edge()
    {
        var calls = Draw(new Scene { Guide = Along() });

        var strokes = calls.Where(c => c.Name == "DrawPath").ToList();

        Assert.Equal(2, strokes.Count);
        Assert.Equal((Hex(Ink), 7f), (Hex(strokes[0].Colour), strokes[0].Stroke));
        Assert.Equal((Hex(Brass), 4f), (Hex(strokes[1].Colour), strokes[1].Stroke));
        Assert.Same(strokes[0].Path, strokes[1].Path);
    }

    /// <summary>Only what is left to walk: from the character's place on the trail to the objective.</summary>
    [Fact]
    public void The_guide_starts_where_the_character_is_on_it()
    {
        var calls = Draw(new Scene { Guide = Along(), GuideFrom = 1 });

        var path = calls.First(c => c.Name == "DrawPath").Path!;

        Assert.Equal([new PointF(10, 0), new PointF(20, 0), new PointF(30, 0)], path.Points);
    }

    [Fact]
    public void The_guide_is_built_again_when_the_character_moves_along_it()
    {
        var scene = new Scene { Guide = Along() };
        var canvas = new MapCanvas { Model = scene };

        var first = Path(canvas);
        var again = Path(canvas);

        scene.GuideFrom = 2;
        var moved = Path(canvas);

        Assert.Same(first, again);
        Assert.NotSame(again, moved);
        Assert.Equal(2, moved.Count);
    }

    [Fact]
    public void The_player_is_a_brass_ring_on_slate()
    {
        var calls = Draw(new Scene { Player = (50, 60) });

        var fill = calls.Single(c => c.Name == "FillEllipse");
        var ring = calls.Single(c => c.Name == "DrawEllipse");

        Assert.Equal((43f, 53f, 14f), (fill.X, fill.Y, fill.Width));
        Assert.Equal(Hex(Slate), Hex(fill.Colour));
        Assert.Equal((Hex(Brass), 3f), (Hex(ring.Colour), ring.Stroke));
    }

    private static readonly Color Brass = Color.FromArgb("#C9A227");
    private static readonly Color Ink = Color.FromArgb("#0E141B");
    private static readonly Color Slate = Color.FromArgb("#18212C");

    private static readonly CompassTarget Heart =
        new("Help Farmer Diah", TargetKind.Heart, 15, "Queensdale", 100, 200);

    private static readonly CompassTarget Chest = new("Chest", TargetKind.Chest, 15, "Queensdale", 300, 400);

    /// <summary>A straight run east, 160 units a step: ten pixels a step at zoom 3.</summary>
    private static readonly RouteLine Line =
        new(15, "tw.trails.main", "Main trail", [(0, 0), (160, 0), (320, 0), (480, 0)]);

    /// <summary>The guide along <see cref="Line"/> to its far end.</summary>
    private static TrailGuide Along() => TrailGuide.Find([Line], 480, 0)!;

    private static string Hex(Color? colour) => colour!.ToArgbHex(includeAlpha: true);

    /// <summary>One frame through a canvas kept between frames, and the first path it drew.</summary>
    private static PathF Path(MapCanvas canvas)
    {
        var recording = new RecordingCanvas();

        canvas.Draw(recording, Screen);

        return recording.Calls.First(c => c.Name == "DrawPath").Path!;
    }

    private static List<DrawCall> Draw(Scene scene)
    {
        var canvas = new RecordingCanvas();

        new MapCanvas { Model = scene }.Draw(canvas, Screen);

        return canvas.Calls;
    }

    /// <summary>The plate and outline drawn exactly at a button's square.</summary>
    private static List<DrawCall> ButtonCalls(List<DrawCall> calls, PadButton button) =>
        calls.Where(c => c.Name.EndsWith("RoundedRectangle")
                         && c.X == (float)button.X && c.Y == (float)button.Y
                         && c.Width == (float)button.Side)
            .ToList();

    /// <summary>
    /// A two-by-two map at its outermost zoom, scrolled to its top left corner, with nothing on it
    /// but what a test puts there. A continent point is drawn at the same numbers, so a marker at
    /// (100, 200) is centred on (100, 200).
    /// </summary>
    private sealed class Scene : IMapScene
    {
        public HashSet<(int X, int Y)> Held { get; } = [];

        public HashSet<(int X, int Y)> Wanted { get; } = [];

        public IReadOnlySet<TileKey>? Kept { get; private set; }

        public int Quarter => 0;

        public int Zoom { get; set; } = 3;

        public double Scale { get; init; } = 1;

        public int? Underlay { get; init; }

        /// <summary>Tiles of other levels in hand, for the underlay.</summary>
        public HashSet<(int Zoom, int X, int Y)> HeldAt { get; } = [];

        public int Reference => 7;

        public int Level { get; init; } = 1;

        public int Levels { get; init; } = 3;

        public bool CanZoomIn { get; init; } = true;

        public bool CanZoomOut { get; init; } = true;

        public int GridWidth => 2;

        public int GridHeight => 2;

        public IReadOnlyList<CompassTarget> Marks { get; init; } = [];

        public IReadOnlyList<RouteLine> Routes { get; set; } = [];

        public TrailGuide? Guide { get; init; }

        public int GuideFrom { get; set; }

        public CompassTarget? Chosen { get; init; }

        /// <summary>The kinds the game's art has arrived for; the rest are drawn as dots.</summary>
        public HashSet<TargetKind> Art { get; } = [];

        public (double X, double Y)? Player { get; init; }

        public (double Width, double Height) Extent(double screenWidth, double screenHeight) =>
            (screenWidth, screenHeight);

        public double Left(double viewWidth) => 0;

        public double Top(double viewHeight) => 0;

        public (double X, double Y) ToView(double continentX, double continentY, double viewWidth,
            double viewHeight) => (continentX, continentY);

        public TileKey Key(int x, int y) => new(1, 1, Zoom, x, y);

        public IImage? Tile(int x, int y) => Held.Contains((x, y)) ? new BlankImage() : null;

        public IImage? TileAt(int zoom, int x, int y) => HeldAt.Contains((zoom, x, y)) ? new BlankImage() : null;

        public void WantTile(int x, int y) => Wanted.Add((x, y));

        public void KeepTiles(IReadOnlySet<TileKey> visible) => Kept = new HashSet<TileKey>(visible);

        public IImage? Symbol(TargetKind kind) => Art.Contains(kind) ? new BlankImage() : null;
    }
}
