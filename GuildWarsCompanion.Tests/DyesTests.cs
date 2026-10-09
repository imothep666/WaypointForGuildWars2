using GuildWarsCompanion.Gw2;
using GuildWarsCompanion.Services;
using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// The colours a character is actually wearing.
///
/// The failure this guards against is silent and pretty: pick the wrong material and every
/// swatch is a real colour from the real dye, just not the one on the character.
/// </summary>
public class DyesTests
{
    private static DyeColor Dye(int id, string name) =>
        new(id, name, [9, 9, 9],
            new DyeMaterial([1, 1, 1]),
            new DyeMaterial([2, 2, 2]),
            new DyeMaterial([3, 3, 3]));

    private static EquipmentPiece Armour(string slot, params int?[] dyes) =>
        new(1, slot, "Equipped", null, null, dyes);

    [Fact]
    public void Armour_weight_decides_which_colour_a_dye_is()
    {
        Assert.Equal("cloth", Dyes.MaterialFor("Necromancer"));
        Assert.Equal("leather", Dyes.MaterialFor("Thief"));
        Assert.Equal("metal", Dyes.MaterialFor("Warrior"));
    }

    [Fact]
    public void An_unknown_profession_is_assumed_heavy()
    {
        // The three heavy professions are the ones left over, so a profession this does not
        // know is more likely new than light.
        Assert.Equal("metal", Dyes.MaterialFor("Bladesworn"));
        Assert.Equal("metal", Dyes.MaterialFor(null));
    }

    [Fact]
    public void The_material_picks_the_rgb()
    {
        var dye = Dye(1, "Shadow Abyss");

        Assert.Equal("#010101", Dyes.Hex(dye, "cloth"));
        Assert.Equal("#020202", Dyes.Hex(dye, "leather"));
        Assert.Equal("#030303", Dyes.Hex(dye, "metal"));
    }

    [Fact]
    public void A_missing_material_falls_back_to_the_dyes_own_colour()
    {
        // Losing the swatch entirely would be worse than showing the dye's base colour.
        var sparse = new DyeColor(1, "Odd", [9, 9, 9], null, null, null);

        Assert.Equal("#090909", Dyes.Hex(sparse, "cloth"));
    }

    [Fact]
    public void A_dye_with_no_colour_at_all_is_left_out()
    {
        Assert.Null(Dyes.Hex(new DyeColor(1, "Odd", null, null, null, null), "cloth"));
        Assert.Null(Dyes.Hex(null, "cloth"));
    }

    [Fact]
    public void Empty_channels_are_not_dyes()
    {
        // Unused channels come back as nulls in the array, not as a shorter array.
        var worn = new[] { Armour("Coat", 1, null, 2, null) };

        Assert.Equal([1, 2], Dyes.Channels(worn));
    }

    [Fact]
    public void Undyed_gear_has_no_palette()
    {
        Assert.Empty(Dyes.Channels([new EquipmentPiece(1, "Coat", "Equipped", null, null)]));
        Assert.Empty(Dyes.Channels(null));
    }

    [Fact]
    public void The_most_worn_dye_leads()
    {
        // A dye on seven pieces is the character's colour; one used once is a trim. Order of
        // arrival would bury the first under the second.
        var worn = new[]
        {
            Armour("Coat", 1, 2, 2, null),
            Armour("Boots", 2, null, null, null)
        };

        var colors = new Dictionary<int, DyeColor> { [1] = Dye(1, "Trim"), [2] = Dye(2, "Main") };

        var palette = Dyes.Palette(worn, colors, "Thief");

        Assert.Equal("Main", palette[0].Name);
        Assert.Equal(3, palette[0].Uses);
        Assert.Equal("Trim", palette[1].Name);
        Assert.Equal(1, palette[1].Uses);
    }

    [Fact]
    public void The_palette_is_mixed_for_the_characters_own_armour()
    {
        var worn = new[] { Armour("Coat", 1) };
        var colors = new Dictionary<int, DyeColor> { [1] = Dye(1, "Shadow Abyss") };

        Assert.Equal("#010101", Dyes.Palette(worn, colors, "Mesmer")[0].Hex);
        Assert.Equal("#030303", Dyes.Palette(worn, colors, "Guardian")[0].Hex);
    }

    [Fact]
    public void A_dye_the_colour_endpoint_did_not_return_is_dropped()
    {
        // Rather than a blank swatch with no name, which reads as a bug in the screen.
        var worn = new[] { Armour("Coat", 1, 5) };
        var colors = new Dictionary<int, DyeColor> { [1] = Dye(1, "Known") };

        Assert.Single(Dyes.Palette(worn, colors, "Thief"));
    }

    [Fact]
    public void A_single_use_says_nothing_about_its_count()
    {
        var worn = new[] { Armour("Coat", 1) };
        var colors = new Dictionary<int, DyeColor> { [1] = Dye(1, "Once") };

        Assert.Equal(string.Empty, Dyes.Palette(worn, colors, "Thief")[0].UsesText);
    }
}
