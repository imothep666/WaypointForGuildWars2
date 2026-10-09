using GuildWarsCompanion.Services;
using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// The account-wide numbers, and the one deliberate refusal among them.
/// </summary>
public class AccountStandingTests
{
    [Fact]
    public void Luck_is_reported_as_the_magic_find_it_bought()
    {
        // This used to be refused: with no formula, interpolating between published anchors
        // would have invented the number. It is the real table now, so the percentage leads.
        var row = AccountStanding.Luck(1_234_567);

        // 1,232,380 buys the 195th percent and the 196th needs 1,255,080.
        Assert.Equal("195%", row.Value);
        Assert.Contains("1,234,567", row.Note!.Replace(".", ","));
    }

    [Fact]
    public void The_note_says_how_much_further_the_next_percent_is()
    {
        var row = AccountStanding.Luck(5_790);

        Assert.Equal("34%", row.Value);
        Assert.Contains("110 more for 35%", row.Note!);
    }

    [Fact]
    public void Reaching_the_cap_says_so_rather_than_still_quoting_a_target()
    {
        var row = AccountStanding.Luck(AccountStanding.LuckForCap);

        Assert.Equal("300%", row.Value);
        Assert.Contains("cap", row.Note);
        Assert.DoesNotContain("more for", row.Note!);
    }

    [Fact]
    public void Nothing_consumed_is_still_a_row()
    {
        // Zero luck is a fact about the account, not a missing value.
        Assert.Equal("0%", AccountStanding.Luck(0).Value);
    }

    // --- the rows that hide themselves --------------------------------------

    [Fact]
    public void Fractals_not_started_are_not_shown() =>
        Assert.Null(AccountStanding.Fractals(0));

    [Fact]
    public void A_fractal_level_is_shown_with_its_ceiling_noted()
    {
        Assert.Equal("42", AccountStanding.Fractals(42)!.Value);
        Assert.Null(AccountStanding.Fractals(42)!.Note);
        Assert.Contains("maximum", AccountStanding.Fractals(100)!.Note!);
    }

    [Fact]
    public void An_account_that_has_never_touched_wvw_shows_no_rank() =>
        Assert.Null(AccountStanding.Wvw(0));

    [Fact]
    public void Mastery_points_are_omitted_when_unknown_or_none()
    {
        // Null is "could not read", zero is "none earned" — neither is worth a row.
        Assert.Null(AccountStanding.Mastery(null));
        Assert.Null(AccountStanding.Mastery(0));
        Assert.Equal("9", AccountStanding.Mastery(9)!.Value);
    }
}
