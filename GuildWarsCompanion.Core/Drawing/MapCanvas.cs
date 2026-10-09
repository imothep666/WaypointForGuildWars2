using GuildWarsCompanion.Services;
using GuildWarsCompanion.ViewModels;

namespace GuildWarsCompanion.Drawing;

/// <summary>
/// The map, drawn: tiles, then what is on them, then the character.
///
/// <b>Two coordinate systems, and keeping them apart is most of this file.</b> The tile service
/// speaks pixels — a tile is 256 of them, and at full zoom a pixel is a continent unit. MAUI
/// speaks device-independent units, of which this phone has about one for every three pixels.
/// Everything the view model holds is in tile pixels, and this class divides by
/// <see cref="Density"/> exactly where the drawing happens. Drawing a 256-pixel tile as 256 MAUI
/// units would blow the map up threefold and blur every label on it.
///
/// <b>Tiles go through <see cref="SymbolStamp"/> for the same reason the dial's marks do.</b>
/// <c>ICanvas.DrawImage</c> was measured as the whole of the compass's memory leak, and a map
/// holds forty images rather than a dozen. The stamp also lands the tile on exactly 256 device
/// pixels, which is what makes the artwork sharp instead of resampled.
/// </summary>
public sealed class MapCanvas : IDrawable
{
    private static readonly Color Brass = Color.FromArgb("#C9A227");
    private static readonly Color Slate = Color.FromArgb("#18212C");
    private static readonly Color Ink = Color.FromArgb("#0E141B");
    private static readonly Color Mist = Color.FromArgb("#9BA8B8");

    /// <summary>
    /// A route's line: brass, at a third strength.
    ///
    /// The same brass everything else on this screen is drawn in, deliberately faded. A route
    /// crosses the whole map and is the only thing here that covers ground rather than marking a
    /// point, so at full strength it competes with the artwork it is drawn on — and the artwork
    /// is what the player is reading the map for.
    /// </summary>
    private static readonly Color Route = Color.FromArgb("#C9A227").WithAlpha(0.65f);

    /// <summary>A marker's drawn size, in MAUI units. The dial uses the same.</summary>
    private const float SymbolSize = 22;

    /// <summary>How many tiles past the edge of the screen are fetched ahead of the finger.</summary>
    private const int Ring = 1;

    public IMapScene? Model { get; set; }

    /// <summary>Device pixels per MAUI unit, read from the display by the page.</summary>
    public float Density { get; set; } = 1;

    /// <summary>The tiles this frame drew or expects to need next, handed back so the cache knows
    /// which are worth keeping when room runs out. See <see cref="Gw2Tiles.Keep"/>.</summary>
    private readonly HashSet<TileKey> _shown = [];

    public void Draw(ICanvas canvas, RectF area)
    {
        canvas.FillColor = Ink;
        canvas.FillRectangle(area);

        if (Model is not { } model) return;

        // Into tile pixels, which is what everything the model holds is measured in, and through
        // the quarter turn: a sideways phone wants a landscape viewport out of a portrait window.
        var (width, height) = model.Extent(area.Width * Density, area.Height * Density);

        var left = model.Left(width);
        var top = model.Top(height);

        // The map is drawn in its own upright rectangle and the whole canvas is turned under it.
        // A quarter turn keeps the tile grid axis-aligned, so nothing is resampled and no label
        // ends up at an angle that has to be read sideways-ish.
        canvas.SaveState();
        canvas.Rotate(-model.Quarter * 90, area.Center.X, area.Center.Y);

        // Its rectangle is centred on the same point the turn is about, so this offset is all
        // that is needed to place it. Folded into the coordinates rather than applied as a canvas
        // translation on purpose: SymbolStamp reaches past the MAUI canvas to the platform one,
        // and a rotation there is proven — see CompassRose — while a translation is not.
        _offsetX = area.Center.X - (float)(width / Density) / 2;
        _offsetY = area.Center.Y - (float)(height / Density) / 2;

        Tiles(canvas, model, left, top, width, height);

        // Under the markers, because a route is the ground and a marker is the thing to reach.
        // A line drawn over a heart would hide the one symbol somebody is aiming a tap at.
        Routes(canvas, model, left, top);

        // Over the routes and under the markers: it is a route, singled out.
        Guide(canvas, model, left, top);

        Marks(canvas, model, width, height);
        Player(canvas, model, width, height);

        // Last, so nothing is drawn over it, and inside the turn so it sits in the corner the
        // player sees rather than the corner the handset has.
        Pad(canvas, model, width, height);

        canvas.RestoreState();
    }

