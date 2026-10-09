using GuildWarsCompanion.Gw2;
using GuildWarsCompanion.Services;
using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// Reading a character's equipment templates.
///
/// Picking the wrong one is a quiet failure: the screen fills with real gear that the
/// character is not wearing, and nothing about it looks wrong.
/// </summary>
public class LoadoutsTests
{
    private static EquipmentTab Tab(
        int number, bool active = false, string? name = null, params string[] slots) =>
        new(number, name, active,
            slots.Select((slot, i) => new EquipmentPiece(100 + i, slot, "Equipped", null, null))
                .ToList(),
            null);

    [Fact]
    public void The_worn_template_is_shown_by_default()
    {
        var tabs = new[] { Tab(1), Tab(2, active: true), Tab(3) };

        Assert.Equal(2, Loadouts.Pick(tabs, null)?.Tab);
    }

    [Fact]
    public void Is_active_beats_the_tab_number()
    {
        // The numbers are positions, and nothing promises the worn one comes first. Taking
        // tab 1 on faith would show the wrong gear on every character that swapped.
        var tabs = new[] { Tab(1), Tab(2, active: true) };

        Assert.False(Loadouts.Pick(tabs, null)?.Tab == 1);
    }

    [Fact]
    public void Asking_for_a_template_overrides_the_worn_one()
    {
        var tabs = new[] { Tab(1), Tab(2, active: true) };

        Assert.Equal(1, Loadouts.Pick(tabs, 1)?.Tab);
    }

    [Fact]
    public void A_template_that_no_longer_exists_falls_back_to_the_worn_one()
    {
        // Templates can be locked again, and a stale selection must not empty the screen.
        var tabs = new[] { Tab(1), Tab(2, active: true) };

        Assert.Equal(2, Loadouts.Pick(tabs, 7)?.Tab);
    }

    [Fact]
    public void With_nothing_marked_active_the_first_stands_in()
    {
        var tabs = new[] { Tab(3), Tab(4) };

        Assert.Equal(3, Loadouts.Pick(tabs, null)?.Tab);
    }

    [Fact]
    public void No_templates_at_all_is_not_an_error()
    {
        // A key without the 'builds' permission returns none, and the screen still has to load.
        Assert.Null(Loadouts.Pick(null, null));
        Assert.Null(Loadouts.Pick([], 1));
        Assert.Empty(Loadouts.Worn(null));
    }

    [Fact]
    public void An_unnamed_template_is_called_by_its_number()
    {
        // The API sends an empty string rather than null for the common case.
        Assert.Equal("Tab 2", Loadouts.Label(Tab(2)));
        Assert.Equal("Tab 2", Loadouts.Label(Tab(2, name: "   ")));
        Assert.Equal("Open world", Loadouts.Label(Tab(2, name: " Open world ")));
    }

    [Fact]
    public void Legendary_gear_counts_as_worn()
    {
        // It reports as drawn from the armory rather than equipped directly. Filtering on
        // "Equipped" alone would strip a legendary character down to nothing.
        var tab = new EquipmentTab(1, null, true,
        [
            new EquipmentPiece(1, "Coat", "EquippedFromLegendaryArmory", null, null),
            new EquipmentPiece(2, "Helm", "Equipped", null, null),
            new EquipmentPiece(3, "Boots", "Bank", null, null)
        ], null);

        Assert.Equal(2, Loadouts.Worn(tab).Count);
    }

    [Fact]
    public void A_piece_with_no_slot_is_not_worn_anywhere()
    {
        var tab = new EquipmentTab(1, null, true,
            [new EquipmentPiece(1, null, "Equipped", null, null)], null);

        Assert.Empty(Loadouts.Worn(tab));
    }

    [Fact]
    public void An_empty_template_is_not_a_permission_problem()
    {
        // Found on the phone: switching to an unused template blamed the API key, which had
        // just loaded the other template perfectly. That sends someone off to reissue a key
        // that was never wrong.
        Assert.Equal("Nothing is equipped in this template.",
            Loadouts.EmptyNotice(pieces: 0, templates: 2));
    }

    [Fact]
    public void No_templates_and_no_gear_really_is_the_key()
    {
        Assert.Equal("No equipment returned. The API key needs the 'builds' permission to read it.",
            Loadouts.EmptyNotice(pieces: 0, templates: 0));
    }

    [Fact]
    public void Gear_on_screen_needs_no_explanation()
    {
        Assert.Null(Loadouts.EmptyNotice(pieces: 12, templates: 0));
    }

    [Fact]
    public void An_untouched_template_has_no_pvp_gear()
    {
        // What the API actually sends for a character that has never set a PvP build: every
        // field present, every value null. Six empty rows would be a screen full of nothing.
        Assert.False(Loadouts.HasPvpGear(new PvpLoadout(null, null, [null, null, null, null])));
        Assert.False(Loadouts.HasPvpGear(null));
    }

    [Fact]
    public void One_sigil_is_enough_to_be_worth_showing()
    {
        Assert.True(Loadouts.HasPvpGear(new PvpLoadout(null, null, [null, 24615, null, null])));
        Assert.True(Loadouts.HasPvpGear(new PvpLoadout(4, null, null)));
    }
}
