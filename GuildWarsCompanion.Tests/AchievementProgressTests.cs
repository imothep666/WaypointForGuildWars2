using GuildWarsCompanion.Services;
using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// Choosing which achievements to put in front of someone.
///
/// An account has touched thousands; the screen shows a couple of dozen. The ordering is the
/// entire product, and a wrong one buries the two-kills-to-go achievement under a project.
/// </summary>
public class AchievementProgressTests
{
    private static Tracked At(int id, int current, int max, bool done = false) =>
        new(id, current, max, done);

    [Fact]
    public void The_nearest_to_finishing_comes_first()
    {
        var closest = AchievementProgress.Closest(
            [At(1, 1, 10), At(2, 9, 10), At(3, 5, 10)], 10);

        Assert.Equal([2, 3, 1], closest.Select(a => a.Id));
    }

    [Fact]
    public void Finished_achievements_are_not_offered() =>
        Assert.Empty(AchievementProgress.Closest([At(1, 10, 10, done: true)], 10));

    [Fact]
    public void An_achievement_at_its_target_but_not_flagged_done_is_still_excluded()
    {
        // Nothing left to do on it, so it is not something to go and finish.
        Assert.Empty(AchievementProgress.Closest([At(1, 10, 10)], 10));
    }

    [Fact]
    public void Untouched_achievements_are_not_close_to_anything()
    {
        // Most of the eight thousand sit at zero. One needing a single kill would otherwise
        // outrank everything the player has actually been working on.
        Assert.Empty(AchievementProgress.Closest([At(1, 0, 1)], 10));
    }

    [Fact]
    public void An_achievement_with_no_target_is_ignored()
    {
        // Some report max 0; dividing by it would be a crash, ranking it would be a lie.
        Assert.Empty(AchievementProgress.Closest([At(1, 5, 0)], 10));
    }

    [Fact]
    public void A_tie_on_proportion_goes_to_the_smaller_job()
    {
        // Both are 90% done. Two kills is an evening; two hundred is a project.
        var closest = AchievementProgress.Closest([At(1, 1800, 2000), At(2, 18, 20)], 10);

        Assert.Equal(2, closest[0].Id);
    }

    [Fact]
    public void Only_as_many_as_asked_for_are_returned()
    {
        var many = Enumerable.Range(1, 50).Select(i => At(i, i, 100)).ToList();

        Assert.Equal(5, AchievementProgress.Closest(many, 5).Count);
    }

    [Fact]
    public void The_order_is_stable_for_identical_standings()
    {
        // Same proportion, same remaining: id decides, so the list does not reshuffle between
        // refreshes and move a row out from under a tapping finger.
        var closest = AchievementProgress.Closest([At(7, 5, 10), At(3, 5, 10)], 10);

        Assert.Equal([3, 7], closest.Select(a => a.Id));
    }

    // --- wording ------------------------------------------------------------

    [Fact]
    public void Progress_shows_the_counts_not_a_percentage() =>
        Assert.Equal("8 of 10", AchievementProgress.ProgressText(At(1, 8, 10)));

    [Fact]
    public void Large_counts_are_grouped_for_reading() =>
        Assert.Equal("1,800 of 2,000", AchievementProgress.ProgressText(At(1, 1800, 2000))
            .Replace(".", ","));

    [Theory]
    [InlineData(9, 10, "1 to go")]
    [InlineData(8, 10, "2 to go")]
    public void What_is_left_is_spelled_out(int current, int max, string expected) =>
        Assert.Equal(expected, AchievementProgress.RemainingText(At(1, current, max)));

    [Fact]
    public void Fraction_never_leaves_its_bounds()
    {
        // The API has been known to report current above max on retuned achievements.
        Assert.Equal(1, At(1, 20, 10).Fraction);
        Assert.Equal(0, At(1, 5, 0).Fraction);
        Assert.Equal(0, At(1, 20, 10).Remaining);
    }

    // --- the instruction ----------------------------------------------------

    [Fact]
    public void The_tier_target_is_spliced_into_the_gap()
    {
        // The API ships one string per achievement and leaves a gap where the tier's number
        // goes: "Kill  centaurs." Printed raw it reads like a typo.
        Assert.Equal("Kill 100 centaurs.", AchievementProgress.Requirement("Kill  centaurs.", 100));
    }

    [Fact]
    public void A_gap_mid_sentence_is_filled_in_place() =>
        Assert.Equal(
            "Collect all 7 items and skins to forge your Mistward Legguards.",
            AchievementProgress.Requirement(
                "Collect all  items and skins to forge your Mistward Legguards.", 7));

    [Fact]
    public void Large_targets_are_grouped_for_reading() =>
        Assert.Contains("1,000", AchievementProgress.Requirement("Kill  centaurs.", 1000)!
            .Replace(".", ","));

    [Fact]
    public void A_requirement_with_no_gap_is_left_alone() =>
        Assert.Equal(
            "Complete the Invocation specialization.",
            AchievementProgress.Requirement("Complete the Invocation specialization.", 5));

    [Fact]
    public void With_no_target_the_gap_is_closed_rather_than_shown()
    {
        // A tell-tale double space reads as a rendering bug.
        var text = AchievementProgress.Requirement("Kill  centaurs.", 0);

        Assert.Equal("Kill centaurs.", text);
        Assert.DoesNotContain("  ", text);
    }

    [Fact]
    public void Only_the_first_gap_is_filled()
    {
        // One number belongs to the achievement; a second gap is not a second target.
        Assert.Equal(
            "Kill 10 centaurs  quickly.",
            AchievementProgress.Requirement("Kill  centaurs  quickly.", 10));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Nothing_to_say_says_nothing(string? requirement) =>
        Assert.Null(AchievementProgress.Requirement(requirement, 10));

    // --- what the tier pays -------------------------------------------------

    [Fact]
    public void The_tier_being_worked_on_is_the_one_matching_the_target()
    {
        // A slayer achievement at tiers 10/100/500 reporting max 100 is on the second tier.
        var tiers = new[] { (10, 1), (100, 5), (500, 10) };

        Assert.Equal(5, AchievementProgress.PointsForTier(tiers, 100));
    }

    [Fact]
    public void An_unmatched_target_yields_nothing_rather_than_a_guess()
    {
        // Happens when an achievement is retuned; inventing a number would be worse.
        Assert.Null(AchievementProgress.PointsForTier([(10, 1), (100, 5)], 250));
        Assert.Null(AchievementProgress.PointsForTier(null, 100));
    }
}
