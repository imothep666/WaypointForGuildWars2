using System.IO.Compression;
using System.Text;

using GuildWarsCompanion.Services;

using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// Reading a TacO marker pack, and deciding what its categories mean.
///
/// The packs are written against a format with no schema and no registry, by a dozen tools and a
/// good many people by hand, so what is pinned here is the leniency: mixed attribute casing, a
/// missing coordinate, a document that does not parse at all, and numbers that a German phone
/// would otherwise read with the decimal point in the wrong place.
///
/// The fixtures are written here rather than taken from a real pack. That is not a shortcut: this
/// machine has no pack, and a test that carried somebody else's file would be redistributing it.
/// </summary>
public class MarkerPackTests
{
    private const string Document = """
        <?xml version="1.0" encoding="utf-8"?>
        <OverlayData>
          <MarkerCategory name="mypack" DisplayName="My Pack">
            <MarkerCategory name="chests" DisplayName="Chests" />
            <MarkerCategory name="jp" DisplayName="Jumping Puzzles" />
          </MarkerCategory>
          <POIs>
            <POI MapID="15" xpos="-12.5" ypos="3.25" zpos="40.75" type="mypack.chests" name="Hidden chest" />
            <POI mapid="15" XPos="8" YPos="0" ZPos="-16" type="mypack.jp.urmaug" />
            <POI MapID="15" xpos="1" zpos="1" type="mypack.trails.chests" />
            <POI MapID="15" type="mypack.chests.nocoords" />
            <POI xpos="1" zpos="1" type="mypack.chests.nomap" />
          </POIs>
        </OverlayData>
        """;

    private static Stream Xml(string text) => new MemoryStream(Encoding.UTF8.GetBytes(text));

