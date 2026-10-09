using GuildWarsCompanion.Services;
using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// Pairing a specialization's two flat lists back into the grid the game draws.
///
/// The API hands over three minors and nine majors, both in tier order, and nothing marks
/// where one tier ends. A grid whose tiers have slipped by one draws perfectly well and shows
/// the wrong traits under the wrong tier — which is why the shape is checked rather than
/// assumed.
/// </summary>
public class TraitGridTests
{
    private static readonly int[] Minors = [10, 20, 30];

    private static readonly int[] Majors = [1, 2, 3, 4, 5, 6, 7, 8, 9];

    [Fact]
    public void Each_tier_takes_its_minor_and_the_three_majors_that_follow()
    {
        var tiers = TraitGrid.Split(Minors, Majors);

        Assert.Equal(3, tiers.Count);
        Assert.Equal(10, tiers[0].Minor);
        Assert.Equal([1, 2, 3], tiers[0].Majors);
        Assert.Equal(20, tiers[1].Minor);
        Assert.Equal([4, 5, 6], tiers[1].Majors);
        Assert.Equal(30, tiers[2].Minor);
        Assert.Equal([7, 8, 9], tiers[2].Majors);
    }

    [Fact]
    public void A_short_list_draws_nothing_rather_than_half_a_grid()
    {
        // A specialization the API has changed out from under this build. Half a grid looks
        // like a build with traits missing, which is a worse lie than an absent panel.
        Assert.Empty(TraitGrid.Split(Minors, [1, 2, 3, 4, 5]));
        Assert.Empty(TraitGrid.Split([10, 20], Majors));
    }

    [Fact]
    public void Nothing_at_all_is_not_a_grid()
    {
        Assert.Empty(TraitGrid.Split(null, null));
        Assert.Empty(TraitGrid.Split([], []));
    }

    [Fact]
    public void A_longer_list_keeps_the_three_tiers_it_understands()
    {
        // If a future specialization carries a fourth tier, three correct tiers beat four
        // guessed ones.
        var tiers = TraitGrid.Split([10, 20, 30, 40], [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12]);

        Assert.Equal(3, tiers.Count);
        Assert.Equal([7, 8, 9], tiers[2].Majors);
    }

    [Fact]
    public void The_shape_check_is_the_same_one_the_split_uses()
    {
        Assert.True(TraitGrid.IsComplete(Minors, Majors));
        Assert.False(TraitGrid.IsComplete(Minors, [1, 2, 3]));
        Assert.False(TraitGrid.IsComplete(null, Majors));
    }
}
