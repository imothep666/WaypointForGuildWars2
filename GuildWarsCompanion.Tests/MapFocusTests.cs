using GuildWarsCompanion.Bridge.Protocol;
using GuildWarsCompanion.Services;
using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// Which map the screen draws when the bridge speaks.
///
/// The pin is the rule that matters most: it is what makes the map usable away from the PC, and
/// a character walking through a border while somebody reads Frostgorge on a train must not drag
/// the screen with them.
/// </summary>
public class MapFocusTests
{
    private const int Queensdale = 15;
    private const int Frostgorge = 30;

    private static GameReport Playing(int mapId, string? mapName = null) =>
        new(new GameState(
                1200, "Imothep", 4, 0, 3, false, mapId, 1, 1, 0,
                UiState.GameHasFocus, 44416, 29248),
            IsLive: true, Note: "playing", MapName: mapName);

    private static GameReport Nothing() => GameReport.Nothing("Waiting for the bridge…");

    // --- following the character -------------------------------------------

    [Fact]
    public void Loads_the_map_the_character_walked_into()
    {
        var frame = MapFocus.Decide(Playing(Frostgorge), null, string.Empty, Queensdale);

        Assert.Equal(Frostgorge, frame.Load);
    }

    [Fact]
    public void Asks_for_nothing_when_the_character_is_already_on_the_drawn_map()
    {
        var frame = MapFocus.Decide(Playing(Queensdale), null, string.Empty, Queensdale);

        Assert.Null(frame.Load);
        Assert.True(frame.ShowsPlayer);
    }

    /// <summary>A dot on the wrong map is worse than no dot.</summary>
    [Fact]
    public void Hides_the_dot_on_a_map_the_character_is_not_standing_in()
    {
        var frame = MapFocus.Decide(Playing(Frostgorge), null, string.Empty, Queensdale);

        Assert.False(frame.ShowsPlayer);
    }

    [Fact]
    public void Names_the_map_the_bridge_named()
    {
        var frame = MapFocus.Decide(
            Playing(Queensdale, "Queensdale"), null, string.Empty, Queensdale);

        Assert.Equal("Queensdale", frame.Line);
    }

    /// <summary>
    /// The id shows for one line while the name is still being looked up, which is
    /// <see cref="GameReport.MapLabel"/>'s whole job — the screen must not invent a second
    /// fallback of its own.
    /// </summary>
    [Fact]
    public void Falls_back_to_the_map_id_until_the_name_arrives()
    {
        var frame = MapFocus.Decide(Playing(Queensdale), null, string.Empty, Queensdale);

        Assert.Equal("Map 15", frame.Line);
    }

    // --- a pinned map ------------------------------------------------------

    /// <summary>The rule the pin exists for.</summary>
    [Fact]
    public void Refuses_to_leave_a_pinned_map_when_the_character_is_elsewhere()
    {
        var frame = MapFocus.Decide(
            Playing(Queensdale), Frostgorge, "Frostgorge Sound", Frostgorge);

        Assert.Null(frame.Load);
        Assert.Equal("Frostgorge Sound", frame.Line);
    }

    /// <summary>
    /// And the dot still arrives when they walk into the map being read — the pin stops the map
    /// moving, not the bridge being listened to.
    /// </summary>
    [Fact]
    public void Shows_the_dot_when_the_character_walks_into_the_pinned_map()
    {
        var frame = MapFocus.Decide(
            Playing(Frostgorge), Frostgorge, "Frostgorge Sound", Frostgorge);

        Assert.True(frame.ShowsPlayer);
        Assert.Null(frame.Load);
    }

    [Fact]
    public void Keeps_the_pinned_name_with_no_game_at_all()
    {
        var frame = MapFocus.Decide(Nothing(), Frostgorge, "Frostgorge Sound", Frostgorge);

        Assert.Equal("Frostgorge Sound", frame.Line);
        Assert.False(frame.ShowsPlayer);
        Assert.Null(frame.Load);
    }

    // --- no game -----------------------------------------------------------

    [Fact]
    public void Reports_the_bridge_note_when_nothing_is_pinned_and_nothing_is_running()
    {
        var frame = MapFocus.Decide(Nothing(), null, string.Empty, -1);

        Assert.Equal("Waiting for the bridge…", frame.Line);
        Assert.False(frame.ShowsPlayer);
        Assert.Null(frame.Load);
    }

    /// <summary>
    /// A block that outlives the game: MumbleLink keeps its last values after Guild Wars 2
    /// closes, so a state that exists is not a game that is running. The map is still the map
    /// that character was last on, and drawing it is right — liveness is said in words elsewhere.
    /// </summary>
    [Fact]
    public void Still_draws_the_last_map_when_the_game_has_closed()
    {
        var stale = Playing(Queensdale, "Queensdale") with { IsLive = false };

        var frame = MapFocus.Decide(stale, null, string.Empty, -1);

        Assert.Equal(Queensdale, frame.Load);
        Assert.Equal("Queensdale", frame.Line);
    }
}
