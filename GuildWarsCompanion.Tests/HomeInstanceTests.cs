using GuildWarsCompanion.Services;
using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// The home instance checklist.
///
/// Both halves have to be right for opposite reasons: what is missing is a shopping list, and
/// listing something already owned sends someone to a vendor for nothing; what is present is a
/// daily gathering round, and dropping one from it costs a node every day.
/// </summary>
public class HomeInstanceTests
{
    private static (string, string, string) Node(string id) => (id, HomeInstance.NodeName(id), "");

    [Fact]
    public void A_node_id_reads_as_a_name()
    {
        Assert.Equal("Flaxseed", HomeInstance.NodeName("flaxseed_node"));
        Assert.Equal("Enchanted Treasure Chest", HomeInstance.NodeName("enchanted_treasure_chest"));
    }

    [Fact]
    public void The_trailing_node_is_dropped_as_a_stutter()
    {
        // Under a heading that already says nodes, "Elder Wood Node" says it twice.
        Assert.Equal("Elder Wood", HomeInstance.NodeName("elder_wood_node"));
        Assert.Equal("Eternal Ice Shard", HomeInstance.NodeName("eternal_ice_shard_node"));
    }

    [Fact]
    public void A_node_that_is_only_called_node_keeps_its_name()
    {
        // Dropping the word unconditionally would leave an empty label.
        Assert.Equal("Node", HomeInstance.NodeName("node"));
    }

    [Fact]
    public void Numbered_plots_read_as_numbers()
    {
        // The ids pad them: garden_plot_01 through garden_plot_08.
        Assert.Equal("Garden Plot 1", HomeInstance.NodeName("garden_plot_01"));
        Assert.Equal("Garden Plot 12", HomeInstance.NodeName("garden_plot_12"));
    }

    [Fact]
    public void A_cat_is_named_by_what_it_wants()
    {
        // The API never names the cats. The hint is the food you leave out, so it is the
        // answer rather than a clue to one.
        Assert.Equal("Grilled Chicken", HomeInstance.CatLure("grilled_chicken"));
        Assert.Equal("Spicy Flank", HomeInstance.CatLure("spicy_flank"));
    }

    [Fact]
    public void A_cat_with_no_hint_says_so_rather_than_showing_a_blank()
    {
        Assert.Equal("Unknown", HomeInstance.CatLure(null));
        Assert.Equal("Unknown", HomeInstance.CatLure("  "));
    }

    [Fact]
    public void What_is_missing_leads()
    {
        var all = new[] { Node("elder_wood_node"), Node("flaxseed_node"), Node("dragon_crystal") };

        var list = HomeInstance.Checklist(all, ["elder_wood_node"]);

        Assert.Equal(["Flaxseed", "Dragon Crystal", "Elder Wood"], list.Select(x => x.Name));
        Assert.False(list[0].Unlocked);
        Assert.True(list[2].Unlocked);
    }

    [Fact]
    public void Order_within_each_half_is_the_catalogues_own()
    {
        // Alphabetical for nodes, release order for cats. Re-sorting either would scramble a
        // list people scan by position.
        var all = new[] { Node("a_node"), Node("b_node"), Node("c_node") };

        Assert.Equal(["A", "B", "C"], HomeInstance.Checklist(all, null).Select(x => x.Name));
    }

    [Fact]
    public void The_two_lists_are_matched_without_regard_to_case()
    {
        // The catalogue and the account list come from different endpoints, and nothing
        // promises they spell an id the same way. A case-sensitive match would quietly send
        // someone to a vendor for a node already in their instance.
        var list = HomeInstance.Checklist([Node("flaxseed_node")], ["Flaxseed_Node"]);

        Assert.True(Assert.Single(list).Unlocked);
    }

    [Fact]
    public void With_no_account_data_everything_reads_as_still_to_get()
    {
        // A key without the scopes still gets the catalogue, and nothing may be claimed as
        // owned on its behalf.
        var list = HomeInstance.Checklist([Node("flaxseed_node")], null);

        Assert.False(Assert.Single(list).Unlocked);
    }

    [Fact]
    public void The_summary_says_what_is_left_rather_than_leaving_it_to_be_worked_out()
    {
        Assert.Equal("45 of 57 nodes · 12 still to unlock", HomeInstance.Summarise(57, 45, "nodes"));
    }

    [Fact]
    public void A_finished_collection_is_congratulated_not_counted()
    {
        Assert.Equal("All 35 cats", HomeInstance.Summarise(35, 35, "cats"));
    }

    [Fact]
    public void Not_knowing_is_not_the_same_as_owning_none()
    {
        // A key without the scopes, or a request that failed. Printing "0 of 57" there would
        // report an empty home instance to someone whose instance is full.
        Assert.Equal("57 nodes in the game", HomeInstance.Summarise(57, null, "nodes"));
    }

    [Fact]
    public void Owning_none_is_still_said_plainly()
    {
        // Genuinely nothing yet — which the API does report, and which reads differently.
        Assert.Equal("0 of 57 nodes · 57 still to unlock", HomeInstance.Summarise(57, 0, "nodes"));
    }

    [Fact]
    public void An_empty_catalogue_says_nothing_at_all()
    {
        // Offline before the catalogue was ever seen: "0 of 0" reads as a broken screen.
        Assert.Equal(string.Empty, HomeInstance.Summarise(0, 0, "nodes"));
        Assert.Equal(string.Empty, HomeInstance.Summarise(0, null, "nodes"));
    }
}
