using GuildWarsCompanion.Bridge.Protocol;
using GuildWarsCompanion.Services;

using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// Finding a PC when there is more than one paired.
///
/// Every test here is a case that got past the old code, and all of them share one shape: two
/// PCs paired, one of them not the one you want. That was the whole bug — the search picked a
/// single winner per step and returned nothing when it failed, so one stale entry was enough to
/// make a perfectly healthy PC unreachable. None of it was catchable before, because the paired
/// list lived behind MAUI statics and this file could not have been written.
/// </summary>
public class BridgeFinderTests
{
    private static readonly string DeskSecret = BridgeProtocol.NewPairingSecret();
    private static readonly string LapSecret = BridgeProtocol.NewPairingSecret();

    private static (BridgeFinder Finder, KnownBridges Known, MemoryBridgeStore Store) Build(
        FakeNetwork network)
    {
        var store = new MemoryBridgeStore();
        var known = new KnownBridges(store);
        var journal = new Journal(Path.Combine(Path.GetTempPath(), "gw2-bridge-tests.log"));

        return (new BridgeFinder(network, network, known, journal), known, store);
    }

    private static FakePc Desk(string address = "192.168.1.20:47623") =>
        new("DESKID", "Desktop", DeskSecret, address);

    private static FakePc Lap(string address = "192.168.1.30:47623") =>
        new("LAPID", "Laptop", LapSecret, address);

    /// <summary>
    /// Files a paired PC as having last been seen this long ago, and optionally there.
    ///
    /// Explicit rather than left to whatever two <c>PairAsync</c> calls in a row produce:
    /// <c>DateTimeOffset.UtcNow</c> moves in 15ms steps on Windows, so pairing order is not a
    /// reliable way to say which entry is more recent — and the whole point of these tests is
    /// that the PC ranked first is the one that fails.
    /// </summary>
    private static void Seen(
        KnownBridges known, FakePc pc, int minutesAgo, string? address = null) =>
        known.Save(known.Find(pc.Id)! with
        {
            LastSeen = DateTimeOffset.UtcNow.AddMinutes(-minutesAgo),
            Address = address,
            Network = BridgePlan.NetworkOf(address)
        });

    // --- the reported failure ----------------------------------------------

    [Fact]
    public async Task The_second_pc_is_used_when_the_first_one_is_switched_off()
    {
        // Two PCs scanned. The one paired first is off and has a cached address, so it is the
        // first thing tried and the first thing to fail. The other one has no address at all —
        // a freshly scanned card carries none on purpose — and is only findable by broadcast.
        var desk = Desk();
        var lap = Lap();

        desk.Running = false;

        var network = new FakeNetwork().With(desk).With(lap);
        var (finder, known, _) = Build(network);

        await known.PairAsync(desk.Card);
        await known.PairAsync(lap.Card);

        // The dead PC is the one with an address on file and the one used most recently, so it
        // is the first thing tried at every step. The other one has to be reached anyway.
        Seen(known, desk, minutesAgo: 1, address: desk.Address);
        Seen(known, lap, minutesAgo: 90);

        var found = await finder.ResolveAsync();

        Assert.Equal("Laptop", found?.Bridge.Label);
        Assert.Equal(lap.Address, found?.Address);
    }

    [Fact]
    public async Task A_pc_whose_secret_no_longer_matches_does_not_take_the_other_one_down_with_it()
    {
        // The case that reads as "no PC answered" and is nothing of the kind: the bridge on the
        // desktop was reinstalled, so it kept its id and grew a new secret. It answers, it
        // fails the challenge, and it used to be the only candidate the step ever tried.
        var desk = Desk();
        var lap = Lap();

        desk.ProvesWith = BridgeProtocol.NewPairingSecret();

        var network = new FakeNetwork().With(desk).With(lap);
        var (finder, known, _) = Build(network);

        await known.PairAsync(desk.Card);
        await known.PairAsync(lap.Card);

        // Ranked first, and it is the one that cannot prove itself. Taking the top candidate
        // and stopping there is exactly what left the laptop unreachable.
        Seen(known, desk, minutesAgo: 1);
        Seen(known, lap, minutesAgo: 90);

        var found = await finder.ResolveAsync();

        Assert.Equal("Laptop", found?.Bridge.Label);
    }

