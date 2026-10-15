using System.Text.Json;

using GuildWarsCompanion.Bridge.Protocol;
using GuildWarsCompanion.Gw2;
using GuildWarsCompanion.Services;

using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// The trail to the objective, on its way into the game: the format it travels in, the metres it is
/// drawn in, and when the phone sends it at all.
/// </summary>
public class GuideWireTests
{
    private static GuidedTrail Trail() =>
        new(1015, (310.5, 375, -300.25), [(0, 400, 0), (100.5, 398.25, -10), (300, 375, -300)]);

    [Fact]
    public void A_trail_reads_back_as_it_was_written()
    {
        var read = GuideWire.Parse(GuideWire.Write(Trail()), out var clear)!;

        Assert.False(clear);
        Assert.Equal(1015, read.MapId);
        Assert.Equal((310.5, 375.0, -300.25), read.Target);
        Assert.Equal([(0.0, 400.0, 0.0), (100.5, 398.25, -10.0), (300.0, 375.0, -300.0)], read.Points);
    }

    [Fact]
    public void Nothing_to_draw_is_said_as_clear()
    {
        Assert.Null(GuideWire.Parse(GuideWire.Write(null), out var clear));
        Assert.True(clear);
    }

    [Fact]
    public void Numbers_are_written_the_same_whatever_the_phone_speaks()
    {
        var before = Thread.CurrentThread.CurrentCulture;

        try
        {
            // German writes 100,5; a trail written that way would read as a hundred and five.
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");

            Assert.Contains("p 100.5 398.25 -10", GuideWire.Write(Trail()));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = before;
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("guide 2\nmap 1015\ntarget 1 2 3\np 0 0 0\np 1 1 1\n")]      // a version never written
    [InlineData("guide 1\ntarget 1 2 3\np 0 0 0\np 1 1 1\n")]                // no map
    [InlineData("guide 1\nmap 1015\np 0 0 0\np 1 1 1\n")]                    // no target
    [InlineData("guide 1\nmap 1015\ntarget 1 2 3\np 0 0 0\n")]               // one point is not a line
    [InlineData("guide 1\nmap 1015\ntarget 1 2 3\np 0 0 0\np 1 NaN 1\n")]    // a height nobody knows
    [InlineData("guide 1\nmap 1015\ntarget 1 2 3\np 0 0 0\np 1e9 1 1\n")]    // somewhere past any map
    [InlineData("guide 1\nmap 1015\ntarget 1 2 3\np 0 0 0\n/gg\n")]          // anything else at all
    public void Anything_that_is_not_quite_a_trail_is_refused(string text)
    {
        Assert.Null(GuideWire.Parse(text, out var clear));
        Assert.False(clear);
    }

    /// <summary>An objective with no trail to it: no points, and a height that may be unknown.</summary>
    [Fact]
    public void An_objective_alone_reads_back_with_its_height_unknown()
    {
        var alone = new GuidedTrail(1015, (310.5, 0, -300.25), [], heightKnown: false);

        var text = GuideWire.Write(alone);
        var read = GuideWire.Parse(text, out var clear)!;

        Assert.Contains("height unknown", text);
        Assert.False(clear);
        Assert.True(read.IsObjectiveOnly);
        Assert.False(read.HeightKnown);
        Assert.Equal((310.5, 0.0, -300.25), read.Target);
    }

    [Fact]
    public void A_chest_with_its_own_height_says_nothing_about_height()
    {
        var chest = new GuidedTrail(1015, (310.5, 512.25, -300.25), []);

        var read = GuideWire.Parse(GuideWire.Write(chest), out _)!;

        Assert.DoesNotContain("height", GuideWire.Write(chest));
        Assert.True(read.HeightKnown);
        Assert.Equal(512.25, read.Target.Up);
    }

    [Fact]
    public void A_trail_longer_than_the_limit_is_refused_rather_than_cut()
    {
        var text = "guide 1\nmap 1015\ntarget 0 0 0\n" +
                   string.Concat(Enumerable.Range(0, GuideWire.MostPoints + 1).Select(i => $"p {i} 0 0\n"));

        Assert.Null(GuideWire.Parse(text, out _));
    }

    [Fact]
    public void Every_line_fits_the_module_link()
    {
        foreach (var line in GuideWire.Write(Trail()).Split('\n'))
            Assert.True(line.Length < ModuleWire.MaxLine);
    }

    [Fact]
    public void The_objective_travels_with_its_name()
    {
        var read = GuideWire.Parse(GuideWire.Write(Trail().Named("Kammer der Bündnisse")), out _)!;

        Assert.Equal("Kammer der Bündnisse", read.Name);
        Assert.Equal(3, read.Points.Count);
    }

    [Fact]
    public void A_trail_without_a_name_says_nothing_about_one()
    {
        Assert.DoesNotContain("name", GuideWire.Write(Trail()));
        Assert.Null(GuideWire.Parse(GuideWire.Write(Trail()), out _)!.Name);
    }

    [Fact]
    public void A_name_cannot_smuggle_in_a_line_of_its_own()
    {
        // A newline in a name would otherwise be a point of the trail.
        var text = GuideWire.Write(Trail().Named("Chest\np 9 9 9"));
        var read = GuideWire.Parse(text, out _)!;

        Assert.Equal("Chest p 9 9 9", read.Name);
        Assert.Equal(3, read.Points.Count);
    }

    [Fact]
    public void A_long_name_is_cut_on_the_way_out_and_refused_on_the_way_in()
    {
        var written = GuideWire.Parse(GuideWire.Write(Trail().Named(new string('a', 200))), out _)!;

        Assert.Equal(GuideWire.LongestName, written.Name!.Length);
        Assert.EndsWith("…", written.Name);

        var sent = $"{GuideWire.Header}\nmap 1015\nname {new string('a', GuideWire.LongestName + 1)}\ntarget 1 2 3\n";
        Assert.Null(GuideWire.Parse(sent, out _));
    }

    [Fact]
    public void The_module_link_marks_where_a_trail_starts_and_ends()
    {
        Assert.Equal(ModuleLineKind.Trail, ModuleWire.Parse(ModuleWire.Trail)!.Kind);
        Assert.Equal(ModuleLineKind.End, ModuleWire.Parse(ModuleWire.End)!.Kind);
    }

    [Fact]
    public void Drawing_in_somebodys_game_needs_the_pairing_code()
    {
        var head = BridgeProtocol.ParseHead($"POST {BridgeProtocol.GuidePath} HTTP/1.1\r\n{BridgeProtocol.CodeHeader}: 123456")!;
        var wrong = BridgeProtocol.ParseHead($"POST {BridgeProtocol.GuidePath} HTTP/1.1\r\n{BridgeProtocol.CodeHeader}: 999999")!;
        var get = BridgeProtocol.ParseHead($"GET {BridgeProtocol.GuidePath} HTTP/1.1\r\n{BridgeProtocol.CodeHeader}: 123456")!;

        Assert.Null(BridgeProtocol.RefuseGuide(head, "123456", null));
        Assert.Equal(403, BridgeProtocol.RefuseGuide(wrong, "123456", null));
        Assert.Equal(400, BridgeProtocol.RefuseGuide(get, "123456", null));
        Assert.Equal(503, BridgeProtocol.RefuseGuide(head, null, null));
    }
}

/// <summary>From the continent back to the game's metres, which is how the objective gets a place in
/// the world.</summary>
public class MetresTests
{
    private static readonly (double, double, double, double) SilverwastesMap = (-24576, -18432, 24576, 18432);
    private static readonly (double, double, double, double) SilverwastesContinent = (36608, 30592, 38656, 32128);

