using GuildWarsCompanion.Data;
using GuildWarsCompanion.Services;

using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// Finding the last session in the ledger. Playtime is the API's own counter, so a session is
/// wherever it rose; everything else is measured from the point before to the point after.
/// </summary>
public class SessionRecapTests
{
    private static readonly DateTimeOffset Evening = new(2026, 10, 5, 18, 0, 0, TimeSpan.Zero);

    private static LedgerPoint At(int minutes, long thiefAge, int thiefLevel = 79, long coin = 10_000, long karma = 500,
        long necroAge = 1000) =>
        new(Evening.AddMinutes(minutes),
            new Dictionary<string, (long, int)>
            {
                ["Sweet Child O Night"] = (thiefAge, thiefLevel),
                ["Imothep The First"] = (necroAge, 56)
            },
            new Dictionary<int, long> { [1] = coin, [2] = karma });

    [Fact]
    public void No_play_is_no_session()
    {
        Assert.Null(SessionRecap.Latest([At(0, 100), At(30, 100), At(60, 100)]));
        Assert.Null(SessionRecap.Latest([At(0, 100)]));
    }

    [Fact]
    public void An_ended_session_runs_from_the_point_before_to_the_point_after()
    {
        // Idle, then two half-hours of play, then quiet. Coin is earned during play and after the
        // last rise, in the last minutes before logging out — which the recap must include.
        LedgerPoint[] points =
        [
            At(0, 100), At(30, 100, coin: 10_000),
            At(60, 1900, coin: 25_000), At(90, 3700, thiefLevel: 80, coin: 40_000, karma: 2500),
            At(120, 3700, thiefLevel: 80, coin: 44_000, karma: 2500)
        ];

        var session = SessionRecap.Latest(points)!;

        Assert.False(session.IsOngoing);
        Assert.Equal(Evening.AddMinutes(30), session.Before.At);
        Assert.Equal(Evening.AddMinutes(90), session.LastSeen.At);
        Assert.Equal(3600, session.PlayedSeconds);
        Assert.Equal(new SessionCharacter("Sweet Child O Night", 3600, 79, 80), Assert.Single(session.Characters));
        Assert.Equal(34_000, session.WalletChange[1]);
        Assert.Equal(2000, session.WalletChange[2]);
    }

    [Fact]
    public void Still_rising_at_the_last_point_is_a_session_in_progress()
    {
        var session = SessionRecap.Latest([At(0, 100), At(30, 1900), At(60, 3700)])!;

        Assert.True(session.IsOngoing);
        Assert.Equal(3600, session.PlayedSeconds);
    }

    [Fact]
    public void A_quiet_gap_ends_one_session_and_only_the_latest_is_told()
    {
        LedgerPoint[] points =
        [
            At(0, 100), At(30, 1900), At(60, 1900), // a first session, 30 minutes
            At(90, 1900), At(120, 4600), At(150, 4600) // a later one, 45 minutes
        ];

        var session = SessionRecap.Latest(points)!;

        Assert.Equal(2700, session.PlayedSeconds);
        Assert.Equal(Evening.AddMinutes(90), session.Before.At);
    }

    [Fact]
    public void Two_characters_in_one_session_are_both_named_longest_first()
    {
        var session = SessionRecap.Latest([At(0, 100), At(30, 700, necroAge: 2200), At(60, 700, necroAge: 2200)])!;

        Assert.Equal(["Imothep The First", "Sweet Child O Night"], session.Characters.Select(c => c.Name));
        Assert.Equal(1800, session.PlayedSeconds);
    }

    [Fact]
    public void The_words()
    {
        Assert.Equal("1 h 50 min", SessionRecap.Duration(6600));
        Assert.Equal("25 min", SessionRecap.Duration(1500));
        Assert.Equal("Sweet Child O Night · 1 h 00 min · level 79 → 80",
            SessionRecap.Line(new SessionCharacter("Sweet Child O Night", 3600, 79, 80)));

        var names = new Dictionary<int, string> { [2] = "Karma", [63] = "Astral Acclaim" };

        // Thousands in the phone's own culture, as every count in the app is.
        Assert.Equal($"+3g 40s 12c · +{12300:N0} Karma · −450 Astral Acclaim",
            SessionRecap.Wallet(new Dictionary<int, long> { [1] = 34012, [2] = 12300, [63] = -450 }, id => names[id]));
        Assert.Equal("the wallet did not change", SessionRecap.Wallet(new Dictionary<int, long>(), _ => ""));
    }
}
