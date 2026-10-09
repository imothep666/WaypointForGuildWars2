using GuildWarsCompanion.Bridge.Protocol;
using GuildWarsCompanion.Services;
using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// Pairing with several PCs, and finding them again after their addresses change.
///
/// The thing under test is a claim about trust as much as about lookup: a typed code is too
/// short to prove which machine answered, a scanned secret is not, and the two therefore earn
/// different amounts of freedom. Most of what follows pins that difference, because it is the
/// part that would be tempting to simplify away.
/// </summary>
public class BridgePairingTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    private static KnownBridge Pc(
        string id, string? address = null, string? network = null, int minutesAgo = 0) =>
        new(id, id, address, network, Noon.AddMinutes(-minutesAgo));

    // --- what a short code cannot do --------------------------------------

    [Fact]
    public void A_typed_code_is_never_strong_enough_to_prove_anything()
    {
        // Eight Crockford symbols is 40 bits. A single (nonce, proof) pair off the network is an
        // offline puzzle of that size, which is hours — so this must not be provable at all
        // rather than provable badly.
        Assert.False(BridgeProtocol.IsStrong(BridgeProtocol.NewPairingCode()));
        Assert.Null(BridgeProtocol.Proof(BridgeProtocol.NewPairingCode(), BridgeProtocol.NewNonce()));
    }

    [Fact]
    public void A_scanned_secret_is()
    {
        var secret = BridgeProtocol.NewPairingSecret();

        Assert.True(BridgeProtocol.IsStrong(secret));
        Assert.NotNull(BridgeProtocol.Proof(secret, BridgeProtocol.NewNonce()));
    }

    [Fact]
    public void A_proof_is_over_this_nonce_and_this_secret_and_no_other()
    {
        var secret = BridgeProtocol.NewPairingSecret();
        var nonce = BridgeProtocol.NewNonce();

        Assert.True(BridgeProtocol.ProofsMatch(
            BridgeProtocol.Proof(secret, nonce), BridgeProtocol.Proof(secret, nonce)));

        // A fresh nonce every time is what stops one overheard exchange being replayed for ever.
        Assert.False(BridgeProtocol.ProofsMatch(
            BridgeProtocol.Proof(secret, nonce), BridgeProtocol.Proof(secret, BridgeProtocol.NewNonce())));

        Assert.False(BridgeProtocol.ProofsMatch(
            BridgeProtocol.Proof(secret, nonce),
            BridgeProtocol.Proof(BridgeProtocol.NewPairingSecret(), nonce)));
    }

    [Fact]
    public void A_missing_proof_is_a_no_and_not_a_pass()
    {
        // The dangerous shape: an unproved bridge answering null and a phone reading that as
        // "nothing to check, carry on".
        Assert.False(BridgeProtocol.ProofsMatch(null, null));
        Assert.False(BridgeProtocol.ProofsMatch(null, "ABC"));
        Assert.False(BridgeProtocol.ProofsMatch("ABC", null));
    }

    // --- the card -----------------------------------------------------------

    [Fact]
    public void A_card_survives_the_round_trip_through_a_qr_code()
    {
        var card = new BridgeCard("797G061ZRJCJ", "Desktop", BridgeProtocol.NewPairingSecret(), 47623);

        var read = BridgeCard.TryParse(card.Encode());

        Assert.Equal(card, read);
    }

    [Fact]
    public void A_card_carries_no_address()
    {
        // The whole point. An address here would be true when the code was drawn and false after
        // the next lease, which is the bug this mechanism exists to remove.
        var encoded = new BridgeCard("ID", "Desktop", BridgeProtocol.NewPairingSecret(), 47623).Encode();

        Assert.DoesNotContain("192.168", encoded);
        Assert.DoesNotContain("addr", encoded);
    }

    [Fact]
    public void A_name_with_spaces_or_symbols_still_reads_back()
    {
        var card = new BridgeCard("ID", "Martin's PC & laptop", BridgeProtocol.NewPairingSecret(), 47623);

        Assert.Equal(card.Name, BridgeCard.TryParse(card.Encode())?.Name);
    }

    [Fact]
    public void Anything_that_is_not_a_card_is_declined_rather_than_thrown_at()
    {
        // The scanner points at whatever the user framed, so a wrong QR is the normal case.
        Assert.Null(BridgeCard.TryParse(null));
        Assert.Null(BridgeCard.TryParse(""));
        Assert.Null(BridgeCard.TryParse("https://example.com"));
        Assert.Null(BridgeCard.TryParse("gw2bridge:v1?name=Desktop&port=47623"));

        // A card offering a weak secret is refused too: accepting it would create a pairing that
        // looks scanned, and therefore trusted to move, while proving nothing.
        Assert.Null(BridgeCard.TryParse("gw2bridge:v1?id=ID&s=ABCD-EFGH&port=47623"));
    }

    // --- two credentials, one bridge ---------------------------------------

    [Fact]
    public void A_bridge_answers_to_either_credential()
    {
        // A phone that typed the code and one that scanned the QR are both legitimately paired,
        // and neither should be broken by the other existing.
        var code = BridgeProtocol.NewPairingCode();
        var secret = BridgeProtocol.NewPairingSecret();

        Assert.True(BridgeProtocol.Accepts(code, code, secret));
        Assert.True(BridgeProtocol.Accepts(secret, code, secret));
        Assert.False(BridgeProtocol.Accepts("WRONG-ONE", code, secret));
        Assert.False(BridgeProtocol.Accepts(null, code, secret));
    }

    [Fact]
    public void A_nonce_is_read_off_the_query_string()
    {
        var head = BridgeProtocol.ParseHead("GET /hello?nonce=DEADBEEF HTTP/1.1\r\nHost: x\r\n");

        Assert.Equal("DEADBEEF", head?.Query("nonce"));
        Assert.Null(head?.Query("missing"));

        // The query must not stop the path matching, or /hello would answer nothing the moment
        // it was asked to prove itself.
        Assert.True(BridgeProtocol.PathIs(head?.Path, BridgeProtocol.HelloPath));
    }

    // --- which address to try, and which answer to believe -----------------

    [Fact]
    public void Addresses_on_this_network_are_tried_before_any_other()
    {
        // The same desktop has one address at home and another on a hotspot. Trying the wrong
        // one first is the cache missing every time it is most needed.
        var known = new[]
        {
            Pc("desk", "10.1.17.100:47623", "10.1.17", minutesAgo: 1),
            Pc("lap", "192.168.137.5:47623", "192.168.137", minutesAgo: 90)
        };

        var order = BridgePlan.Addresses(known, "192.168.137");

        Assert.Equal("192.168.137.5:47623", order[0]);
        Assert.Equal("10.1.17.100:47623", order[1]);
    }

    [Fact]
    public void An_address_from_another_network_is_kept_as_a_long_shot()
    {
        // One parallel probe each, and it rescues the case where the subnet could not be read.
        var known = new[] { Pc("desk", "10.1.17.100:47623", "10.1.17") };

        Assert.Single(BridgePlan.Addresses(known, null));
        Assert.Empty(BridgePlan.Addresses([Pc("desk")], "10.1.17"));
    }

    [Fact]
    public void The_pc_running_the_game_wins_over_the_one_used_most_recently()
    {
        // With a desktop and a laptop both awake, the one rendering a character is where the
        // person is sitting — a better answer than anything the phone could ask them for.
        var recent = Answer(Pc("laptop", minutesAgo: 0));
        var playing = Answer(Pc("desktop", minutesAgo: 300), playing: true);

        Assert.Equal("desktop", BridgePlan.Rank([recent, playing])[0].Bridge.Id);
    }

    [Fact]
    public void Recency_settles_it_when_nothing_is_being_played()
    {
        var ranked = BridgePlan.Rank(
            [Answer(Pc("old", minutesAgo: 300)), Answer(Pc("new", minutesAgo: 2))]);

        Assert.Equal("new", ranked[0].Bridge.Id);
        Assert.Empty(BridgePlan.Rank([]));
    }

    [Fact]
    public void Everything_that_answered_is_kept_and_not_just_the_winner()
    {
        // The reason this returns a list. Being ranked first is not the same as being usable —
        // a PC can answer and then fail its challenge — and taking one winner meant the other
        // PC, answering perfectly, was never tried at all.
        var ranked = BridgePlan.Rank(
            [Answer(Pc("desktop", minutesAgo: 1)), Answer(Pc("laptop", minutesAgo: 90))]);

        Assert.Equal(["desktop", "laptop"], ranked.Select(a => a.Bridge.Id));
    }

    [Fact]
    public void One_pc_answering_twice_is_one_candidate_at_the_address_it_was_known_at()
    {
        // A PC on two interfaces answers twice. Preferring the address already on file is what
        // keeps a pointless "it has moved, is that all right?" out of the way.
        var desk = Pc("desktop", "192.168.1.20:47623", "192.168.1");

        var ranked = BridgePlan.Rank([
            new BridgeAnswer(desk, "10.8.0.6:47623"),
            new BridgeAnswer(desk, "192.168.1.20:47623")
        ]);

        Assert.Single(ranked);
        Assert.Equal("192.168.1.20:47623", ranked[0].Address);
    }

    [Fact]
    public void A_pc_never_found_anywhere_has_not_moved()
    {
        // A freshly scanned card carries no address on purpose. Counting that as a move made
        // the PC the user had just paired the last one tried, and asked permission to use it.
        Assert.False(new BridgeAnswer(Pc("desktop"), "192.168.1.20:47623").Moved);
        Assert.True(
            new BridgeAnswer(Pc("desktop", "192.168.1.20:47623", "192.168.1"), "192.168.1.99:47623")
                .Moved);
    }

    private static BridgeAnswer Answer(KnownBridge bridge, bool playing = false) =>
        new(bridge, bridge.Address ?? "192.168.1.20:47623", null, playing);

    [Fact]
    public void The_network_of_an_address_is_its_first_three_octets()
    {
        Assert.Equal("192.168.137", BridgePlan.NetworkOf("192.168.137.191"));
        Assert.Equal("192.168.137", BridgePlan.NetworkOf("192.168.137.1:47623"));

        // Not a v4 address, so no claim about which network it is. Null has to mean "do not
        // pretend to know" rather than any particular network.
        Assert.Null(BridgePlan.NetworkOf("fe80::288c:adff:fe25:9eff"));
        Assert.Null(BridgePlan.NetworkOf("desktop.local"));
        Assert.Null(BridgePlan.NetworkOf(null));
    }

    [Fact]
    public void A_carried_over_pairing_is_unidentified_until_the_bridge_says_who_it_is()
    {
        // Every pairing that exists today is address-and-code shaped. It has to keep working,
        // and it has to be able to grow an identity later without being re-paired.
        var legacy = new KnownBridge(KnownBridge.Pending, "PC", "10.1.17.100:47623");

        Assert.False(legacy.IsIdentified);
        Assert.True(legacy with { Id = "797G061ZRJCJ" } is { IsIdentified: true });
    }
}