    private static Stream Pack(params (string Name, string Text)[] entries)
    {
        var buffer = new MemoryStream();

        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, text) in entries)
            {
                var entry = archive.CreateEntry(name);

                using var stream = entry.Open();
                using var writer = new StreamWriter(stream, Encoding.UTF8);

                writer.Write(text);
            }

        buffer.Position = 0;

        return buffer;
    }

    [Fact]
    public void Every_readable_marker_is_read()
    {
        var markers = MarkerPack.ReadDocument(Xml(Document));

        // Three of the five: one has no coordinates and one has no map.
        Assert.Equal(3, markers.Count);
    }

    [Fact]
    public void Coordinates_are_read_as_written()
    {
        var marker = MarkerPack.ReadDocument(Xml(Document))[0];

        Assert.Equal(15, marker.MapId);
        Assert.Equal(-12.5, marker.MetresX, 6);
        Assert.Equal(3.25, marker.MetresY!.Value, 6);
        Assert.Equal(40.75, marker.MetresZ, 6);
        Assert.Equal("Hidden chest", marker.Name);
    }

    /// <summary>The same tool writes MapID in one file and mapid in another.</summary>
    [Fact]
    public void Attribute_casing_does_not_matter()
    {
        var marker = MarkerPack.ReadDocument(Xml(Document))[1];

        Assert.Equal(15, marker.MapId);
        Assert.Equal(8, marker.MetresX, 6);
        Assert.Equal(-16, marker.MetresZ, 6);
    }

    /// <summary>A marker with nothing to place it is dropped rather than placed at the origin,
    /// which is the far corner of the continent and a needle pointing confidently at nothing.</summary>
    [Fact]
    public void A_marker_without_a_position_or_a_map_is_dropped()
    {
        var markers = MarkerPack.ReadDocument(Xml(Document));

        Assert.DoesNotContain(markers, m => m.Type.Contains("nocoords"));
        Assert.DoesNotContain(markers, m => m.Type.Contains("nomap"));
    }

    /// <summary>
    /// The decimal point is a machine convention, not a local one.
    ///
    /// On a phone set to German, a culture-sensitive parse reads "-12.5" as minus one hundred and
    /// twenty-five — a marker a hundred maps away, from a file that was perfectly correct.
    /// </summary>
    [Fact]
    public void Numbers_are_read_in_the_invariant_culture()
    {
        var was = Thread.CurrentThread.CurrentCulture;

        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");

            Assert.Equal(-12.5, MarkerPack.ReadDocument(Xml(Document))[0].MetresX, 6);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = was;
        }
    }

    [Fact]
    public void A_pack_reads_every_xml_it_holds()
    {
        var markers = MarkerPack.Read(Pack(("one.xml", Document), ("two/three.xml", Document)));

        Assert.Equal(6, markers.Count);
    }

    /// <summary>One unreadable document must not cost the rest of the pack.</summary>
    [Fact]
    public void A_broken_document_does_not_cost_the_others()
    {
        var markers = MarkerPack.Read(Pack(
            ("broken.xml", "<OverlayData><POIs><POI MapID=\"1\""),
            ("good.xml", Document)));

        Assert.Equal(3, markers.Count);
    }

    [Fact]
    public void Anything_that_is_not_xml_is_skipped()
    {
        var markers = MarkerPack.Read(Pack(
            ("art/chest.png", "not xml at all"), ("good.xml", Document)));

        Assert.Equal(3, markers.Count);
    }

    [Fact]
    public void The_limit_is_obeyed()
    {
        Assert.Single(MarkerPack.ReadDocument(Xml(Document), limit: 1));
    }

    /// <summary>
    /// The limit counts what is kept, not what is read. Tekkit's All-In-One has 56,368 POIs and
    /// the first 20,000 of them held 314 of its 1,900 chests and puzzles.
    /// </summary>
    [Fact]
    public void The_limit_is_spent_only_on_what_is_kept()
    {
        var pois = string.Concat(Enumerable.Range(0, 50).Select(_ =>
            """<POI MapID="15" xpos="1" ypos="2" zpos="3" type="tw_guides.tw_fishing.hole"/>"""));
        var chest = """<POI MapID="15" xpos="4" ypos="5" zpos="6" type="tw_guides.tw_core.tw_core_sunkenchests.tw_core_sunkenchests_sunkenchest"/>""";

        var kept = MarkerPack.Read(
            Pack(("all.xml", $"<OverlayData><POIs>{pois}{chest}</POIs></OverlayData>")),
            limit: 10, keep: type => MarkerKinds.Of(type) is not null);

        Assert.Equal(TargetKind.Chest, MarkerKinds.Of(Assert.Single(kept).Type));
    }

    [Theory]
    [InlineData("mypack.chests", TargetKind.Chest)]
    [InlineData("TW.Chests.HoT.ChestOne", TargetKind.Chest)]
    [InlineData("mypack.jumpingpuzzle.urmaug", TargetKind.JumpingPuzzle)]
    [InlineData("mypack.jp.urmaug", TargetKind.JumpingPuzzle)]
    [InlineData("reactif.jps.drytop", TargetKind.JumpingPuzzle)]
    public void A_category_is_read_for_what_it_says_it_is(string type, TargetKind expected)
    {
        Assert.Equal(expected, MarkerKinds.Of(type));
    }

    /// <summary>
    /// The trail to a chest is not a chest, and a "chest opener" is a wall.
    ///
    /// Without this they arrive as chests standing in the middle of nowhere, which looks exactly
    /// like a chest this app does not know about.
    /// </summary>
    [Theory]
    [InlineData("mypack.trails.chests")]
    [InlineData("tw.chests.chestopener")]
    [InlineData("pack.jp.route.step1")]
    public void A_way_to_a_thing_is_not_the_thing(string type)
    {
        Assert.Null(MarkerKinds.Of(type));
    }

    /// <summary>
    /// Everything this app has no opinion about is dropped, including the things the API already
    /// publishes properly. A target whose kind is a guess is worse than no target.
    /// </summary>
    [Theory]
    [InlineData("mypack.heropoints.kryta")]
    [InlineData("mypack.vistas")]
    [InlineData("")]
    [InlineData(null)]
    public void An_unknown_category_is_carried_by_nothing(string? type)
    {
        Assert.Null(MarkerKinds.Of(type));
    }

    /// <summary>Tekkit's All-In-One, paths as in the pack fetched on 2026-10-06.</summary>
    [Theory]
    [InlineData("tw_guides.tw_jp.tw_jp_kryta_trollsrevenge.tw_jp_kryta_trollsrevenge_signs.tw_jp_kryta_trollsrevenge_startsign",
        TargetKind.JumpingPuzzle)]
    [InlineData("tw_guides.tw_eod.tw_eod_newkainengcity.tw_eod_newkainengcity_jp_windthroughtthewalls.tw_eod_newkainengcity_jp_windthroughtthewalls_signs.tw_eod_newkainengcity_jp_windthroughtthewalls_startsign",
        TargetKind.JumpingPuzzle)]
    [InlineData("tw_guides.tw_eod.tw_eod_dragonsend.tw_eod_dragonsend_jadetechchests.tw_eod_dragonsend_jadetechchests_jadetechchest",
        TargetKind.Chest)]
    [InlineData("tw_guides.tw_lws2.tw_lws2_silverwastes.tw_lws2_silverwastes_chestfarm.tw_lws2_silverwastes_chestfarm_chest",
        TargetKind.Chest)]
    [InlineData("tw_guides.tw_eod.tw_eod_dragonsend.tw_eod_dragonsend_deadplay.tw_eod_dragonsend_deadplay_chest07",
        TargetKind.Chest)]
    public void Tekkits_pack_is_read_by_its_own_layout(string type, TargetKind expected)
    {
        Assert.Equal(expected, MarkerKinds.Of(type));
    }

    /// <summary>
    /// What sits beside a Tekkit chest or puzzle and is not it: the steps through a puzzle, the
    /// chest hint along it, its finish, and a chest run's signs and trail switch.
    /// </summary>
    [Theory]
    [InlineData("tw_guides.tw_jp.tw_jp_kryta_trollsrevenge.tw_jp_kryta_trollsrevenge_signs.tw_jp_kryta_trollsrevenge_trail_jump")]
    [InlineData("tw_guides.tw_jp.tw_jp_kryta_trollsrevenge.tw_jp_kryta_trollsrevenge_signs.tw_jp_kryta_trollsrevenge_trail_chest")]
    [InlineData("tw_guides.tw_jp.tw_jp_kryta_trollsrevenge.tw_jp_kryta_trollsrevenge_signs.tw_jp_kryta_trollsrevenge_finish")]
    [InlineData("tw_guides.tw_eod.tw_eod_dragonsend.tw_eod_dragonsend_jadetechchests.tw_eod_dragonsend_jadetechchests_signs.tw_eod_dragonsend_jadetechchests_startsign")]
    [InlineData("tw_guides.tw_eod.tw_eod_dragonsend.tw_eod_dragonsend_jadetechchests.tw_eod_dragonsend_jadetechchests_toggletrail")]
    [InlineData("tw_guides.tw_festivals.tw_sab.tw_sab_normal.tw_sab_normal_chests.tw_sab_normal_chests_jumponspot")]
    public void Tekkits_way_to_a_thing_is_not_the_thing(string type)
    {
        Assert.Null(MarkerKinds.Of(type));
    }

    /// <summary>
    /// Puzzles whose path never says "jp", known by the wiki's name for them, and by the pack's own
    /// name for two. Paths as in the pack fetched on 2026-10-13.
    /// </summary>
    [Theory]
    [InlineData("tw_guides.tw_lws3.tw_lws3_sirenslanding.tw_lws3_sirenslanding_abaddonsascent.tw_lws3_sirenslanding_abaddonsascent_signs.tw_lws3_sirenslanding_abaddonsascent_startsign")]
    [InlineData("tw_guides.tw_voe.tw_voe_eternitysgarden.tw_voe_eternitysgarden_artificersrise.tw_voe_eternitysgarden_artificersrise_signs.tw_voe_eternitysgarden_artificersrise_startsign")]
    [InlineData("tw_guides.tw_hot.tw_hot_td.tw_hot_td_discodelver.tw_hot_td_discodelver_startsign")]
    public void A_puzzle_the_wiki_names_is_a_puzzle_without_saying_jp(string type)
    {
        Assert.Equal(TargetKind.JumpingPuzzle, MarkerKinds.Of(type));
    }

    /// <summary>
    /// Inside such a puzzle only the start sign counts, as inside a "jp" one: the jumps are the way
    /// through and the Clock Tower's chests are along it. An adventure has the same start sign and
    /// the same jumps, and is not on the wiki's list.
    /// </summary>
    [Theory]
    [InlineData("tw_guides.tw_lws3.tw_lws3_sirenslanding.tw_lws3_sirenslanding_abaddonsascent.tw_lws3_sirenslanding_abaddonsascent_signs.tw_lws3_sirenslanding_abaddonsascent_trail_jump")]
    [InlineData("tw_guides.tw_festivals.tw_festival_halloween.tw_festival_halloween_clocktower.tw_festival_halloween_clocktower_signs.tw_festival_halloween_clocktower_chest")]
    [InlineData("tw_guides.tw_hot.tw_hot_ab.tw_hot_ab_adv_fungusamongus.tw_hot_ab_adv_fungusamongus_signs.tw_hot_ab_adv_fungusamongus_startsign")]
    public void Around_a_named_puzzle_only_its_start_counts(string type)
    {
        Assert.Null(MarkerKinds.Of(type));
    }

    /// <summary>"jp" is two letters; it must be a whole segment or it matches words that are not
    /// it.</summary>
    [Fact]
    public void Jp_is_matched_as_a_segment_and_not_as_letters()
    {
        Assert.Null(MarkerKinds.Of("mypack.jpeg.things"));
        Assert.Equal(TargetKind.JumpingPuzzle, MarkerKinds.Of("mypack.jp"));
    }
}
