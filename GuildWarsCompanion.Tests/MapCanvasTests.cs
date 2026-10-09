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
    /// but what a test puts there.
    /// </summary>
    private sealed class Scene : IMapScene
    {
        public HashSet<(int X, int Y)> Held { get; } = [];

        public HashSet<(int X, int Y)> Wanted { get; } = [];

        public IReadOnlySet<TileKey>? Kept { get; private set; }

        public int Quarter => 0;

        public int Zoom => 3;

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

        public IReadOnlyList<CompassTarget> Marks => [];

        public IReadOnlyList<RouteLine> Routes => [];

        public TrailGuide? Guide => null;

        public int GuideFrom => 0;

        public CompassTarget? Chosen => null;

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

        public IImage? Symbol(TargetKind kind) => null;
    }
}
