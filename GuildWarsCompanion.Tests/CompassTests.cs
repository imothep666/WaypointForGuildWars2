using GuildWarsCompanion.Services;

using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// Turning two continent positions and a facing into "which way do I turn".
///
/// Continent coordinates grow east and south, which is the one thing here worth pinning: every
/// sign in <see cref="Bearing"/> follows from it, and getting it wrong mirrors the needle without
/// making any single number look wrong.
/// </summary>
public class BearingTests
{
    private const double Quarter = Math.PI / 2;

    /// <summary>Degrees, because a test that fails should say "you are 90 degrees out".</summary>
    private static double Degrees(double radians) => radians * 180 / Math.PI;

    [Fact]
    public void North_is_negative_y()
    {
        Assert.Equal(0, Bearing.To(100, 100, 100, 50), 6);
    }

    [Fact]
    public void East_is_a_quarter_turn_clockwise()
    {
        Assert.Equal(90, Degrees(Bearing.To(100, 100, 150, 100)), 6);
    }

    [Fact]
    public void South_is_half_a_turn()
    {
        Assert.Equal(180, Math.Abs(Degrees(Bearing.To(100, 100, 100, 150))), 6);
    }

    [Fact]
    public void West_is_a_quarter_turn_anticlockwise()
    {
        Assert.Equal(-90, Degrees(Bearing.To(100, 100, 50, 100)), 6);
    }

    /// <summary>
    /// Facing north with the target due east means turn right. This is the whole screen in one
    /// assertion.
    /// </summary>
    [Fact]
    public void Facing_north_with_the_target_east_turns_right()
    {
        Assert.Equal(90, Degrees(Bearing.Relative(100, 100, 0, 150, 100)), 6);
    }

    [Fact]
    public void Facing_the_target_is_straight_ahead()
    {
        Assert.Equal(0, Bearing.Relative(100, 100, Quarter, 150, 100), 6);
    }

    /// <summary>
    /// Facing east with the target north is a left turn, not three-quarters of a right one.
    ///
    /// The case <see cref="Bearing.Normalise"/> exists for: without folding, the needle takes
    /// the long way round every time the target crosses behind the player.
    /// </summary>
    [Fact]
    public void A_turn_is_always_the_short_way_round()
    {
        Assert.Equal(-90, Degrees(Bearing.Relative(100, 100, Quarter, 100, 50)), 6);
    }

    [Theory]
    [InlineData(0, "straight ahead")]
    [InlineData(30, "bear right")]
    [InlineData(-30, "bear left")]
    [InlineData(90, "turn right")]
    [InlineData(-150, "hard left")]
    [InlineData(180, "turn around")]
    public void The_instruction_matches_the_turn(double degrees, string expected)
    {
        Assert.Equal(expected, Bearing.Describe(degrees * Math.PI / 180));
    }

    [Fact]
    public void Distance_is_plain_pythagoras_in_continent_units()
    {
        Assert.Equal(5, Bearing.Distance(0, 0, 3, 4), 6);
    }

    /// <summary>
    /// A real pair off the map the simulator walks: Queensdale's Godslost Swamp sits south-east
    /// of the middle of the map, so a character at the centre facing north turns right and back.
    /// </summary>
    [Fact]
    public void A_real_queensdale_pair_points_south_east()
    {
        // Map centre from /v2/maps/15 continent_rect, swamp from the floor endpoint's sectors.
        var turn = Degrees(Bearing.Relative(44416, 29248, 0, 45060.6, 30002.2));

        Assert.InRange(turn, 130, 150);
    }
}

/// <summary>
/// What the compass is allowed to point at.
/// </summary>
public class CompassTargetTests
{
    /// <summary>
    /// Every place the timetable names has a map id.
    ///
    /// The guard that matters when the boss rotation changes: <see cref="EventSchedule"/> is
    /// hardcoded prose and the compass looks its locations up by name, so a renamed or added
    /// location would silently drop a boss off this screen rather than fail.
    /// </summary>
    [Fact]
    public void Every_scheduled_boss_location_has_a_map()
    {
        var scheduled = EventSchedule
            .NextAppearances(DateTimeOffset.UnixEpoch)
            .Select(b => b.Location)
            .Distinct();

        foreach (var location in scheduled)
            Assert.True(
                BossMaps.All.ContainsKey(location),
                $"No map id for '{location}'. Add it to BossMaps, from /v2/maps.");
    }

    /// <summary>
    /// A world boss is never claimed to be precisely placed.
    ///
    /// The API publishes no arena coordinate, so the screen points at the middle of the map and
    /// says so. If this ever passes accidentally because someone hardcoded spawn points, that is
    /// the thing to argue about rather than a test to delete.
    /// </summary>
    [Fact]
    public void A_map_centre_target_is_not_precise()
    {
        var boss = new CompassTarget(
            "Shadow Behemoth", TargetKind.WorldBoss, 15, "Queensdale", 44416, 29248,
            Precise: false, Note: "in 12 min");

        Assert.False(boss.Precise);
        Assert.Equal("World boss", boss.KindLabel);
    }

