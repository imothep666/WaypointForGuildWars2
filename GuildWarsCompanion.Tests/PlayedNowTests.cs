using GuildWarsCompanion.Services;
using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// Which character the roster says is being played.
///
/// Three sources of very different quality — the game naming itself, a playtime figure that
/// moved, and a stored detection — and the whole value is in the order they are tried. Most of
/// what follows is about the ways a weaker source can wrongly outlive a stronger one.
///
/// The distinction the bridge draws between "I could not look" and "I looked and nobody is
/// playing" is load-bearing for half of these, so it is spelled out at every call rather than
/// left to a null.
/// </summary>
public class PlayedNowTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

    private static (string, DateTimeOffset)? Detected(string name, DateTimeOffset at) => (name, at);

    private static LiveReading Playing(string name) => LiveReading.Playing(name);

    private static readonly LiveReading NoBridge = LiveReading.Unknown;

    private static readonly LiveReading Closed = LiveReading.NobodyPlaying;

    [Fact]
    public void The_game_naming_a_character_is_the_answer()
    {
        Assert.Equal("playing now",
            PlayedNow.Describe("Imothep", Playing("Imothep"), grew: null, detected: null, Noon));
    }

    [Fact]
    public void A_live_reading_outranks_a_playtime_guess_about_a_different_character()
    {
        // The trap this ordering exists for. Playtime keeps rising for a minute or two after
        // somebody logs out, so the character they left would otherwise still claim to be live
        // beside the one that demonstrably is.
        var stale = PlayedNow.Describe("Riven", Playing("Imothep"), grew: "Riven", detected: null, Noon);

        Assert.Null(stale);
    }

    [Fact]
    public void Only_one_row_can_be_playing_now()
    {
        var rows = new[] { "Imothep", "Riven", "Sable" }
            .Select(name => PlayedNow.Describe(name, Playing("Imothep"), grew: "Riven", detected: null, Noon))
            .ToList();

        Assert.Single(rows, phrase => phrase == "playing now");
    }

    [Fact]
    public void Without_a_bridge_the_playtime_guess_still_answers()
    {
        // Unknown is ignorance, not a denial: no bridge, mobile data, PC asleep. The app must
        // work exactly as it did before for anyone who never sets one up.
        Assert.Equal("playing now",
            PlayedNow.Describe("Riven", NoBridge, grew: "Riven", detected: null, Noon));
    }

    [Fact]
    public void A_live_reading_does_not_erase_a_rows_own_history()
    {
        // Somebody else is playing, but this character was played an hour ago and that is still
        // worth saying — it just cannot be said as "now".
        var phrase = PlayedNow.Describe(
            "Riven", Playing("Imothep"), grew: null, Detected("Riven", Noon.AddHours(-2)), Noon);

        Assert.Equal("last played 2h ago", phrase);
    }

    [Fact]
    public void A_stored_detection_ages_into_words()
    {
        Assert.Equal("played just now",
            PlayedNow.Describe("Riven", NoBridge, null, Detected("Riven", Noon.AddMinutes(-5)), Noon));

        Assert.Equal("last played 3h ago",
            PlayedNow.Describe("Riven", NoBridge, null, Detected("Riven", Noon.AddHours(-3)), Noon));

        Assert.Equal("last played 2d ago",
            PlayedNow.Describe("Riven", NoBridge, null, Detected("Riven", Noon.AddDays(-2)), Noon));
    }

    [Fact]
    public void The_just_now_window_is_the_one_it_always_was()
    {
        // 45 minutes, carried over unchanged from the inference this replaced. Worth pinning:
        // shortening it silently would quietly reword half the roster.
        Assert.Equal(TimeSpan.FromMinutes(45), PlayedNow.Recent);

        Assert.Equal("played just now",
            PlayedNow.Describe("Riven", NoBridge, null, Detected("Riven", Noon.AddMinutes(-44)), Noon));

        Assert.Equal("last played 0h ago",
            PlayedNow.Describe("Riven", NoBridge, null, Detected("Riven", Noon.AddMinutes(-46)), Noon));
    }

    [Fact]
    public void A_character_nobody_has_anything_to_say_about_says_nothing()
    {
        Assert.Null(PlayedNow.Describe("Sable", NoBridge, null, null, Noon));
        Assert.Null(PlayedNow.Describe("Sable", NoBridge, "Riven", Detected("Riven", Noon), Noon));
    }

    [Fact]
    public void A_closed_game_silences_the_playtime_guess()
    {
        // The bug this distinction exists for, and the one MumbleLink working properly finally
        // exposed. Log out and the API keeps handing over playtime for another minute or two, so
        // "grew" names the character just closed. The bridge has already said nobody is playing;
        // believing the guess over it puts "playing now" on a row with no bead beside it, which
        // is the app contradicting itself in two controls an inch apart. A live game sitting on
        // the login screen is the same value and the same reasoning.
        Assert.Null(PlayedNow.Describe("Riven", Closed, grew: "Riven", detected: null, Noon));
    }

    [Fact]
    public void A_closed_game_still_lets_a_row_say_what_it_did()
    {
        // Silencing the guess is not silencing the row. This is the phrase somebody should see
        // the moment they alt-tab to the phone after closing the game.
        Assert.Equal("played just now",
            PlayedNow.Describe("Riven", Closed, grew: "Riven", Detected("Riven", Noon.AddMinutes(-2)), Noon));
    }

    [Fact]
    public void Nobody_playing_is_an_answer_and_no_bridge_is_not()
    {
        // Both used to arrive as null, which is exactly how the closed game kept its claim.
        Assert.True(LiveReading.NobodyPlaying.Answered);
        Assert.False(LiveReading.Unknown.Answered);

        // Unknown is the default so that a caller with no bridge — and no knowledge that any of
        // this exists — cannot accidentally assert absence.
        Assert.Equal(LiveReading.Unknown, default(LiveReading));

        Assert.Equal("playing now",
            PlayedNow.Describe("Riven", LiveReading.Unknown, "Riven", null, Noon));
        Assert.Null(PlayedNow.Describe("Riven", LiveReading.NobodyPlaying, "Riven", null, Noon));
    }

    [Fact]
    public void The_name_off_the_wire_is_trimmed_before_it_is_trusted()
    {
        // MumbleLink's copy has been through a fixed-width buffer and a JSON round trip.
        Assert.True(PlayedNow.Matches("Imothep", "Imothep "));
        Assert.Equal("playing now",
            PlayedNow.Describe("Imothep", Playing(" Imothep "), grew: null, detected: null, Noon));
    }

    [Fact]
    public void Names_that_differ_only_in_case_are_different_characters()
    {
        // The API returns names exactly as typed and they are unique per account, so a loose
        // comparison could mark the wrong row.
        Assert.False(PlayedNow.Matches("Imothep", "imothep"));
    }
}