    /// <summary>Where the map's own rectangle sits inside the turned canvas, in MAUI units.</summary>
    private float _offsetX;

    private float _offsetY;

    private void Tiles(
        ICanvas canvas, IMapScene model, double left, double top, double width, double height)
    {
        // A tile's side on the screen, in pixels. The level's own 256 at rest; more or less while
        // a zoom eases, which is the one time a tile is drawn scaled.
        var scale = model.Scale;
        var size = TileGeometry.TileSize * scale;

        var (fromX, fromY, toX, toY) = TileGeometry.Visible(
            left / scale, top / scale, width / scale, height / scale, model.GridWidth, model.GridHeight);

        var side = (float)(size / Density);

        _shown.Clear();

        // A ring past the edge of the screen, fetched but not drawn.
        //
        // Panning used to reveal blank squares that filled in a moment later, because a tile was
        // only ever asked for once it was already needed. One tile of lead time is the difference
        // between a map that slides and a map that is assembled in front of the player, and it
        // costs one row and one column of a cache that holds ninety-six.
        for (var x = fromX - Ring; x <= toX + Ring; x++)
        for (var y = fromY - Ring; y <= toY + Ring; y++)
        {
            if (x < 0 || y < 0 || x >= model.GridWidth || y >= model.GridHeight) continue;

            var key = model.Key(x, y);

            // Kept as well as wanted: a tile just off the edge is the one the next flick needs,
            // and telling the cache it is unused is what made the edges tear while panning.
            _shown.Add(key);

            if (model.Tile(x, y) is null) model.WantTile(x, y);
        }

        // The last level under the gaps in this one, when there are gaps.
        if (model.Underlay is { } under && Missing(model, fromX, fromY, toX, toY))
            Underlay(canvas, model, under, left, top, width, height);

        for (var x = fromX; x <= toX; x++)
        for (var y = fromY; y <= toY; y++)
        {
            if (model.Tile(x, y) is not { } image)
            {
                // Not here yet. The underlay shows through if it has this square, the background
                // if not, rather than a placeholder that would flash on every pan.
                continue;
            }

            var centreX = _offsetX + (float)((x * size - left + size / 2) / Density);
            var centreY = _offsetY + (float)((y * size - top + size / 2) / Density);

            if (!SymbolStamp.TryDraw(canvas, image, centreX, centreY, side, 1f))
                canvas.DrawImage(image, centreX - side / 2, centreY - side / 2, side, side);
        }

        // After the pass, not during it: a tile dropped mid-loop is one this frame still wanted.
        model.KeepTiles(_shown);
    }

    private static bool Missing(IMapScene model, int fromX, int fromY, int toX, int toY)
    {
        for (var x = fromX; x <= toX; x++)
        for (var y = fromY; y <= toY; y++)
            if (model.Tile(x, y) is null)
                return true;

        return false;
    }

    /// <summary>
    /// The level that was on screen before the zoom, scaled to this one: four of its tiles to one
    /// of these after zooming out, a quarter of one after zooming in.
    ///
    /// Only what is already in hand is drawn, and none of it is kept: these are not asked for and
    /// not stamped as used, so the cache lets them go first as the new level's tiles arrive and
    /// need the room. That is the whole of the underlay's lifetime.
    /// </summary>
    private void Underlay(
        ICanvas canvas, IMapScene model, int under, double left, double top, double width, double height)
    {
        // A tile of that level, on the screen, in pixels.
        var size = TileGeometry.TileSize * Math.Pow(2, model.Zoom - under) * model.Scale;
        var side = (float)(size / Density);

        var fromX = Math.Max(0, (int)Math.Floor(left / size));
        var fromY = Math.Max(0, (int)Math.Floor(top / size));
        var toX = (int)Math.Floor((left + width) / size);
        var toY = (int)Math.Floor((top + height) / size);

        for (var x = fromX; x <= toX; x++)
        for (var y = fromY; y <= toY; y++)
        {
            if (model.TileAt(under, x, y) is not { } image) continue;

            var centreX = _offsetX + (float)((x * size - left + size / 2) / Density);
            var centreY = _offsetY + (float)((y * size - top + size / 2) / Density);

            if (!SymbolStamp.TryDraw(canvas, image, centreX, centreY, side, 1f))
                canvas.DrawImage(image, centreX - side / 2, centreY - side / 2, side, side);
        }
    }