    [Theory]
    [InlineData(0, 0)]
    [InlineData(312.5, -140.25)]
    [InlineData(-600, 460)]
    public void There_and_back_is_where_it_started(double metresX, double metresZ)
    {
        var (mapX, mapY) = MapSpace.FromMetres(metresX, metresZ);
        var (cx, cy) = MapSpace.ToContinent(mapX, mapY, SilverwastesMap, SilverwastesContinent);

        var (x, z) = MapSpace.ToMetres(cx, cy, SilverwastesMap, SilverwastesContinent);

        Assert.Equal(metresX, x, 6);
        Assert.Equal(metresZ, z, 6);
    }
}

/// <summary>The stretch that goes into the game, and when it is sent.</summary>
public class TrailForGameTests
{
    private static RouteLine Around(IReadOnlyList<double>? heights) =>
        new(1015, "legs.map.barefoot", "Around the wall",
            [(0, 0), (100, 0), (200, 0), (300, 0), (300, -100)],
            Heights: heights,
            Metres: [(10, -10), (11, -11), (12, -12), (13, -13), (14, -14)]);

    [Fact]
    public void The_stretch_goes_out_in_the_games_metres_with_heights()
    {
        var guide = TrailGuide.Find([Around([400, 399, 398, 397, 380])], 300, -100)!;

        var trail = guide.ForGame(1, (99, 88))!;

        Assert.Equal(1015, trail.MapId);
        Assert.Equal([(11.0, 399.0, -11.0), (12.0, 398.0, -12.0), (13.0, 397.0, -13.0), (14.0, 380.0, -14.0)], trail.Points);

        // The objective where the API puts it, at the height of the trail beside it.
        Assert.Equal((99.0, 380.0, 88.0), trail.Target);
    }

