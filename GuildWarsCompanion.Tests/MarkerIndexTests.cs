using GuildWarsCompanion.Gw2;
using GuildWarsCompanion.Services;

using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// Several packs on one device: what is remembered about each, and what happens where two of them
/// describe the same chest.
/// </summary>
public class MarkerIndexTests
{
    private const string Metal =
        "https://github.com/Metallis/Metal-Marker-Myriad/releases/latest/download/Metal-Marker-Myriad.taco";

    private static readonly MapInfo Queensdale = new(
        15, "Queensdale", 4, 1, 1,
        [[42624, 28032], [46208, 30464]],
        [[-43008, -27648], [43008, 30720]]);

    [Fact]
    public void A_pack_is_named_after_its_file()
    {
        Assert.Equal("Metal-Marker-Myriad", MarkerIndex.Title(Metal));
        Assert.Equal("tw_ALL_IN_ONE", MarkerIndex.Title("https://example.com/downloads/tw_ALL_IN_ONE.taco"));
    }

    /// <summary>The server's own name for the file wins over an address that ends in a query.</summary>
    [Fact]
    public void A_pack_is_named_after_the_file_the_server_names()
    {
        Assert.Equal("tw_ALL_IN_ONE", MarkerIndex.Title(
            "https://www.tekkitsworkshop.net/download?download=1:tw-all-in-one", "\"tw_ALL_IN_ONE.taco\""));
        Assert.Equal("Metal-Marker-Myriad", MarkerIndex.Title(Metal, null));
    }

    /// <summary>A URL with nothing usable on the end still has to be called something.</summary>
    [Fact]
    public void A_pack_with_no_file_name_falls_back_to_its_host()
    {
        Assert.Equal("example.com", MarkerIndex.Title("https://example.com/"));
    }

    [Fact]
    public void Remembers_the_routes_and_the_import_that_read_them()
    {
        var written = MarkerIndex.Write(
            [new MarkerPackRecord("a", Metal, "Metal", "tag", "when", 106, 17, 1334, 2)]);

        var pack = Assert.Single(MarkerIndex.Read(written));

        Assert.Equal(1334, pack.Routes);
        Assert.Equal(2, pack.Imported);
    }

    /// <summary>
    /// Both columns arrived after the file's first version. A line written before them is read as
    /// having neither rather than refused: the pack still works, and the numbers fill themselves
    /// in on its next update.
    /// </summary>
    [Fact]
    public void Reads_a_line_written_before_routes_existed()
    {
        var pack = Assert.Single(MarkerIndex.Read(
            MarkerIndex.Version + "\na\t" + Metal + "\tMetal\ttag\twhen\t106\t17\n"));

        Assert.Equal(106, pack.Count);
        Assert.Equal(0, pack.Routes);
        Assert.Equal(0, pack.Imported);
    }

    /// <summary>
    /// The same address is the same pack, every run and every device — so adding it twice replaces
    /// it rather than accumulating copies.
    /// </summary>
    [Fact]
    public void The_same_address_always_gets_the_same_file_name()
    {
        Assert.Equal(MarkerIndex.Name(Metal), MarkerIndex.Name(Metal + "  "));
        Assert.NotEqual(MarkerIndex.Name(Metal), MarkerIndex.Name(Metal + "x"));
    }

    [Fact]
    public void A_file_name_is_safe_to_write_to_disk()
    {
        var name = MarkerIndex.Name(Metal);

        Assert.Equal(16, name.Length);
        Assert.DoesNotContain(name, c => Path.GetInvalidFileNameChars().Contains(c));
    }

    [Fact]
    public void A_pack_reads_back_exactly_as_written()
    {
        MarkerPackRecord[] packs =
        [
            new(MarkerIndex.Name(Metal), Metal, "Metal-Marker-Myriad", "\"0x8DDA5E4C40085A2\"",
                "Sat, 07 Jun 2025 17:00:11 GMT", 106, 17),
            new("abc", "https://example.com/p.taco", "p", string.Empty, string.Empty, 0, 0)
        ];

        Assert.Equal(packs, MarkerIndex.Read(MarkerIndex.Write(packs)));
    }

    private const string Tekkit = "https://www.tekkitsworkshop.net/download?download=1:tw-all-in-one";

    [Fact]
    public void Remembers_the_size_the_server_announced()
    {
        var pack = Assert.Single(MarkerIndex.Read(MarkerIndex.Write(
            [new MarkerPackRecord("t", Tekkit, "tw_ALL_IN_ONE", "", "", 900, 60, 300, 5, 49378661)])));

        Assert.Equal(49378661, pack.Size);
    }

    /// <summary>A line from before the size was kept reads as "not known", which never matches,
    /// so the next update downloads once and remembers it.</summary>
    [Fact]
    public void A_line_written_before_sizes_has_none()
    {
        var pack = Assert.Single(MarkerIndex.Read(
            MarkerIndex.Version + "\nt\t" + Tekkit + "\ttw\t\t\t900\t60\t300\t5\n"));

        Assert.Equal(0, pack.Size);
        Assert.False(MarkerIndex.Unchanged(pack, 0));
    }