    private void Marks(ICanvas canvas, IMapScene model, double width, double height)
    {
        var chosen = model.Chosen;
        var here = false;

        foreach (var mark in model.Marks)
        {
            if (ReferenceEquals(mark, chosen))
            {
                here = true;

                continue;
            }

            Mark(canvas, model, mark, width, height, false);
        }

        // Last, so the objective is never drawn underneath something else. Only when this map
        // holds it: an objective two maps away is not anywhere on these tiles.
        if (here && chosen is not null) Mark(canvas, model, chosen, width, height, true);
    }

    /// <summary>
    /// The routes a pack carries for this map, as lines.
    ///
    /// <b>The paths are built once and moved, not rebuilt every frame.</b> A path is built in the
    /// tile grid's own coordinates — which do not move when the map is panned — and the canvas is
    /// translated under it. Only a zoom changes the geometry, because that is the only thing that
    /// changes what a continent unit is worth in pixels.
    ///
    /// <b>What the measurement does and does not say.</b> Rebuilding twelve routes per frame read
    /// 32.65% janky against 23.97% with routes off, which is what prompted this; but three runs of
    /// each condition afterwards put routes-on at 22-27% and routes-off at 27-31%, with the 90th
    /// percentile at 16-18ms either way. <c>dumpsys gfxinfo</c> on this handset varies by about
    /// four points between identical runs, so the original single-run gap was weaker evidence than
    /// it looked, and the honest claim is narrower: caching costs nothing measurable and removes
    /// the per-frame allocation of up to fifty paths of several hundred points each. Twelve routes
    /// is a small map; Teh's Trails files fifty on some.
    ///
    /// <b>Drawn even when it leaves the screen.</b> A route is a shape, and cutting it at the
    /// viewport would leave lines stopping in mid-air at the edges; the platform clips properly
    /// and a run is a few hundred points after thinning. A route for another map never arrives
    /// here at all — see <see cref="MarkerStore.RoutesOnMap"/>.
    /// </summary>
    private void Routes(ICanvas canvas, IMapScene model, double left, double top)
    {
        if (model.Routes.Count == 0) return;

        Rebuild(model);

        var scale = (float)model.Scale;

        canvas.SaveState();

        // The whole grid's offset for this frame. Everything below is drawn in grid coordinates,
        // so this one translation is what a per-point conversion used to do per point. The scale
        // is the easing zoom's, and the line width is divided by it so a route does not swell and
        // thin with the map.
        canvas.Translate(_offsetX - (float)(left / Density), _offsetY - (float)(top / Density));
        canvas.Scale(scale, scale);

        canvas.StrokeSize = 2.5f / scale;
        canvas.StrokeLineCap = LineCap.Round;
        canvas.StrokeLineJoin = LineJoin.Round;

        for (var i = 0; i < _paths.Count; i++)
        {
            // The pack's own colour where it states one — Metal Marker Myriad colours 251 of its
            // trails, Teh's Trails none at all — and this screen's brass where it does not.
            canvas.StrokeColor = _colours[i];

            canvas.DrawPath(_paths[i]);
        }

        canvas.RestoreState();
    }

    /// <summary>Paths in tile grid coordinates, valid until the zoom or the routes change.</summary>
    private readonly List<PathF> _paths = [];

    /// <summary>Each path's colour, built beside it so a frame does not unpack an int per
    /// route.</summary>
    private readonly List<Color> _colours = [];

    private IReadOnlyList<RouteLine> _pathsFor = [];

    private int _pathsZoom = -1;

