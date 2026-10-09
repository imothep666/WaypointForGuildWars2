using GuildWarsCompanion.Services;

using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// The "Here:" line on Today. Its one way to be wrong is to claim something it did not read, so
/// most of these are about what it does not say.
/// </summary>
public class HereLineTests
{
    /// <summary>The ids as /v2/mapchests published them on 2026-10-05, in part.</summary>
    private static readonly string[] Chests =
    [
        "amnytas_heros_choice_chest", "auric_basin_heros_choice_chest", "convergence_heros_choice_chest",
        "dragons_stand_heros_choice_chest", "the_desolation_heros_choice_chest"
    ];

    [Theory]
    [InlineData("Auric Basin", "auric_basin_heros_choice_chest")]
    [InlineData("Dragon's Stand", "dragons_stand_heros_choice_chest")]
    [InlineData("The Desolation", "the_desolation_heros_choice_chest")]
    [InlineData("Amnytas", "amnytas_heros_choice_chest")]
    public void A_map_finds_its_chest_by_its_own_name(string map, string chest)
    {
        Assert.Equal(chest, HereLine.ChestFor(map, Chests));
    }

    [Fact]
    public void A_map_without_a_chest_finds_none()
    {
        Assert.Null(HereLine.ChestFor("Queensdale", Chests));
        // A prefix is not a match: "Auric" is not Auric Basin.
        Assert.Null(HereLine.ChestFor("Auric", Chests));
    }

    private static HereFacts Facts(
        bool? chest = null, int heroes = 0, int? left = null, params (string, TimeSpan)[] bosses) =>
        new(chest, heroes, left, bosses);

    [Fact]
    public void Everything_left_is_said_in_one_line()
    {
        var line = HereLine.Describe(Facts(false, 13, 2, ("Shadow Behemoth", TimeSpan.FromMinutes(25))));

        Assert.Equal("Here: map chest waiting · 2 of 13 hero points left · Shadow Behemoth in 25 min", line);
    }

    [Fact]
    public void A_boss_further_out_says_hours_and_one_up_says_so()
    {
        Assert.Equal("Here: Tequatl the Sunless in 1h 05m",
            HereLine.Describe(Facts(bosses: ("Tequatl the Sunless", TimeSpan.FromMinutes(65)))));
        Assert.Equal("Here: Tequatl the Sunless up now",
            HereLine.Describe(Facts(bosses: ("Tequatl the Sunless", TimeSpan.Zero))));
    }

    [Fact]
    public void Everything_read_and_done_says_nothing_left()
    {
        Assert.Equal("Here: nothing left", HereLine.Describe(Facts(true, 13, 0)));
        Assert.Equal("Here: nothing left", HereLine.Describe(Facts(chest: true)));
    }

    [Fact]
    public void Nothing_read_is_no_line_at_all()
    {
        // No chest on the map, hero points not read, no boss due: "nothing left" would be a guess.
        Assert.Null(HereLine.Describe(Facts()));
        Assert.Null(HereLine.Describe(Facts(heroes: 13, left: null)));
    }

    [Fact]
    public void Hero_points_not_read_are_left_out_rather_than_counted_as_all_left()
    {
        Assert.Equal("Here: map chest waiting", HereLine.Describe(Facts(false, 13, null)));
    }
}
