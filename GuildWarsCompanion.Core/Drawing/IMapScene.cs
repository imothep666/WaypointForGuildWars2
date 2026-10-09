using GuildWarsCompanion.Services;

namespace GuildWarsCompanion.Drawing;

/// <summary>
/// Everything <see cref="MapCanvas"/> reads to draw a frame, and nothing else.
///
/// <see cref="ViewModels.MapViewModel"/> is the one real answer, and it takes thirteen services
/// to build, which is why the drawing went untested for as long as it did: a test that wanted to
/// see a frame had to stand up a game stream, a tile cache and the API first. Drawn from this
/// instead, a frame needs only the numbers on it. Measured in tile pixels throughout, as the view
/// model holds them; see <see cref="MapCanvas"/> for the units.
/// </summary>
public interface IMapScene
{
    /// <summary>Quarter turns the map is drawn through, for a phone held sideways.</summary>
    int Quarter { get; }

    int Zoom { get; }

    /// <summary>How much bigger than <see cref="Zoom"/>'s own pixels the map is drawn: one at rest,
    /// between a half and two while a zoom eases.</summary>
    double Scale { get; }

    /// <summary>The level drawn under any tile of <see cref="Zoom"/> not yet in hand, or null.</summary>
    int? Underlay { get; }

    /// <summary>The zoom at which a continent unit is a tile pixel.</summary>
    int Reference { get; }

    /// <summary>The zoom pad's step, one-based, and how many there are.</summary>
    int Level { get; }

    int Levels { get; }

    bool CanZoomIn { get; }

    bool CanZoomOut { get; }

    /// <summary>The map's size in tiles at this zoom.</summary>
    int GridWidth { get; }

    int GridHeight { get; }

    IReadOnlyList<CompassTarget> Marks { get; }

    IReadOnlyList<RouteLine> Routes { get; }

    TrailGuide? Guide { get; }

    /// <summary>The first point of <see cref="Guide"/> still ahead.</summary>
    int GuideFrom { get; }

    CompassTarget? Chosen { get; }

    (double X, double Y)? Player { get; }

    /// <summary>The screen's size in tile pixels, through the quarter turn.</summary>
    (double Width, double Height) Extent(double screenWidth, double screenHeight);

    /// <summary>The tile pixel at the view's left and top edges.</summary>
    double Left(double viewWidth);

    double Top(double viewHeight);

    (double X, double Y) ToView(double continentX, double continentY, double viewWidth, double viewHeight);

    TileKey Key(int x, int y);

    /// <summary>A tile in hand, or null. Never blocks.</summary>
    IImage? Tile(int x, int y);

    /// <summary>A tile of another level, only if already in hand; for the underlay.</summary>
    IImage? TileAt(int zoom, int x, int y);

    /// <summary>Asks for a tile not yet in hand.</summary>
    void WantTile(int x, int y);

    /// <summary>The tiles a frame drew or will want next, so the cache keeps them.</summary>
    void KeepTiles(IReadOnlySet<TileKey> visible);

    IImage? Symbol(TargetKind kind);
}