    [Fact]
    public void A_trail_without_heights_is_not_drawn_in_the_world()
    {
        Assert.Null(TrailGuide.Find([Around(null)], 300, -100)!.ForGame(0, (0, 0)));
        Assert.Null(TrailGuide.Find([Around([400, double.NaN, 398, 397, 380])], 300, -100)!.ForGame(0, (0, 0)));
    }

    private static readonly MapInfo Silverwastes = new(
        1015, "The Silverwastes", 11, 1, 1,
        [[36608, 30592], [38656, 32128]],
        [[-24576, -18432], [24576, 18432]]);

    private static CompassTarget Target(double x = 300, double y = -100) =>
        new("Picaroon Scratch", TargetKind.Landmark, 1015, "The Silverwastes", x, y);

    private static (TrailShare Share, FakeNetwork Network) Build(bool refusesNames = false)
    {
        var secret = BridgeProtocol.NewPairingSecret();
        var desk = new FakePc("DESKID", "Desktop", secret, "192.168.1.20:47623") { RefusesNames = refusesNames };
        var network = new FakeNetwork().With(desk);
        var known = new KnownBridges(new MemoryBridgeStore());
        var journal = new Journal(Path.Combine(Path.GetTempPath(), "gw2-bridge-tests.log"));

        known.PairAsync(desk.Card).GetAwaiter().GetResult();

        var client = new BridgeClient(network, new BridgeFinder(network, network, known, journal), journal);

        return (new TrailShare(client), network);
    }

    private static int Sent(FakeNetwork network)
    {
        // Sends are fire and forget; the fake answers at once, so a moment is enough.
        Thread.Sleep(50);

        lock (network.Asked) return network.Asked.Count(url => url.EndsWith(BridgeProtocol.GuidePath));
    }

    [Fact]
    public void The_same_trail_to_the_same_objective_is_sent_once()
    {
        var (share, network) = Build();
        var guide = TrailGuide.Find([Around([400, 399, 398, 397, 380])], 300, -100);

        // The compass, then the map, each with its own idea of where the character is.
        share.Share(guide, Target(), Silverwastes, (0, 0));
        share.Share(guide, Target(), Silverwastes, (100, 0));

        Assert.Equal(1, Sent(network));
    }

    [Fact]
    public void Nothing_is_sent_before_there_is_anything_to_say()
    {
        var (share, network) = Build();

        share.Share(null, null, null, null);

        Assert.Equal(0, Sent(network));
    }

    [Fact]
    public void Looking_at_another_map_leaves_the_line_in_the_game()
    {
        var (share, network) = Build();
        var guide = TrailGuide.Find([Around([400, 399, 398, 397, 380])], 300, -100);

        share.Share(guide, Target(), Silverwastes, (0, 0));

        // The map screen browsing somewhere else: no guide there, same objective.
        share.Share(null, Target(), null, null);

        Assert.Equal(1, Sent(network));
    }

    [Fact]
    public void A_new_objective_without_a_trail_takes_the_old_line_down()
    {
        var (share, network) = Build();
        var guide = TrailGuide.Find([Around([400, 399, 398, 397, 380])], 300, -100);

        share.Share(guide, Target(), Silverwastes, (0, 0));
        share.Share(null, Target(5000, 5000), Silverwastes, (0, 0));

        Assert.Equal(2, Sent(network));
    }

    [Fact]
    public void Putting_the_objective_down_takes_the_line_down()
    {
        var (share, network) = Build();
        var guide = TrailGuide.Find([Around([400, 399, 398, 397, 380])], 300, -100);

        share.Share(guide, Target(), Silverwastes, (0, 0));
        share.Share(null, null, null, null);

        Assert.Equal(2, Sent(network));
    }

    private static GuidedTrail? LastGuide(FakeNetwork network)
    {
        Thread.Sleep(50);

        lock (network.Asked)
        {
            var at = network.Asked.FindLastIndex(url => url.EndsWith(BridgeProtocol.GuidePath));

            return at < 0 ? null : GuideWire.Parse(network.Bodies[at], out _);
        }
    }