    /// <summary>Tekkit's case: no validators, so the size it announces is all there is to go by.</summary>
    [Fact]
    public void A_pack_without_validators_is_unchanged_at_the_same_size()
    {
        var held = new MarkerPackRecord("t", Tekkit, "tw", "", "", 900, 60, 300, 5, 49378661);

        Assert.True(MarkerIndex.Unchanged(held, 49378661));
        Assert.False(MarkerIndex.Unchanged(held, 49378662));
        Assert.False(MarkerIndex.Unchanged(held, null));
    }

    /// <summary>A server that sets a validator answered 200 despite it, so the pack changed —
    /// whatever its size says.</summary>
    [Fact]
    public void A_pack_with_validators_is_never_judged_by_size()
    {
        var tagged = new MarkerPackRecord("m", Metal, "Metal", "\"0x8DD\"", "", 106, 17, 1334, 5, 7000000);
        var dated = tagged with { Tag = "", Modified = "Sat, 07 Jun 2025 17:00:11 GMT" };

        Assert.False(MarkerIndex.Unchanged(tagged, 7000000));
        Assert.False(MarkerIndex.Unchanged(dated, 7000000));
    }

    /// <summary>An ETag is a quoted string from a stranger's server; it must not be able to break
    /// the line it is written on.</summary>
    [Fact]
    public void A_validator_cannot_break_the_index()
    {
        var read = MarkerIndex.Read(MarkerIndex.Write(
            [new MarkerPackRecord("a", "https://x/p.taco", "p", "W/\t\"tag\"", "x\ny", 1, 1)]));

        Assert.Single(read);
        Assert.Equal("W/ \"tag\"", read[0].Tag);
    }

    [Fact]
    public void A_file_from_a_format_this_does_not_know_is_refused()
    {
        Assert.Empty(MarkerIndex.Read("packs 99\na\tb\tc\td\te\t1\t1"));
        Assert.Empty(MarkerIndex.Read(string.Empty));
    }

    private static CompassTarget Chest(double x, double y) =>
        new("Chest", TargetKind.Chest, 15, "Queensdale", x, y);

    /// <summary>
    /// Two packs marking the same chest.
    ///
    /// Measured against Metal Marker Myriad and Hero's Marker Pack, which both cover Lowland
    /// Shore: where they mark the same in-game object they agree to within a unit, because both
    /// authors stood on it. Half a unit apart is the real case, not a contrived one.
    /// </summary>
    [Fact]
    public void The_same_chest_from_two_packs_is_kept_once()
    {
        var kept = MarkerLibrary.Deduplicate([Chest(44000, 29000), Chest(44000.5, 29000.2)]);

        Assert.Single(kept);
    }

    /// <summary>
    /// Two different chests in the same farming spot are two chests.
    ///
    /// The measurement said the first genuine neighbour turns up around 200 units out, so the
    /// threshold has to leave that alone: at 400 it would have merged 21 of Lowland Shore's 32
    /// markers, which is data loss wearing deduplication's clothes.
    /// </summary>
    [Fact]
    public void Two_nearby_but_distinct_chests_are_both_kept()
    {
        var kept = MarkerLibrary.Deduplicate([Chest(44000, 29000), Chest(44000, 29200)]);

        Assert.Equal(2, kept.Count);
    }

    [Fact]
    public void The_threshold_is_where_the_measurement_put_it()
    {
        Assert.Equal(25, MarkerLibrary.SameThing);

        Assert.Single(MarkerLibrary.Deduplicate([Chest(44000, 29000), Chest(44000, 29024)]));
        Assert.Equal(2, MarkerLibrary.Deduplicate([Chest(44000, 29000), Chest(44000, 29026)]).Count);
    }

    /// <summary>A chest and a jumping puzzle at one spot are two things worth saying, and a
    /// farming route often has both.</summary>
    [Fact]
    public void Different_kinds_at_the_same_place_are_both_kept()
    {
        var kept = MarkerLibrary.Deduplicate(
        [
            Chest(44000, 29000),
            new CompassTarget("Jumping puzzle", TargetKind.JumpingPuzzle, 15, "Queensdale", 44000, 29000)
        ]);

        Assert.Equal(2, kept.Count);
    }

    /// <summary>The first pack added wins, so which marker survives is at least predictable.</summary>
    [Fact]
    public void The_earlier_pack_keeps_its_marker()
    {
        var kept = MarkerLibrary.Deduplicate(
        [
            new CompassTarget("First", TargetKind.Chest, 15, "Queensdale", 44000, 29000),
            new CompassTarget("Second", TargetKind.Chest, 15, "Queensdale", 44001, 29000)
        ]);

        Assert.Equal("First", Assert.Single(kept).Name);
    }

    [Fact]
    public void Deduplicating_nothing_is_nothing()
    {
        Assert.Empty(MarkerLibrary.Deduplicate([]));
    }

    /// <summary>Markers from two packs land in the same continent space before they are compared,
    /// which is what makes comparing them meaningful at all.</summary>
    [Fact]
    public void Markers_from_two_packs_are_compared_after_conversion()
    {
        var one = MarkerLibrary.Targets([new StoredMarker(15, 10, 10, TargetKind.Chest, "A")], Queensdale);
        var two = MarkerLibrary.Targets([new StoredMarker(15, 10.1, 10.1, TargetKind.Chest, "B")], Queensdale);

        Assert.Single(MarkerLibrary.Deduplicate([.. one, .. two]));
    }
}
