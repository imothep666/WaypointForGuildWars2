using GuildWarsCompanion.Bridge.Protocol;

namespace GuildWarsCompanion.Services;

/// <summary>
/// What one report from the bridge means for the map screen.
/// </summary>
/// <param name="Line">What the line under the map says.</param>
/// <param name="Load">A map to start loading, or null to leave the screen on the one it has.</param>
/// <param name="ShowsPlayer">
/// Whether the character is standing on the map being drawn, and so whether the dot belongs on it.
/// </param>
public readonly record struct MapFrame(string Line, int? Load, bool ShowsPlayer);

/// <summary>
/// Which map the screen draws, and what it calls itself.
///
/// Three rules that used to live inside the view model's report handler, where nothing could
/// reach them: a pinned map outranks the game, the dot belongs only to the map actually drawn,
/// and the line names whichever of the two the screen is showing. They are the difference between
/// a map screen that is usable on a train and one that yanks itself somewhere else mid-read, so
/// they are worth more than device verification.
///
/// Pure, like <see cref="FollowBox"/> and <see cref="MapPicks"/>. The view model keeps the parts
/// only it can do — starting a load, moving the centre, telling the canvas to redraw.
/// </summary>
public static class MapFocus
{
    /// <param name="pinned">The map somebody chose by name, or null while following the character.</param>
    /// <param name="pinnedName">That map's name, for the line.</param>
    /// <param name="loadedMap">The map whose tiles and markers the screen currently holds.</param>
    public static MapFrame Decide(
        GameReport report, int? pinned, string pinnedName, int loadedMap)
    {
        // No game. A pinned map keeps its own name: "Waiting for the bridge…" over a map that is
        // plainly drawn and plainly named would be describing the wrong thing.
        if (report.State is not { } state)
            return new MapFrame(pinned is null ? GameText.Note(report) : pinnedName, null, false);

        // Only on the map being drawn. A character in Kessex is not standing anywhere on
        // Queensdale's tiles, and a dot on the wrong map is worse than no dot.
        var here = state.MapId == loadedMap;

        // The pin is what makes this screen usable away from the PC, so the game is not allowed
        // to take the map away from somebody who asked for it by name.
        var load = pinned is null && !here ? state.MapId : (int?)null;

        return new MapFrame(pinned is null ? report.MapLabel : pinnedName, load, here);
    }
}
