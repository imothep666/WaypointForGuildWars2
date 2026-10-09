using GuildWarsCompanion.Gw2;
using GuildWarsCompanion.Services;

using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// Placing a world boss at the arena it is actually fought in.
///
/// The numbers come from <c>/v1/event_details.json</c> and the conversion from the two rectangles
/// <c>/v2/maps/{id}</c> publishes. What is pinned here is the flip between the two coordinate
/// spaces and the table's agreement with the schedule — the two things that can go wrong without
/// producing a number that looks wrong.
/// </summary>
public class BossArenaTests
{
    /// <summary>Queensdale, from /v2/maps/15, both rectangles exactly as published.</summary>
    private static readonly (double Left, double Bottom, double Right, double Top) QueensdaleMap =
        (-43008, -27648, 43008, 30720);

    private static readonly (double Left, double Top, double Right, double Bottom) QueensdaleContinent =
        (42624, 28032, 46208, 30464);

    /// <summary>
    /// Shadow Behemoth's centre, converted, against a landmark the floor endpoint publishes.
    ///
    /// (44826, 30045) is 422 units from the Temple of the Ages — the Godslost Swamp, which is
    /// where it spawns. Checked against the API's own landmark rather than against the arithmetic
    /// that produced it: a mirrored y puts it in the fields north of Beetletun, which is just as
    /// plausible a pair of numbers and completely wrong.
    /// </summary>
    [Fact]
    public void The_shadow_behemoth_lands_in_the_godslost_swamp()
    {
        var at = MapSpace.ToContinent(9835, -17597, QueensdaleMap, QueensdaleContinent);

        Assert.Equal(44826, at.X, 0);
        Assert.Equal(30045, at.Y, 0);

        // Both read from /v2/continents/1/floors/1/regions/4/maps/15, not remembered.
        var temple = Bearing.Distance(at.X, at.Y, 45150.8, 29776.4);
        var shack = Bearing.Distance(at.X, at.Y, 44911.2, 29856.2);

        Assert.True(temple < 450, $"{temple:0} from the Temple of the Ages");
        Assert.True(shack < 250, $"{shack:0} from Deathroot Shack");
    }

    /// <summary>The corners, which is where a flip or a swapped pair shows up plainly.</summary>
    [Fact]
    public void The_maps_corners_map_onto_the_continents_corners()
    {
        var northWest = MapSpace.ToContinent(-43008, 30720, QueensdaleMap, QueensdaleContinent);

        Assert.Equal(42624, northWest.X, 6);
        Assert.Equal(28032, northWest.Y, 6);

        var southEast = MapSpace.ToContinent(43008, -27648, QueensdaleMap, QueensdaleContinent);

        Assert.Equal(46208, southEast.X, 6);
        Assert.Equal(30464, southEast.Y, 6);
    }

    /// <summary>North is a small continent y, and that is the whole of the flip.</summary>
    [Fact]
    public void Going_north_in_map_space_goes_up_the_continent()
    {
        var south = MapSpace.ToContinent(0, -10000, QueensdaleMap, QueensdaleContinent);
        var north = MapSpace.ToContinent(0, 10000, QueensdaleMap, QueensdaleContinent);

        Assert.True(north.Y < south.Y);
        Assert.Equal(north.X, south.X, 6);
    }

    [Fact]
    public void The_middle_of_one_rectangle_is_the_middle_of_the_other()
    {
        var at = MapSpace.ToContinent(0, 1536, QueensdaleMap, QueensdaleContinent);

        Assert.Equal(44416, at.X, 6);
        Assert.Equal(29248, at.Y, 6);
    }

    /// <summary>A map with no extent cannot place anything, and must not divide by zero doing
    /// it.</summary>
    [Fact]
    public void A_map_with_no_extent_answers_its_middle()
    {
        var at = MapSpace.ToContinent(100, 100, (0, 0, 0, 0), QueensdaleContinent);

        Assert.Equal(44416, at.X, 6);
        Assert.Equal(29248, at.Y, 6);
    }

    /// <summary>
    /// Every arena belongs to a map the schedule actually sends people to, so a row cannot quietly
    /// point at a boss on a map this app never mentions.
    /// </summary>
    [Fact]
    public void Every_arena_is_on_a_scheduled_map()
    {
        var scheduled = BossMaps.All.Values.ToHashSet();

        foreach (var (apiId, arena) in BossArenas.All)
            Assert.True(scheduled.Contains(arena.MapId), $"{apiId} is on map {arena.MapId}");
    }

    /// <summary>
    /// The table answers for every scheduled boss but one.
    ///
    /// Karka Queen is the exception and it is deliberate: v1 publishes four events with her exact
    /// name, all on Southsun Cove, all with different centres. She keeps the map-centre fallback.
    /// </summary>
    [Fact]
    public void Every_scheduled_boss_has_an_arena_except_the_one_that_cannot()
    {
        var bosses = EventSchedule.NextAppearances(DateTimeOffset.UnixEpoch)
            .Select(b => b.ApiId)
            .Distinct()
            .ToList();

        var missing = bosses.Where(id => !BossArenas.All.ContainsKey(id)).ToList();

        Assert.Equal(["karka_queen"], missing);
    }

    /// <summary>A row is only used for the map the schedule names. Claw of Jormag has events by
    /// that name in Bjora Marches too, and the schedule means Frostgorge Sound.</summary>
    [Fact]
    public void An_arena_is_refused_for_the_wrong_map()
    {
        Assert.NotNull(BossArenas.For("claw_of_jormag", 30));
        Assert.Null(BossArenas.For("claw_of_jormag", 1371));
        Assert.Null(BossArenas.For("karka_queen", 873));
    }

    /// <summary>
    /// A boss placed at its arena is precise, which is what earns it a filled needle and lets
    /// PortRoute answer at all.
    /// </summary>
    [Fact]
    public void An_arena_makes_a_boss_a_precise_target()
    {
        var arena = BossArenas.For("shadow_behemoth", 15);

        Assert.NotNull(arena);

        var at = MapSpace.ToContinent(arena.MapX, arena.MapY, QueensdaleMap, QueensdaleContinent);

        var boss = new CompassTarget(
            "Shadow Behemoth", TargetKind.WorldBoss, 15, "Queensdale", at.X, at.Y);

        Assert.True(boss.Precise);
        Assert.True(boss.CanPointFrom(15));
    }

    /// <summary>Both rectangles come off the same response, so the model has to read the pair the
    /// API actually sends.</summary>
    [Fact]
    public void A_map_publishes_both_rectangles()
    {
        var map = new MapInfo(
            15, "Queensdale", 4, 1, 1,
            [[42624, 28032], [46208, 30464]],
            [[-43008, -27648], [43008, 30720]]);

        Assert.Equal(QueensdaleContinent, map.ContinentArea);
        Assert.Equal(QueensdaleMap, map.MapArea);
    }
}
