using GuildWarsCompanion.Gw2;
using GuildWarsCompanion.Services;

using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// The Wizard's Vault shop. The API under-reports purchases — on the player's own account the
/// "/bless" tome showed as bought while two Ascended Armor Chests showed 0 of 3 — so a reported
/// purchase is a floor and a 0 says nothing.
/// </summary>
public class VaultShopTests
{
    private static (string, string?) Named(int id) => ($"Item {id}", null);

    private static VaultListing Limited(int id, string type, int cost, int limit, int purchased) =>
        new(id, 1000 + id, 1, type, cost, purchased, limit);

    private static VaultListing Unlimited(int id, string type, int cost) => new(id, 1000 + id, 1, type, cost);

    [Fact]
    public void A_reported_purchase_is_a_floor_and_a_zero_says_nothing()
    {
        VaultListing[] listings =
        [
            Limited(1, "Legacy", 300, 1, 1), // the /bless tome: reported, and it is all there was
            Limited(2, "Normal", 400, 3, 0), // the armor chests: bought twice, reported as none
            Limited(3, "Normal", 100, 5, 2), // reported, but perhaps not every one
            Unlimited(4, "Normal", 8)
        ];

        var rows = VaultShop.Sections(listings, Named, acclaim: 390).SelectMany(section => section).ToList();

        Assert.Equal("bought out", rows.Single(row => row.Cost == 300).LimitText);
        Assert.Equal("limit 3", rows.Single(row => row.Cost == 400).LimitText);
        Assert.Equal("at most 3 of 5 left", rows.Single(row => row.Cost == 100).LimitText);
        Assert.Equal("unlimited", rows.Single(row => row.Cost == 8).LimitText);
    }

    [Fact]
    public void A_section_says_how_many_rewards_and_adds_up_nothing()
    {
        var rewards = VaultShop.Sections([Limited(1, "Normal", 100, 5, 2), Unlimited(2, "Normal", 20)], Named, 0).Single();

        Assert.Equal("2 rewards", rewards.Summary);
    }

    [Fact]
    public void Sections_come_in_the_games_order_and_rows_cheapest_first()
    {
        VaultListing[] listings =
            [Unlimited(1, "Legacy", 1200), Unlimited(2, "Normal", 450), Unlimited(3, "Featured", 300), Unlimited(4, "Normal", 20)];

        var sections = VaultShop.Sections(listings, Named, acclaim: null);

        Assert.Equal(["Featured", "Rewards", "Legacy vault"], sections.Select(section => section.Name));
        Assert.Equal([20, 450], sections[1].Select(row => row.Cost));
    }

    [Fact]
    public void Affordable_is_measured_against_the_balance()
    {
        Assert.False(VaultShop.Sections([Unlimited(1, "Normal", 450)], Named, acclaim: 390)[0][0].Affordable);
        Assert.True(VaultShop.Sections([Unlimited(1, "Normal", 390)], Named, acclaim: 390)[0][0].Affordable);
    }
}