    [Fact]
    public void The_trail_goes_out_with_the_objectives_name()
    {
        var (share, network) = Build();

        share.Share(TrailGuide.Find([Around([400, 399, 398, 397, 380])], 300, -100), Target(), Silverwastes, (0, 0));

        Assert.Equal("Picaroon Scratch", LastGuide(network)!.Name);
    }

    [Fact]
    public void A_bridge_from_before_names_still_gets_the_trail()
    {
        var (share, network) = Build(refusesNames: true);

        share.Share(TrailGuide.Find([Around([400, 399, 398, 397, 380])], 300, -100), Target(), Silverwastes, (0, 0));

        var sent = LastGuide(network)!;

        // The same trail, asked twice: once with its name, refused, and once without.
        Assert.Null(sent.Name);
        Assert.NotEmpty(sent.Points);
        Assert.Equal(2, Sent(network));
    }

    [Fact]
    public void An_objective_with_no_trail_goes_to_the_game_alone()
    {
        var (share, network) = Build();

        share.Share(null, Target() with { Up = 512.5 }, Silverwastes, (0, 0));

        var sent = LastGuide(network)!;

        Assert.True(sent.IsObjectiveOnly);
        Assert.True(sent.HeightKnown);
        Assert.Equal(512.5, sent.Target.Up);
    }

    [Fact]
    public void An_objective_the_api_gives_no_height_says_so()
    {
        var (share, network) = Build();

        share.Share(null, Target(), Silverwastes, (0, 0));

        Assert.False(LastGuide(network)!.HeightKnown);
    }

    /// <summary>A world boss's point is the middle of its map; a beam there leads to nothing.</summary>
    [Fact]
    public void An_imprecise_objective_is_not_marked_in_the_game()
    {
        var (share, network) = Build();

        share.Share(null, Target() with { Precise = false }, Silverwastes, (0, 0));

        Assert.Equal(0, Sent(network));
    }

    [Fact]
    public void A_screen_without_the_trail_does_not_swap_it_for_the_objective_alone()
    {
        var (share, network) = Build();
        var guide = TrailGuide.Find([Around([400, 399, 398, 397, 380])], 300, -100);

        share.Share(guide, Target(), Silverwastes, (0, 0));
        share.Share(null, Target(), Silverwastes, (0, 0));

        Assert.Equal(1, Sent(network));
        Assert.False(LastGuide(network)!.IsObjectiveOnly);
    }

    private static GameReport Reached(int map, double x, double z) =>
        new(null, true, string.Empty, Arrived: new TrailArrival(map, x, z));

    [Fact]
    public void Reaching_the_objective_in_the_game_is_heard_once()
    {
        var (share, _) = Build();
        var guide = TrailGuide.Find([Around([400, 399, 398, 397, 380])], 300, -100);

        share.Share(guide, Target(), Silverwastes, (0, 0));

        var sent = guide!.ForGame(guide.Leg(0, 0).At, MapSpace.ToMetres(300, -100, Silverwastes.MapArea!.Value, Silverwastes.ContinentArea!.Value))!;
        var report = Reached(1015, sent.Target.X, sent.Target.Z);

        // The compass hears it and clears the objective; the map, hearing the same report, is not
        // told again.
        Assert.True(share.HasArrived(report));
        Assert.False(share.HasArrived(report));
    }

    [Fact]
    public void Somewhere_else_reached_is_not_this_objective()
    {
        var (share, _) = Build();
        var guide = TrailGuide.Find([Around([400, 399, 398, 397, 380])], 300, -100);

        share.Share(guide, Target(), Silverwastes, (0, 0));

        Assert.False(share.HasArrived(Reached(1015, 99999, 99999)));
        Assert.False(share.HasArrived(Reached(28, 0, 0)));
        Assert.False(share.HasArrived(new GameReport(null, true, string.Empty)));
    }

    [Fact]
    public void Nothing_sent_means_nothing_reached()
    {
        var (share, _) = Build();

        Assert.False(share.HasArrived(Reached(1015, 0, 0)));
    }

    [Fact]
    public void An_arrival_travels_in_the_report()
    {
        var report = Reached(1015, 1204.5, -8812.1);
        var json = JsonSerializer.Serialize(report, BridgeJson.Default.GameReport);

        Assert.Equal(new TrailArrival(1015, 1204.5, -8812.1), JsonSerializer.Deserialize(json, BridgeJson.Default.GameReport)!.Arrived);

        // And a report from a bridge older than arrivals reads as none.
        Assert.Null(JsonSerializer.Deserialize("{\"State\":null,\"IsLive\":true,\"Note\":\"\"}", BridgeJson.Default.GameReport)!.Arrived);
    }
}
