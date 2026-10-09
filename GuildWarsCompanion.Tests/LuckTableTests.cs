using GuildWarsCompanion.Services;
using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// Luck consumed, turned into base magic find.
///
/// The app refused to show this number for a long time, and the reason still holds: there is no
/// formula, the steps run from 100 to 30,000, and anything interpolated would look authoritative
/// and be wrong. The table is real data now, so what these tests guard is that it is read
/// correctly — an off-by-one here misreports a stat on every account.
/// </summary>
public class LuckTableTests
{
    [Fact]
    public void The_first_hundred_luck_buys_the_first_percent()
    {
        Assert.Equal(0, LuckTable.MagicFind(99));
        Assert.Equal(1, LuckTable.MagicFind(100));
        Assert.Equal(1, LuckTable.MagicFind(199));
        Assert.Equal(2, LuckTable.MagicFind(200));
    }

    [Fact]
    public void Nothing_consumed_is_no_magic_find()
    {
        Assert.Equal(0, LuckTable.MagicFind(0));
        Assert.Equal(0, LuckTable.MagicFind(-5));
    }

    [Fact]
    public void A_real_account_reads_the_way_the_table_says()
    {
        // Measured against this project's own account: 5,790 consumed sits on the 34th
        // percent, which 5,560 bought, and the 35th wants 5,900.
        Assert.Equal(34, LuckTable.MagicFind(5_790));
        Assert.Equal(5_900, LuckTable.NextAt(5_790));
        Assert.Equal(110, LuckTable.StillNeeded(5_790));
    }

    [Fact]
    public void The_cap_is_the_end_of_the_table()
    {
        Assert.Equal(300, LuckTable.MagicFind(AccountStanding.LuckForCap));

        // And the table's last threshold is that same figure, which the app knew separately
        // long before it had the table — the check that the data is the right data.
        Assert.Equal(4_295_450, AccountStanding.LuckForCap);
    }

    [Fact]
    public void Eating_past_the_cap_adds_nothing()
    {
        Assert.Equal(300, LuckTable.MagicFind(AccountStanding.LuckForCap * 2));
        Assert.Null(LuckTable.NextAt(AccountStanding.LuckForCap));
        Assert.Null(LuckTable.StillNeeded(AccountStanding.LuckForCap));
    }

    [Fact]
    public void One_short_of_a_threshold_is_the_percent_below()
    {
        // The whole failure mode in one case: a threshold reached exactly counts, one short
        // does not.
        var next = LuckTable.NextAt(5_790)!.Value;

        Assert.Equal(34, LuckTable.MagicFind(next - 1));
        Assert.Equal(35, LuckTable.MagicFind(next));
    }

    [Fact]
    public void The_steps_get_steeper_the_further_it_goes()
    {
        // 100 for the first percent, 30,000 near the cap — which is why no formula fits and
        // why the table has to be carried.
        Assert.Equal(100, LuckTable.NextAt(0));
        Assert.Equal(30_000, LuckTable.NextAt(4_295_449) - LuckTable.NextAt(4_265_449 - 1));
    }
}
