using System.Globalization;

using GuildWarsCompanion.Gw2;
using GuildWarsCompanion.Services;

using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// Pairing a catalogue with what the account owns.
/// </summary>
public class UnlocksTests
{
    private static UnlockEntry Entry(int id, string name) => new(id, name, false);

    private static DyeColor Dye(IReadOnlyList<string>? categories) =>
        new(1, "Sky", [128, 26, 26], new DyeMaterial([1, 2, 3]), null, null, categories);

    [Fact]
    public void Owned_entries_are_marked_and_come_first()
    {
        var paired = Unlocks.Pair(
            [Entry(1, "Red"), Entry(2, "Blue"), Entry(3, "Gold")],
            [3]);

        Assert.Equal("Gold", paired[0].Name);
        Assert.True(paired[0].Owned);
        Assert.All(paired.Skip(1), entry => Assert.False(entry.Owned));
    }

    /// <summary>
    /// The API returns dyes in the order the game groups them, which is more use than
    /// alphabetical — so within each half that order has to survive.
    /// </summary>
    [Fact]
    public void The_catalogues_own_order_is_kept_within_each_half()
    {
        var paired = Unlocks.Pair(
            [Entry(1, "First"), Entry(2, "Second"), Entry(3, "Third"), Entry(4, "Fourth")],
            [3, 1]);

        Assert.Equal(["First", "Third", "Second", "Fourth"], paired.Select(entry => entry.Name));
    }

    [Fact]
    public void An_account_with_nothing_still_gets_the_whole_catalogue()
    {
        var paired = Unlocks.Pair([Entry(1, "Red"), Entry(2, "Blue")], null);

        Assert.Equal(2, paired.Count);
        Assert.All(paired, entry => Assert.False(entry.Owned));
    }

    /// <summary>
    /// A dye pulled from the game does not leave the accounts that own it, so the account can
    /// carry an id the public catalogue no longer lists. The count has to describe the rows
    /// actually drawn, or the heading and the list disagree.
    /// </summary>
    [Fact]
    public void The_count_describes_the_rows_rather_than_the_accounts_ids()
    {
        var paired = Unlocks.Pair([Entry(1, "Red"), Entry(2, "Blue")], [1, 99]);

        Assert.Equal("1 of 2 unlocked", Unlocks.Counted(paired));
    }

    [Fact]
    public void A_tally_reports_a_fraction_and_never_divides_by_zero()
    {
        var category = Unlocks.All[0];

        Assert.Equal(0.5, new UnlockTally(category, 5, 10).Fraction, 3);
        Assert.Equal(0, new UnlockTally(category, 0, 0).Fraction);

        // More owned than the catalogue lists, for the retired-dye case above.
        Assert.Equal(1, new UnlockTally(category, 12, 10).Fraction);
    }