    /// <summary>
    /// Builds the paths, when there is a reason to.
    ///
    /// A zoom changes what a continent unit is worth in pixels, so the geometry has to be redone;
    /// a pan does not. Choosing a different category replaces the list, which is compared by
    /// identity — the view model hands out a new list each time rather than mutating one, which
    /// is what makes that check sound.
    /// </summary>
    private void Rebuild(IMapScene model)
    {
        if (model.Zoom == _pathsZoom && ReferenceEquals(model.Routes, _pathsFor)) return;

        _paths.Clear();
        _colours.Clear();

        foreach (var route in model.Routes)
        {
            _colours.Add(route.Argb == RouteColour.None ? Route : Color.FromInt(route.Argb));

            var path = new PathF();

            for (var i = 0; i < route.Points.Count; i++)
            {
                var x = (float)(TileGeometry.ToPixels(route.Points[i].X, model.Zoom, model.Reference) / Density);
                var y = (float)(TileGeometry.ToPixels(route.Points[i].Y, model.Zoom, model.Reference) / Density);

                if (i == 0) path.MoveTo(x, y);
                else path.LineTo(x, y);
            }

            _paths.Add(path);
        }

        _pathsZoom = model.Zoom;
        _pathsFor = model.Routes;
    }

    /// <summary>The stretch of trail still to walk, in tile grid coordinates, and what it was
    /// built for. Rebuilt when the zoom, the guide or the character's place on it changes.</summary>
    private PathF? _guidePath;

    private TrailGuide? _guideFor;

    private int _guideFrom = -1;

    private int _guideZoom = -1;

    /// <summary>
    /// The way to the objective, drawn over the rest: an ink edge under full brass, so it reads
    /// against sand and snow alike and never looks like one more faded route.
    ///
    /// Built in grid coordinates and moved with the canvas like the routes are. It changes as the
    /// character walks, but only when they reach a new trail point — every fifteen metres at most
    /// — not every frame.
    /// </summary>
    private void Guide(ICanvas canvas, IMapScene model, double left, double top)
    {
        if (model.Guide is not { } guide) return;

        if (_guidePath is null || !ReferenceEquals(guide, _guideFor) ||
            model.GuideFrom != _guideFrom || model.Zoom != _guideZoom)
        {
            var path = new PathF();
            var first = true;

            foreach (var (px, py) in guide.Stretch(model.GuideFrom))
            {
                var x = (float)(TileGeometry.ToPixels(px, model.Zoom, model.Reference) / Density);
                var y = (float)(TileGeometry.ToPixels(py, model.Zoom, model.Reference) / Density);

                if (first) path.MoveTo(x, y);
                else path.LineTo(x, y);

                first = false;
            }

            _guidePath = path;
            _guideFor = guide;
            _guideFrom = model.GuideFrom;
            _guideZoom = model.Zoom;
        }

        var scale = (float)model.Scale;

        canvas.SaveState();

        // As the routes: moved and scaled with the map, at a width that does not change.
        canvas.Translate(_offsetX - (float)(left / Density), _offsetY - (float)(top / Density));
        canvas.Scale(scale, scale);

        canvas.StrokeLineCap = LineCap.Round;
        canvas.StrokeLineJoin = LineJoin.Round;

        canvas.StrokeColor = Ink;
        canvas.StrokeSize = 7 / scale;
        canvas.DrawPath(_guidePath);

        canvas.StrokeColor = Brass;
        canvas.StrokeSize = 4 / scale;
        canvas.DrawPath(_guidePath);

        canvas.RestoreState();
    }

    private void Mark(
        ICanvas canvas, IMapScene model, CompassTarget mark, double width, double height,
        bool isChosen)
    {
        var (px, py) = model.ToView(mark.X, mark.Y, width, height);

        var x = _offsetX + (float)(px / Density);
        var y = _offsetY + (float)(py / Density);

        // Off the map's rectangle. Cheaper to skip than to let the platform clip it, and a marker
        // a whole map away is the common case once the view is panned.
        if (px < -SymbolSize * Density || py < -SymbolSize * Density ||
            px > width + SymbolSize * Density || py > height + SymbolSize * Density) return;

        if (isChosen)
        {
            canvas.StrokeColor = Brass;
            canvas.StrokeSize = 2;
            canvas.DrawCircle(x, y, SymbolSize * 0.85f);
        }

        if (model.Symbol(mark.Kind) is { } image)
        {
            if (!SymbolStamp.TryDraw(canvas, image, x, y, SymbolSize, 1f))
                canvas.DrawImage(
                    image, x - SymbolSize / 2, y - SymbolSize / 2, SymbolSize, SymbolSize);

            return;
        }

        // No art for this kind, or it would not download. A dot still says where the thing is,
        // which is most of what this screen is for; see TargetDot for why chests and jumping
        // puzzles have no icon and are not given a borrowed one.
        canvas.FillColor = TargetDot.For(mark.Kind);
        canvas.FillCircle(x, y, 5);
    }