    [Fact]
    public async Task A_pc_that_answers_and_fails_its_challenge_says_so_rather_than_going_quiet()
    {
        // The diagnosis is the point. "Is the bridge running?" is the wrong question to be
        // asked about a bridge that is running and answering; the fix is to scan it again, and
        // nothing in the app used to say that anywhere the user could see it.
        var desk = Desk();

        desk.ProvesWith = BridgeProtocol.NewPairingSecret();

        var network = new FakeNetwork().With(desk);
        var (finder, known, _) = Build(network);

        await known.PairAsync(desk.Card);

        Assert.Null(await finder.ResolveAsync());

        var problem = finder.Problem;

        Assert.Contains("Desktop", problem);
        Assert.Contains("could not prove", problem);
        Assert.Contains("scan its QR code again", problem);
    }

    [Fact]
    public async Task A_pairing_with_no_secret_on_this_phone_is_named_and_stepped_over()
    {
        // The keystore and the list can come apart — a restore, a cleared keystore — and the
        // symptom is a PC in the list that can never be used. Say which one and why.
        var desk = Desk();
        var lap = Lap();

        var network = new FakeNetwork().With(desk).With(lap);
        var (finder, known, store) = Build(network);

        await known.PairAsync(desk.Card);
        await known.PairAsync(lap.Card);

        store.RemoveSecret(desk.Id);

        Seen(known, desk, minutesAgo: 1);
        Seen(known, lap, minutesAgo: 90);

        var found = await finder.ResolveAsync();

        Assert.Equal("Laptop", found?.Bridge.Label);
    }

    // --- the promise the settings page makes ------------------------------

    [Fact]
    public async Task The_pc_running_the_game_gets_the_paste()
    {
        var desk = Desk();
        var lap = Lap();

        lap.Playing = true;

        var network = new FakeNetwork().With(desk).With(lap);
        var (finder, known, _) = Build(network);

        await known.PairAsync(desk.Card);
        await known.PairAsync(lap.Card);

        // The desktop is the more recently used of the two, so only the game can decide this.
        Seen(known, desk, minutesAgo: 1);
        Seen(known, lap, minutesAgo: 90);

        Assert.Equal("Laptop", (await finder.ResolveAsync())?.Bridge.Label);
    }

    [Fact]
    public async Task One_paired_pc_is_never_asked_what_the_game_is_doing()
    {
        // There is nothing to choose between, so the round trip could only confirm the only
        // option there is. It runs behind a tap.
        var desk = Desk();
        var network = new FakeNetwork().With(desk);
        var (finder, known, _) = Build(network);

        await known.PairAsync(desk.Card);

        await finder.ResolveAsync();

        Assert.DoesNotContain(network.Asked, url => url.Contains(BridgeProtocol.GamePath));
    }

    // --- where it looks, and how hard ------------------------------------

    [Fact]
    public async Task A_pc_off_the_default_port_is_still_swept_for()
    {
        // The card carries a port and the pairing used to drop it, which left a bridge moved
        // off the default findable by broadcast and by nothing else.
        var desk = Desk("192.168.1.20:50000");

        desk.Port = 50000;
        desk.Discoverable = false;

        var network = new FakeNetwork { BroadcastWorks = false }.With(desk);
        var (finder, known, _) = Build(network);

        await known.PairAsync(desk.Card);

        var found = await finder.ResolveAsync();

        Assert.Equal("192.168.1.20:50000", found?.Address);
    }

