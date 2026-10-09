using GuildWarsCompanion.Bridge.Protocol;
using GuildWarsCompanion.Services;

using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// Keeping the list of paired PCs, and not losing any of them.
///
/// Every pairing this app has ever supported has to survive being joined by another one, and
/// that is harder than it sounds: two of the bugs pinned here deleted somebody's PC outright
/// and told them nothing.
/// </summary>
public class KnownBridgesTests
{
    private static BridgeCard Card(string id, string name) =>
        new(id, name, BridgeProtocol.NewPairingSecret(), BridgeProtocol.DefaultPort);

    [Fact]
    public async Task Pairing_a_second_pc_by_hand_does_not_delete_the_first()
    {
        // The one that bit. Every typed pairing was filed under the same "?" placeholder, and
        // Save matches on the id — so the second one replaced the first, in the list and in the
        // keystore, and the PC that had been working for weeks simply stopped existing.
        var known = new KnownBridges(new MemoryBridgeStore());

        await known.PairAsync("192.168.1.20:47623", "ABCD-2345");
        await known.PairAsync("192.168.1.30:47623", "WXYZ-9876");

        var all = known.All();

        Assert.Equal(2, all.Count);
        Assert.Contains(all, b => b.Address == "192.168.1.20:47623");
        Assert.Contains(all, b => b.Address == "192.168.1.30:47623");
    }

    [Fact]
    public async Task Each_typed_pairing_keeps_its_own_code()
    {
        var known = new KnownBridges(new MemoryBridgeStore());

        var first = await known.PairAsync("192.168.1.20:47623", "ABCD-2345");
        var second = await known.PairAsync("192.168.1.30:47623", "WXYZ-9876");

        Assert.Equal("ABCD-2345", await known.SecretAsync(first.Id));
        Assert.Equal("WXYZ-9876", await known.SecretAsync(second.Id));
    }

    [Fact]
    public async Task Pairing_the_same_address_twice_replaces_it_rather_than_stacking_up()
    {
        // Correcting a typo is the normal reason to pair the same PC twice.
        var known = new KnownBridges(new MemoryBridgeStore());

        await known.PairAsync("192.168.1.20:47623", "ABCD-2345");
        await known.PairAsync("192.168.1.20:47623", "WXYZ-9876");

        Assert.Single(known.All());
        Assert.Equal("WXYZ-9876", await known.SecretAsync(known.All()[0].Id));
    }

    [Fact]
    public async Task A_scanned_card_keeps_the_port_it_named()
    {
        var known = new KnownBridges(new MemoryBridgeStore());

        var paired = await known.PairAsync(
            new BridgeCard("DESKID", "Desktop", BridgeProtocol.NewPairingSecret(), 50000));

        Assert.Equal(50000, paired.Port);
        Assert.Equal(50000, paired.Listens);
    }

    [Fact]
    public void A_pairing_that_named_no_port_listens_on_the_default_one()
    {
        Assert.Equal(BridgeProtocol.DefaultPort, new KnownBridge("DESKID", "Desktop").Listens);
    }

    [Fact]
    public async Task Two_pcs_scanned_are_two_pcs()
    {
        var known = new KnownBridges(new MemoryBridgeStore());

        await known.PairAsync(Card("DESKID", "Desktop"));
        await known.PairAsync(Card("LAPID", "Laptop"));

        Assert.Equal(2, known.All().Count);
    }

    [Fact]
    public async Task Scanning_a_pc_already_paired_by_hand_wins_over_the_typed_one()
    {
        // A scanned pairing can follow its PC to a new address and a typed one cannot, so the
        // strong one has to survive the meeting. The placeholder goes, secret and all.
        var store = new MemoryBridgeStore();
        var known = new KnownBridges(store);

        var typed = await known.PairAsync("192.168.1.20:47623", "ABCD-2345");
        var card = Card("DESKID", "Desktop");

        await known.PairAsync(card);

        var settled = await known.AdoptAsync(typed, new BridgeIdentity(card.Id, card.Name, card.Port));

        Assert.True(settled.Strong);
        Assert.Single(known.All());
        Assert.DoesNotContain(typed.Id, store.SecretIds);
    }

    [Fact]
    public async Task A_carried_over_pairing_is_migrated_without_disturbing_a_scanned_one()
    {
        // The migration deleted what it was migrating once already. Anyone who scanned a PC
        // before this first ran lost the pairing that predated it.
        var store = new MemoryBridgeStore { LegacyHost = "192.168.1.20:47623", LegacyCode = "ABCD-2345" };
        var known = new KnownBridges(store);

        await known.PairAsync(Card("LAPID", "Laptop"));
        await known.MigrateAsync();

        Assert.Equal(2, known.All().Count);
        Assert.Null(store.LegacyPairing().Host);
    }

    [Fact]
    public async Task Migrating_twice_adds_one_pc()
    {
        var store = new MemoryBridgeStore { LegacyHost = "192.168.1.20:47623", LegacyCode = "ABCD-2345" };
        var known = new KnownBridges(store);

        await known.MigrateAsync();
        await known.MigrateAsync();

        Assert.Single(known.All());
    }

    [Fact]
    public async Task Saving_from_several_places_at_once_loses_nobody()
    {
        // Two resolves recording where two PCs were found is the ordinary case, and a
        // read-modify-write without a lock drops whichever entry the other one had just added.
        var known = new KnownBridges(new MemoryBridgeStore());

        await Task.WhenAll(Enumerable.Range(0, 24).Select(i => Task.Run(() =>
            known.Save(new KnownBridge($"PC{i}", $"PC {i}", $"192.168.1.{i + 10}:47623")))));

        Assert.Equal(24, known.All().Count);
    }

    [Fact]
    public void Forgetting_one_pc_keeps_the_other()
    {
        var known = new KnownBridges(new MemoryBridgeStore());

        known.Save(new KnownBridge("DESKID", "Desktop", "192.168.1.20:47623"));
        known.Save(new KnownBridge("LAPID", "Laptop", "192.168.1.30:47623"));

        known.Forget("DESKID");

        Assert.Single(known.All());
        Assert.Equal("LAPID", known.All()[0].Id);
    }
}