    /// <summary>
    /// Grouped, because "14388" beside "1204" is a smear — and grouped the way the handset
    /// writes numbers, not the way this machine happens to. A German phone reads 14.388.
    /// </summary>
    [Fact]
    public void A_tally_groups_its_thousands_the_way_the_phone_does()
    {
        var was = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("en-GB");
            Assert.Equal("1,204 of 14,388", new UnlockTally(Unlocks.All[1], 1204, 14388).Line);

            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal("1.204 of 14.388", new UnlockTally(Unlocks.All[1], 1204, 14388).Line);
        }
        finally
        {
            CultureInfo.CurrentCulture = was;
        }
    }

    [Fact]
    public void A_swatch_is_hex_from_the_three_channels()
    {
        Assert.Equal("#8A2F2F", Unlocks.Swatch([138, 47, 47]));
        Assert.Null(Unlocks.Swatch(null));
        Assert.Null(Unlocks.Swatch([12, 34]));
    }

    /// <summary>
    /// The API has published channels outside 0-255 before now, and a swatch built from one
    /// would either throw or wrap to a confident wrong colour.
    /// </summary>
    [Fact]
    public void A_channel_out_of_range_is_clamped_rather_than_wrapped()
    {
        Assert.Equal("#FF0000", Unlocks.Swatch([300, -5, 0]));
    }

    /// <summary>
    /// base_rgb is [128, 26, 26] for every dye in the game — the neutral base the material
    /// shifts are applied to, not the colour. A swatch built from it drew six hundred identical
    /// red squares that looked entirely deliberate, which is why the colour comes from the
    /// material blocks and this test names the field that must not be used.
    /// </summary>
    [Fact]
    public void A_dyes_colour_comes_from_its_material_and_never_from_base_rgb()
    {
        var dye = new DyeColor(
            1, "Deep Blue",
            BaseRgb: [128, 26, 26],
            Cloth: new DyeMaterial([47, 74, 138]),
            Leather: new DyeMaterial([50, 70, 130]),
            Metal: new DyeMaterial([60, 80, 140]));

        Assert.Equal("#2F4A8A", Unlocks.Of(dye));
    }

    [Fact]
    public void A_dye_with_no_cloth_falls_back_to_the_materials_it_does_publish()
    {
        var leatherOnly = new DyeColor(
            2, "Odd", BaseRgb: [128, 26, 26], Cloth: null,
            Leather: new DyeMaterial([10, 20, 30]), Metal: null);

        Assert.Equal("#0A141E", Unlocks.Of(leatherOnly));

        var nothing = new DyeColor(3, "Blank", [128, 26, 26], null, null, null);

        Assert.Null(Unlocks.Of(nothing));
    }

    /// <summary>
    /// The API publishes a dye's categories as a bare array — ["Blue", "Vibrant", "Rare"] —
    /// with position as the only thing saying which is which. Rarity is the third.
    /// </summary>
    [Fact]
    public void A_dyes_rarity_is_the_third_of_its_three_categories()
    {
        Assert.Equal("Rare", Unlocks.RarityOf(Dye(["Blue", "Vibrant", "Rare"])));
        Assert.Equal("Exclusive", Unlocks.RarityOf(Dye(["Gray", "Metal", "Exclusive"])));
    }

    /// <summary>
    /// Dye Remover carries an empty array. Reading index two of it would throw on a screen made
    /// of six hundred dyes, so the length is checked rather than assumed.
    /// </summary>
    [Fact]
    public void A_dye_with_no_categories_has_no_rarity_and_does_not_throw()
    {
        Assert.Null(Unlocks.RarityOf(Dye([])));
        Assert.Null(Unlocks.RarityOf(Dye(null)));
        Assert.Null(Unlocks.RarityOf(Dye(["Blue", "Vibrant"])));
    }

    [Fact]
    public void Rarity_ranks_rarest_highest_and_the_unknown_lowest()
    {
        Assert.True(Unlocks.Rank("Exclusive") > Unlocks.Rank("Rare"));
        Assert.True(Unlocks.Rank("Rare") > Unlocks.Rank("Uncommon"));
        Assert.True(Unlocks.Rank("Uncommon") > Unlocks.Rank("Common"));
        Assert.True(Unlocks.Rank("Common") > Unlocks.Rank("Starter"));
        Assert.True(Unlocks.Rank("Starter") > Unlocks.Rank(null));
        Assert.Equal(0, Unlocks.Rank("nonsense"));
    }

    /// <summary>
    /// Unlocked first, rarest first within that. The grid answers "what have I got" before it
    /// answers "how good is it".
    /// </summary>
    [Fact]
    public void Unlocked_leads_and_rarity_orders_within_it()
    {
        var paired = Unlocks.Pair(
            [
                new UnlockEntry(1, "Owned common", false, null, "Common"),
                new UnlockEntry(2, "Locked exclusive", false, null, "Exclusive"),
                new UnlockEntry(3, "Owned exclusive", false, null, "Exclusive"),
                new UnlockEntry(4, "Owned starter", false, null, "Starter")
            ],
            [1, 3, 4]);

        Assert.Equal(
            ["Owned exclusive", "Owned common", "Owned starter", "Locked exclusive"],
            paired.Select(entry => entry.Name));
    }

    /// <summary>
    /// One heading per rarity for what is owned, rarest first, then everything locked under one.
    /// </summary>
    [Fact]
    public void Groups_run_rarest_first_and_end_with_one_locked_group()
    {
        var groups = Unlocks.Group(
        [
            new UnlockEntry(1, "Owned common", true, null, "Common"),
            new UnlockEntry(2, "Owned exclusive", true, null, "Exclusive"),
            new UnlockEntry(3, "Owned rare", true, null, "Rare"),
            new UnlockEntry(4, "Locked exclusive", false, null, "Exclusive"),
            new UnlockEntry(5, "Locked starter", false, null, "Starter")
        ]);

        Assert.Equal(
            ["Exclusive", "Rare", "Common", Unlocks.Locked],
            groups.Select(group => group.Rarity));

        // The locked group takes them whatever their rarity — that is the point of it.
        Assert.Equal(2, groups[^1].Entries.Count);
    }

    [Fact]
    public void Everything_ends_up_in_exactly_one_group()
    {
        var entries = Enumerable.Range(1, 20)
            .Select(i => new UnlockEntry(i, $"#{i}", i % 3 == 0, null,
                new[] { "Exclusive", "Rare", "Common", null }[i % 4]))
            .ToList();

        var groups = Unlocks.Group(entries);

        Assert.Equal(entries.Count, groups.Sum(group => group.Entries.Count));
        Assert.Equal(
            entries.Select(entry => entry.Id).OrderBy(id => id),
            groups.SelectMany(group => group.Entries).Select(entry => entry.Id).OrderBy(id => id));
    }

    /// <summary>An unlocked entry with no rarity still has to land somewhere.</summary>
    [Fact]
    public void An_owned_entry_with_no_rarity_goes_to_its_own_heading_and_sorts_last()
    {
        var groups = Unlocks.Group(
        [
            new UnlockEntry(1, "Nameless", true, null, null),
            new UnlockEntry(2, "Owned rare", true, null, "Rare")
        ]);

        Assert.Equal(["Rare", Unlocks.Other], groups.Select(group => group.Rarity));
    }

    [Fact]
    public void A_category_with_nothing_locked_has_no_locked_heading()
    {
        var groups = Unlocks.Group([new UnlockEntry(1, "Owned", true, null, "Rare")]);

        Assert.Equal(["Rare"], groups.Select(group => group.Rarity));
    }

    [Fact]
    public void A_heading_is_written_the_way_the_screen_writes_it()
    {
        var group = Unlocks.Group([new UnlockEntry(1, "Owned", true, null, "Exclusive")])[0];

        Assert.Equal("EXCLUSIVE", group.Heading);
        Assert.Equal("1", group.Line);
    }

    [Fact]
    public void Every_category_names_two_endpoints_and_they_differ()
    {
        Assert.NotEmpty(Unlocks.All);

        foreach (var category in Unlocks.All)
        {
            Assert.StartsWith("account/", category.Account);
            Assert.False(category.Catalogue.StartsWith("account/"));
            Assert.False(string.IsNullOrWhiteSpace(category.Name));
        }
    }

    /// <summary>
    /// One kind per category. A duplicate would have two rows sharing a cache entry, so opening
    /// one would show the other's contents.
    /// </summary>
    [Fact]
    public void No_two_categories_share_a_kind()
    {
        Assert.Equal(
            Unlocks.All.Count,
            Unlocks.All.Select(category => category.Kind).Distinct().Count());
    }

    /// <summary>Skins are the only category the screen cannot list; everything else is
    /// browsable, and that is what the page's two halves are built on.</summary>
    [Fact]
    public void Only_skins_are_count_only()
    {
        Assert.Equal(
            [UnlockKind.Skin],
            Unlocks.All.Where(c => !c.Browsable).Select(c => c.Kind));
    }
}