    [Fact]
    public async Task The_pc_sharing_the_connection_is_found_at_the_gateway()
    {
        // What a PC hotspot is: the machine sharing the connection is the gateway, so on the
        // setup where the address churns most the bridge sits somewhere the phone can read off
        // its own link properties. Broadcast is off here, which is the case this rescues.
        var desk = Desk("192.168.137.1:47623");

        desk.Discoverable = false;

        var network = new FakeNetwork("192.168.137", gateway: "192.168.137.1")
        {
            BroadcastWorks = false
        }.With(desk);

        var (finder, known, _) = Build(network);

        await known.PairAsync(desk.Card);

        Assert.Equal("192.168.137.1:47623", (await finder.ResolveAsync())?.Address);
    }

    [Fact]
    public async Task An_unusable_pc_low_in_the_subnet_does_not_hide_the_one_above_it()
    {
        // The sweep goes out in chunks of 32. A PC at .5 that answers and cannot prove itself
        // is in the first chunk; the one that works is in the seventh. Returning as soon as a
        // chunk answered would stop at the broken one and report nothing found.
        var desk = Desk("192.168.1.5:47623");
        var lap = Lap("192.168.1.200:47623");

        desk.ProvesWith = BridgeProtocol.NewPairingSecret();
        desk.Discoverable = false;
        lap.Discoverable = false;

        var network = new FakeNetwork { BroadcastWorks = false }.With(desk).With(lap);
        var (finder, known, _) = Build(network);

        await known.PairAsync(desk.Card);
        await known.PairAsync(lap.Card);

        Assert.Equal("192.168.1.200:47623", (await finder.ResolveAsync())?.Address);
    }

    [Fact]
    public async Task A_switched_off_pc_in_the_list_does_not_slow_down_the_one_that_answers()
    {
        // Found on the phone: a live PC answered /hello in 4ms and the resolve still took
        // 2.2 seconds, because every cached probe was awaited and one paired PC was switched
        // off. The dead entry is first in the cache order here, which is the bad case.
        var desk = Desk();
        var lap = Lap();

        desk.Running = false;

        var network = new FakeNetwork().With(desk).With(lap);
        var (finder, known, _) = Build(network);

        await known.PairAsync(desk.Card);
        await known.PairAsync(lap.Card);

        Seen(known, desk, minutesAgo: 1, address: desk.Address);
        Seen(known, lap, minutesAgo: 2, address: lap.Address);

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var found = await finder.ResolveAsync();

        clock.Stop();

        Assert.Equal("Laptop", found?.Bridge.Label);

        // The fake answers instantly and never times out, so this only pins that nothing waits
        // on the dead probe. Against a real socket the difference was 2.2s versus 250ms.
        Assert.True(
            clock.ElapsedMilliseconds < 1500,
            $"waited {clock.ElapsedMilliseconds}ms on a PC that was never going to answer");
    }

    [Fact]
    public async Task The_same_failed_challenge_is_not_asked_for_twice()
    {
        // Four steps can each turn up the same PC at the same address. On the phone that was
        // three identical challenge failures in one search: wasted round trips, and a log that
        // read like three problems.
        var desk = Desk("192.168.1.1:47623");

        desk.ProvesWith = BridgeProtocol.NewPairingSecret();
        desk.Discoverable = false;

        var network = new FakeNetwork("192.168.1", gateway: "192.168.1.1")
        {
            BroadcastWorks = false
        }.With(desk);

        var (finder, known, _) = Build(network);

        await known.PairAsync(desk.Card);
        Seen(known, desk, minutesAgo: 1, address: desk.Address);

        Assert.Null(await finder.ResolveAsync());

        // One nonce challenge for that address, however many steps arrive at it.
        var challenges = network.Asked.Count(url => url.Contains("nonce="));

        Assert.Equal(1, challenges);
    }

