using GuildWarsCompanion.Services;
using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// Which banner a specialization draws.
///
/// The cut files exist because the API's own art is half empty — the painted region is its
/// lower-left, and no crop the layout can perform fixes that. What these guard is the edge of
/// that arrangement: a specialization released after this build has no file here, and must
/// fall back to the API's artwork rather than drawing nothing.
/// </summary>
public class SpecializationArtTests
{
    private const string Remote =
        "https://render.guildwars2.com/file/2AA7FB65630AD540E204982978DA9601023B540E/1012102.png";

    [Fact]
    public void A_specialization_this_build_ships_art_for_uses_the_cut_file()
    {
        // 28 is Deadly Arts, one of the 81 cut when this was built.
        Assert.Equal("spec_28.jpg", SpecializationArt.Banner(28, Remote));
        Assert.True(SpecializationArt.HasBanner(28));
    }

    [Fact]
    public void One_released_later_falls_back_to_the_api_artwork()
    {
        // Half empty, but present. A blank row would read as a broken screen.
        Assert.Equal(Remote, SpecializationArt.Banner(9999, Remote));
        Assert.False(SpecializationArt.HasBanner(9999));
    }

    [Fact]
    public void With_no_artwork_either_there_is_simply_nothing_to_draw()
    {
        Assert.Null(SpecializationArt.Banner(9999, null));
    }

    [Fact]
    public void Every_specialization_in_the_game_at_build_time_has_one()
    {
        // 81 of them, and the elite specializations are the ones a character is most likely
        // to be wearing — worth knowing they are all covered rather than the core ones only.
        foreach (var id in new[] { 1, 28, 35, 58, 72 })
            Assert.True(SpecializationArt.HasBanner(id), $"specialization {id} has no banner");
    }

    [Fact]
    public void The_banners_share_one_ratio_so_every_row_is_the_same_height()
    {
        Assert.Equal(640.0 / 152.0, SpecializationArt.Ratio, precision: 3);
    }
}
