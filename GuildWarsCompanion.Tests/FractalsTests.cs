using GuildWarsCompanion.Gw2;
using GuildWarsCompanion.Services;

using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// The daily fractals, read out of the achievement names the API gives them.
///
/// The names are the ones /v2/achievements/categories/88 named on 2026-10-13: three fractals at
/// four tiers each, and three recommended scales.
/// </summary>
public class FractalsTests
{
    private static readonly FractalDay Day = Fractals.Read(
    [
        Named(2927, "Daily Tier 1 Aetherblade"), Named(2909, "Daily Tier 2 Aetherblade"),
        Named(2928, "Daily Tier 3 Aetherblade"), Named(2948, "Daily Tier 4 Aetherblade"),
        Named(2981, "Daily Tier 1 Thaumanova Reactor"), Named(2991, "Daily Tier 2 Thaumanova Reactor"),
        Named(2895, "Daily Tier 3 Thaumanova Reactor"), Named(2967, "Daily Tier 4 Thaumanova Reactor"),
        Named(2940, "Daily Tier 1 Uncategorized"), Named(2902, "Daily Tier 2 Uncategorized"),
        Named(2925, "Daily Tier 3 Uncategorized"), Named(2939, "Daily Tier 4 Uncategorized"),
        Named(3198, "Daily Recommended Fractal—Scale 61"), Named(2229, "Daily Recommended Fractal—Scale 6"),
        Named(2297, "Daily Recommended Fractal—Scale 28")
    ]);

    [Fact]
    public void The_day_is_three_fractals_and_three_scales()
    {
        Assert.Equal(["Aetherblade", "Thaumanova Reactor", "Uncategorized"], Day.Fractals);
        Assert.Equal(12, Day.Tiers.Count);
        Assert.Equal([6, 28, 61], Day.Recommended.Select(scale => scale.Scale));
    }

    /// <summary>A name of another shape is left out rather than guessed at.</summary>
    [Fact]
    public void An_achievement_of_another_shape_is_left_out()
    {
        var day = Fractals.Read([Named(1, "Daily Fractal Mastery"), Named(2, "Daily Tier 5 Aetherblade")]);

        Assert.True(day.IsEmpty);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(25, 1)]
    [InlineData(26, 2)]
    [InlineData(50, 2)]
    [InlineData(51, 3)]
    [InlineData(75, 3)]
    [InlineData(76, 4)]
    [InlineData(100, 4)]
    public void A_fractal_level_reaches_the_tier_its_daily_asks_for(int level, int tier)
    {
        Assert.Equal(tier, Fractals.TierFor(level));
    }

    /// <summary>Tier 4 at level 87: three fractals and three scales, one of each done.</summary>
    [Fact]
    public void Each_fractal_counts_at_the_account_s_own_tier()
    {
        var tasks = Fractals.Tasks(Day, 87, new HashSet<int> { 2948, 2909, 2297 });

        Assert.Equal(6, tasks.Count);
        Assert.Equal(
            [("Aetherblade", true), ("Thaumanova Reactor", false), ("Uncategorized", false),
                ("Recommended scale 6", false), ("Recommended scale 28", true), ("Recommended scale 61", false)],
            tasks.Select(task => (task.Name, task.Done)));
        Assert.Equal("Tiers done: 2, 4", tasks[0].Note);
        Assert.Equal("Tier 4 to do", tasks[1].Note);
    }

    /// <summary>Tier 2 done counts nothing for a tier 4 player, and nothing hides that it was run.</summary>
    [Fact]
    public void A_lower_tier_done_does_not_finish_the_fractal()
    {
        var tasks = Fractals.Tasks(Day, 87, new HashSet<int> { 2991 });

        var reactor = tasks.Single(task => task.Name == "Thaumanova Reactor");

        Assert.False(reactor.Done);
        Assert.Equal("Tiers done: 2", reactor.Note);
    }

    [Fact]
    public void A_lower_level_aims_at_its_own_tier()
    {
        var tasks = Fractals.Tasks(Day, 30, new HashSet<int> { 2909 });

        Assert.True(tasks.Single(task => task.Name == "Aetherblade").Done);
        Assert.Equal("Tier 2 to do", tasks.Single(task => task.Name == "Uncategorized").Note);
    }

    /// <summary>Scale 61 cannot be entered at level 30, so it is not left undone either.</summary>
    [Fact]
    public void A_scale_above_the_account_s_level_does_not_count()
    {
        var tasks = Fractals.Tasks(Day, 30, new HashSet<int>());

        var high = tasks.Single(task => task.Name == "Recommended scale 61");

        Assert.False(high.Counts);
        Assert.Equal("Above your fractal level", high.Note);
        Assert.Equal(5, tasks.Count(task => task.Counts));
    }

    /// <summary>No fractal level, no dailies, and Today shows no row at all.</summary>
    [Fact]
    public void An_account_that_never_ran_a_fractal_has_nothing_to_do()
    {
        Assert.Empty(Fractals.Tasks(Day, 0, new HashSet<int>()));
    }

    [Fact]
    public void A_fractal_with_no_daily_at_the_tier_counts_at_its_highest()
    {
        var day = Fractals.Read([Named(1, "Daily Tier 1 Molten Boss"), Named(2, "Daily Tier 2 Molten Boss")]);

        var task = Fractals.Tasks(day, 90, new HashSet<int> { 2 }).Single();

        Assert.True(task.Done);
    }

    [Fact]
    public void Tomorrow_is_one_line()
    {
        Assert.Equal("Aetherblade · Thaumanova Reactor · Uncategorized · scales 6, 28, 61", Fractals.Describe(Day));
    }

    private static Achievement Named(int id, string name) => new(id, name, null, null, null, ["Daily"], null);
}
