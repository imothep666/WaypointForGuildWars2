using GuildWarsCompanion.Services;
using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// The daily-reset endpoints return bare ids and the API has no display names for them, so
/// this conversion is the only thing standing between the player and "Claw Of Jormag". Every
/// case here is an id the live API actually returned.
/// </summary>
public class IdsTests
{
    [Theory]
    [InlineData("charged_quartz_crystal", "Charged Quartz Crystal")]
    [InlineData("lump_of_mithrilium", "Lump of Mithrilium")]
    [InlineData("glob_of_elder_spirit_residue", "Glob of Elder Spirit Residue")]
    [InlineData("claw_of_jormag", "Claw of Jormag")]
    [InlineData("admiral_taidha_covington", "Admiral Taidha Covington")]
    [InlineData("inquest_golem_mark_ii", "Inquest Golem Mark Ii")]
    public void Reads_as_a_title(string id, string expected) =>
        Assert.Equal(expected, Ids.ToDisplayName(id));

    [Fact]
    public void Keeps_a_leading_joining_word_capitalised()
    {
        // "of" is only lower case when it joins; an id starting with one still opens a title.
        Assert.Equal("The Shatterer", Ids.ToDisplayName("the_shatterer"));
        Assert.Equal("A Wilder Hunt", Ids.ToDisplayName("a_wilder_hunt"));
    }

    [Fact]
    public void Leaves_apostrophes_alone_because_they_cannot_be_recovered()
    {
        // "heros_choice_chest" could be "hero's" or a plural; guessing would be wrong as
        // often as right, so the id's own spelling stands.
        Assert.Equal(
            "Auric Basin Heros Choice Chest",
            Ids.ToDisplayName("auric_basin_heros_choice_chest"));
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("_", "")]
    [InlineData("__a__b__", "A B")]
    [InlineData("x", "X")]
    public void Survives_malformed_ids(string id, string expected) =>
        Assert.Equal(expected, Ids.ToDisplayName(id));

    [Fact]
    public void Does_not_lower_case_the_body_of_a_word()
    {
        // Only the first letter is touched: an id that already carries capitals keeps them.
        Assert.Equal("WvW Reward Track", Ids.ToDisplayName("WvW_reward_track"));
    }
}