    /// <summary>
    /// The zoom pad: plus, the level, minus.
    ///
    /// Drawn rather than composed out of buttons, and that is what lets it turn: a view would sit
    /// in the page's layout, which never rotates, so a sideways phone would have the controls
    /// running down what the player sees as the bottom edge. Inside the canvas it is subject to
    /// the same quarter turn as the map, and <see cref="ZoomPad"/> hands the same rectangles to
    /// the hit test.
    /// </summary>
    private void Pad(ICanvas canvas, IMapScene model, double width, double height)
    {
        var plus = ZoomPad.Plus(width, height, Density);
        var minus = ZoomPad.Minus(width, height, Density);
        var (levelX, levelY, levelWidth, levelHeight) = ZoomPad.Level(width, height, Density);

        Button(canvas, plus, "+", model.CanZoomIn);
        Button(canvas, minus, "\u2212", model.CanZoomOut);

        var x = _offsetX + (float)(levelX / Density);
        var y = _offsetY + (float)(levelY / Density);
        var w = (float)(levelWidth / Density);
        var h = (float)(levelHeight / Density);

        // On its own plate, like the buttons. Brass on bare terrain is unreadable over half the
        // map — grass, cliff and water are all mid-tone — and this is the one part of the pad that
        // is read rather than aimed at.
        canvas.Alpha = 0.88f;
        canvas.FillColor = Ink;
        canvas.FillRoundedRectangle(x, y, w, h, 6);
        canvas.Alpha = 1f;

        // The level between them, as a step rather than the service's own zoom number.
        canvas.FontColor = Brass;
        canvas.FontSize = 13;
        canvas.DrawString(
            $"{model.Level}/{model.Levels}", x, y, w, h,
            HorizontalAlignment.Center, VerticalAlignment.Center);
    }

    private void Button(ICanvas canvas, PadButton button, string glyph, bool enabled)
    {
        var x = _offsetX + (float)(button.X / Density);
        var y = _offsetY + (float)(button.Y / Density);
        var side = (float)(button.Side / Density);

        // Dimmed rather than hidden at a limit. A control that disappears at the end of its range
        // takes the other one with it in the player's head, and they stop looking for either.
        //
        // Only the brass dims; the plate stays as solid as the live button's. Dimming the plate
        // too made it see-through, and over busy terrain such as a city's rooftops the minus at
        // the outermost zoom vanished into the art and looked drawn over.
        canvas.Alpha = 0.88f;

        canvas.FillColor = Ink;
        canvas.FillRoundedRectangle(x, y, side, side, 6);

        if (!enabled) canvas.Alpha = 0.3f;

        canvas.StrokeColor = Brass;
        canvas.StrokeSize = 2;
        canvas.DrawRoundedRectangle(x, y, side, side, 6);

        canvas.FontColor = Brass;
        canvas.FontSize = 22;
        canvas.DrawString(
            glyph, x, y, side, side, HorizontalAlignment.Center, VerticalAlignment.Center);

        canvas.Alpha = 1f;
    }

    private void Player(ICanvas canvas, IMapScene model, double width, double height)
    {
        if (model.Player is not { } player) return;

        var (px, py) = model.ToView(player.X, player.Y, width, height);

        var x = _offsetX + (float)(px / Density);
        var y = _offsetY + (float)(py / Density);

        // A ring rather than a dot, so whatever the character is standing on is still readable
        // underneath them — which on a map is usually the thing they are standing on for.
        canvas.FillColor = Slate;
        canvas.FillCircle(x, y, 7);

        canvas.StrokeColor = Brass;
        canvas.StrokeSize = 3;
        canvas.DrawCircle(x, y, 7);
    }
}
