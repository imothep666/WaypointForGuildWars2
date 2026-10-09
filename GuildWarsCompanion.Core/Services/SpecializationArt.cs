namespace GuildWarsCompanion.Services;

/// <summary>
/// Which specialization banner the app draws, and where it comes from.
///
/// The API's own background art is 1024x256 with only its lower-left painted: the top half is
/// transparent and the right quarter empty. Drawn whole, half the banner shows the panel
/// through it, and no crop the layout can perform fixes that - the empty/painted boundary sits
/// exactly on the art's midline, so a centred crop is always half empty whatever the row size.
///
/// So the crop is done once, before the app ships: each banner trimmed to the part that
/// carries a picture, anchored left where the character stands, every one at the same ratio so
/// every row is the same height. 81 of them, about 1.3 MB, and they need no connection.
///
/// A specialization released after this build has no file here and falls back to the API's
/// artwork - the whole thing, half empty, but present rather than blank.
/// </summary>
public static class SpecializationArt
{
    /// <summary>The ratio every bundled banner was cut to, and so the height of every row.</summary>
    public const double Ratio = 640.0 / 152.0;

    private static readonly HashSet<int> Bundled =
    [
        1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23,
        24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44,
        45, 46, 47, 48, 49, 50, 51, 52, 53, 54, 55, 56, 57, 58, 59, 60, 61, 62, 63, 64, 65,
        66, 67, 68, 69, 70, 71, 72, 73, 74, 75, 76, 77, 78, 79, 80, 81
    ];

    /// <summary>
    /// The banner for a specialization: the cut-down local file where there is one, and the
    /// API's own artwork where there is not.
    /// </summary>
    public static string? Banner(int id, string? apiArtwork) =>
        Bundled.Contains(id) ? $"spec_{id}.jpg" : apiArtwork;

    /// <summary>Whether this build carries a cut banner for a specialization.</summary>
    public static bool HasBanner(int id) => Bundled.Contains(id);
}