    [Fact]
    public void A_landmark_is_precise_by_default()
    {
        var heart = new CompassTarget(
            "Help the Seraph", TargetKind.Heart, 15, "Queensdale", 44063.9, 29886.6);

        Assert.True(heart.Precise);
        Assert.Equal("Heart", heart.KindLabel);
    }
}

/// <summary>
/// The one case where a map-centre target has to be suppressed rather than drawn.
/// </summary>
public class MapCentreTargetTests
{
    private static CompassTarget Boss(int mapId) =>
        new("Shadow Behemoth", TargetKind.WorldBoss, mapId, "Queensdale", 44416, 29248,
            Precise: false);

    /// <summary>
    /// A boss on the map you are standing in points at the middle of that map, which is a real
    /// point and useless information. Found on the dial: it sat four hundred units away among
    /// the local waypoints and looked exactly like a genuine nearby target.
    /// </summary>
    [Fact]
    public void A_boss_on_this_map_cannot_be_pointed_at()
    {
        Assert.False(Boss(15).CanPointFrom(15));
    }

    [Fact]
    public void A_boss_on_another_map_still_gives_a_direction()
    {
        Assert.True(Boss(30).CanPointFrom(15));
    }

    /// <summary>A real coordinate is always worth pointing at, including on this map — that is
    /// the entire difference the flag carries.</summary>
    [Fact]
    public void A_precise_target_is_always_pointable()
    {
        var heart = new CompassTarget(
            "Help the Seraph", TargetKind.Heart, 15, "Queensdale", 44063.9, 29886.6);

        Assert.True(heart.CanPointFrom(15));
        Assert.True(heart.CanPointFrom(30));
    }
}

/// <summary>
/// Hiding what is already finished.
/// </summary>
public class CompassFilterTests
{
    private static CompassTarget Hero(string id) =>
        new("Hero point", TargetKind.HeroPoint, 15, "Queensdale", 45135.5, 29863.7, Id: id);

    private static CompassTarget Heart() =>
        new("Help the Seraph", TargetKind.Heart, 15, "Queensdale", 44063.9, 29886.6);

    [Fact]
    public void A_finished_hero_point_is_dropped()
    {
        var filter = new CompassFilter(new HashSet<string> { "0-7" });

        Assert.False(filter.Keep(Hero("0-7")));
        Assert.True(filter.Keep(Hero("0-8")));
    }

    [Fact]
    public void A_boss_killed_today_is_dropped()
    {
        var filter = new CompassFilter(new HashSet<string> { "shadow_behemoth" });

        var boss = new CompassTarget(
            "Shadow Behemoth", TargetKind.WorldBoss, 15, "Queensdale", 44416, 29248,
            Precise: false, Id: "shadow_behemoth");

        Assert.False(filter.Keep(boss));
    }

    /// <summary>
    /// A heart carries no id, so nothing can mark it done and it always survives.
    ///
    /// Not an oversight: there is no renown heart completion anywhere in the API. Hiding one on a
    /// guess would be worse than leaving it on the dial.
    /// </summary>
    [Fact]
    public void A_heart_survives_every_filter()
    {
        var filter = new CompassFilter(new HashSet<string> { "0-7", "shadow_behemoth" });

        Assert.True(filter.Keep(Heart()));
        Assert.True(CompassFilter.Nothing.Keep(Heart()));
    }

    [Fact]
    public void Only_hero_points_and_bosses_are_filterable()
    {
        Assert.True(CompassFilter.Filterable(TargetKind.HeroPoint));
        Assert.True(CompassFilter.Filterable(TargetKind.WorldBoss));

        Assert.False(CompassFilter.Filterable(TargetKind.Heart));
        Assert.False(CompassFilter.Filterable(TargetKind.Waypoint));
        Assert.False(CompassFilter.Filterable(TargetKind.Vista));
        Assert.False(CompassFilter.Filterable(TargetKind.Landmark));
    }

    /// <summary>An empty filter hides nothing, which is what every failure path falls back to.</summary>
    [Fact]
    public void Nothing_hides_nothing()
    {
        Assert.True(CompassFilter.Nothing.Keep(Hero("0-7")));
    }
}

/// <summary>
/// The radial axis: distance in, fraction of the dial's radius out.
/// </summary>
public class RadarScaleTests
{
    [Fact]
    public void The_centre_is_here_and_the_rim_is_max_range()
    {
        Assert.Equal(0, RadarScale.Fraction(0));
        Assert.Equal(1, RadarScale.Fraction(RadarScale.MaxRange), 6);
    }

    [Fact]
    public void Further_is_always_further_out()
    {
        var last = -1.0;

        foreach (var d in new double[] { 0, 10, 50, 200, 800, 2500 })
        {
            var at = RadarScale.Fraction(d);

            Assert.True(at > last, $"{d} came out at {at}, not past {last}");

            last = at;
        }
    }