    [Fact]
    public async Task A_pc_that_answered_and_was_refused_is_not_also_called_absent()
    {
        // The composed message contradicted itself on the phone: "GKS-0432 answered at … but
        // could not prove it holds the same pairing secret. Nothing answered on 192.168.137.
        // Is the bridge running?" Both sentences, about one PC, in one label.
        var desk = Desk();

        desk.ProvesWith = BridgeProtocol.NewPairingSecret();
        desk.Discoverable = false;

        var network = new FakeNetwork { BroadcastWorks = false }.With(desk);
        var (finder, known, _) = Build(network);

        await known.PairAsync(desk.Card);
        Seen(known, desk, minutesAgo: 1, address: desk.Address);

        Assert.Null(await finder.ResolveAsync());

        var problem = finder.Problem;

        Assert.Contains("could not prove", problem);
        Assert.DoesNotContain("Nothing answered", problem);
        Assert.DoesNotContain("Is the bridge running", problem);
    }

    [Fact]
    public async Task A_second_resolve_inside_the_window_asks_the_network_nothing()
    {
        var desk = Desk();
        var network = new FakeNetwork().With(desk);
        var (finder, known, _) = Build(network);

        await known.PairAsync(desk.Card);

        await finder.ResolveAsync();

        var asked = network.Asked.Count;

        await finder.ResolveAsync();

        Assert.Equal(asked, network.Asked.Count);
    }

    [Fact]
    public async Task Two_resolves_at_once_search_once()
    {
        // A roster refresh and an emote tap is the ordinary case. Two searches at once meant
        // two subnet sweeps and two racing writes over the paired list.
        var desk = Desk();
        var network = new FakeNetwork().With(desk);
        var (finder, known, _) = Build(network);

        await known.PairAsync(desk.Card);

        var both = await Task.WhenAll(finder.ResolveAsync(), finder.ResolveAsync());

        Assert.All(both, found => Assert.Equal("Desktop", found?.Bridge.Label));

        // One /hello for the cache miss, one for the challenge. A second full search would
        // double it, and a second sweep would add hundreds.
        Assert.True(
            network.Asked.Count <= 4,
            $"expected one search, saw {network.Asked.Count} requests");
    }

    [Fact]
    public async Task Where_a_pc_was_found_is_remembered_for_next_time()
    {
        var desk = Desk();

        desk.Discoverable = true;

        var network = new FakeNetwork().With(desk);
        var (finder, known, _) = Build(network);

        await known.PairAsync(desk.Card);

        await finder.ResolveAsync();

        var stored = known.Find(desk.Id);

        Assert.Equal(desk.Address, stored?.Address);
        Assert.Equal("192.168.1", stored?.Network);
    }

    // --- the typed pairing, which cannot prove anything -------------------

    [Fact]
    public async Task A_typed_pairing_that_moved_is_not_asked_about_while_another_pc_answers()
    {
        // A confirmation is a question put to somebody who was trying to send an emote. Asking
        // it when there is a scanned PC sitting right there answering is asking for nothing.
        var lap = Lap();
        var network = new FakeNetwork().With(lap);
        var (finder, known, _) = Build(network);

        // Typed against an address it is no longer at, and its id is adopted from /hello.
        await known.PairAsync("192.168.1.99:47623", "ABCD-2345");
        await known.PairAsync(lap.Card);

        var found = await finder.ResolveAsync();

        Assert.Equal("Laptop", found?.Bridge.Label);
        Assert.Null(finder.Move);
    }

    [Fact]
    public async Task A_typed_pairing_is_adopted_once_the_bridge_says_who_it_is()
    {
        var desk = Desk();
        var network = new FakeNetwork().With(desk);
        var (finder, known, store) = Build(network);

        await known.PairAsync(desk.Address, "ABCD-2345");

        var found = await finder.ResolveAsync();

        Assert.Equal("Desktop", found?.Bridge.Label);
        Assert.Equal(desk.Id, found?.Bridge.Id);
        Assert.True(found?.Bridge.IsIdentified);

        // The secret moved with the id, and the placeholder slot was cleaned up rather than
        // left behind holding a live credential.
        Assert.Contains(desk.Id, store.SecretIds);
        Assert.DoesNotContain(KnownBridge.PendingId(desk.Address), store.SecretIds);
    }
}
