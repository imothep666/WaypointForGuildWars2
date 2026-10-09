using GuildWarsCompanion.Bridge.Protocol;
using GuildWarsCompanion.Services;
using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// The line at the top of Today. Its one way to be wrong is to speak for a game that has
/// closed: MumbleLink keeps the last character and map, so only a live report may say anything.
/// </summary>
public class PlayingLineTests
{
    private static GameState Thief(string name = "Sweet Child O Night") =>
        new(42, name, 5, 58, 2, false, 28, 1, 1, 0, UiState.GameHasFocus, 0, 0);

    [Fact]
    public void A_live_game_names_the_character_and_the_map()
    {
        var report = new GameReport(Thief(), true, "Reading Guild Wars 2 now.", "Wayfarer Foothills");

        Assert.Equal("Sweet Child O Night · Thief · in Wayfarer Foothills", PlayingLine.Describe(report));
    }

    [Fact]
    public void The_map_is_its_id_until_the_name_arrives()
    {
        var report = new GameReport(Thief(), true, "Reading Guild Wars 2 now.");

        Assert.Equal("Sweet Child O Night · Thief · in Map 28", PlayingLine.Describe(report));
    }

    [Fact]
    public void A_closed_game_says_nothing_though_its_last_state_remains()
    {
        var report = new GameReport(Thief(), false, "Guild Wars 2 is not running", "Wayfarer Foothills");

        Assert.Null(PlayingLine.Describe(report));
    }

    [Fact]
    public void The_login_screen_says_nothing()
    {
        // Live, but no character yet: there is nobody to name and no map they are in.
        Assert.Null(PlayingLine.Describe(new GameReport(Thief(name: ""), true, "At character select.")));
    }

    [Fact]
    public void No_bridge_says_nothing()
    {
        Assert.Null(PlayingLine.Describe(null));
        Assert.Null(PlayingLine.Describe(GameReport.Nothing("Guild Wars 2 is not running")));
    }
}