    /// <summary>
    /// Past the outer ring everything pins to the rim rather than running off it.
    /// </summary>
    [Fact]
    public void Beyond_range_clamps()
    {
        Assert.Equal(1, RadarScale.Fraction(RadarScale.MaxRange * 10), 6);
        Assert.True(RadarScale.BeyondRange(RadarScale.MaxRange + 1));
        Assert.False(RadarScale.BeyondRange(RadarScale.MaxRange));
    }

    /// <summary>
    /// Each ring is four times the last, so a logarithmic radius spaces them evenly — roughly a
    /// quarter of the dial apart. If someone retunes the range and the rings bunch up, this is
    /// what says so.
    /// </summary>
    [Fact]
    public void The_rings_are_evenly_spaced()
    {
        var at = RadarScale.Rings.Select(RadarScale.Fraction).ToList();

        for (var i = 0; i < at.Count; i++)
            Assert.InRange(at[i], (i + 1) * 0.25 - 0.06, (i + 1) * 0.25 + 0.06);
    }

    /// <summary>
    /// The near band has to be worth looking at: half the dial should cover the first couple of
    /// hundred units, which is the range a player is actually walking within.
    ///
    /// This is the property the 8000-unit outer ring was failing — it pushed 200 units down to a
    /// third of the radius and crushed every real landmark into a mid-radius ring.
    /// </summary>
    [Fact]
    public void The_inner_half_covers_the_walking_range()
    {
        Assert.InRange(RadarScale.Fraction(200), 0.40, 0.60);
    }

    /// <summary>
    /// Short enough to sit on the ring it labels.
    ///
    /// Deliberately not asserting the exact text of the thousands label: it is formatted in the
    /// user's culture, so it is "2.5k" on an English phone and "2,5k" on a German one, and both
    /// are right. What matters is that no label grows long enough to collide with the dial.
    /// </summary>
    [Theory]
    [InlineData(50, "50")]
    [InlineData(800, "800")]
    public void Ring_labels_below_a_thousand_are_plain(double distance, string expected)
    {
        Assert.Equal(expected, RadarScale.Label(distance));
    }

    [Fact]
    public void Every_ring_label_is_short()
    {
        foreach (var ring in RadarScale.Rings)
            Assert.InRange(RadarScale.Label(ring).Length, 1, 5);
    }

    [Fact]
    public void Thousands_are_abbreviated()
    {
        Assert.EndsWith("k", RadarScale.Label(2500));
        Assert.DoesNotContain("2500", RadarScale.Label(2500));
    }
}

/// <summary>
/// The trail of where the character has been.
/// </summary>
public class WalkedPathTests
{
    [Fact]
    public void It_records_movement_oldest_first()
    {
        var path = new WalkedPath();

        path.Add(15, 0, 0);
        path.Add(15, 0, 50);
        path.Add(15, 0, 100);

        var points = path.Points();

        Assert.Equal(3, points.Count);
        Assert.Equal(0, points[0].Y);
        Assert.Equal(100, points[^1].Y);
    }

    /// <summary>
    /// MumbleLink jitters by a unit or two while somebody stands still. Without this the buffer
    /// fills with a shivering dot in seconds and the real trail is pushed out of it.
    /// </summary>
    [Fact]
    public void Standing_still_records_nothing()
    {
        var path = new WalkedPath();

        path.Add(15, 100, 100);

        for (var i = 0; i < 50; i++) path.Add(15, 100 + i % 2, 100);

        Assert.Single(path.Points());
    }

    /// <summary>
    /// A waypoint teleport is not a walk, and joining it up would draw a confident straight line
    /// across terrain nobody crossed.
    /// </summary>
    [Fact]
    public void A_teleport_starts_a_new_trail()
    {
        var path = new WalkedPath();

        path.Add(15, 0, 0);
        path.Add(15, 0, 50);
        path.Add(15, 5000, 5000);

        var points = path.Points();

        Assert.Single(points);
        Assert.Equal(5000, points[0].X);
    }

    [Fact]
    public void Changing_map_clears_it()
    {
        var path = new WalkedPath();

        path.Add(15, 0, 0);
        path.Add(15, 0, 50);
        path.Add(30, 0, 100);

        Assert.Single(path.Points());
    }

    /// <summary>Older points fall off rather than the buffer growing without bound.</summary>
    [Fact]
    public void It_keeps_only_the_recent_past()
    {
        var path = new WalkedPath();

        for (var i = 0; i < WalkedPath.Capacity * 2; i++) path.Add(15, 0, i * 20);

        var points = path.Points();

        Assert.Equal(WalkedPath.Capacity, points.Count);

        // The newest survives and the oldest is gone.
        Assert.Equal((WalkedPath.Capacity * 2 - 1) * 20, points[^1].Y);
    }
}
