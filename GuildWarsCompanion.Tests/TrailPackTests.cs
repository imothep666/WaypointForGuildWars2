using System.Buffers.Binary;

using GuildWarsCompanion.Services;
using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// Reading a TacO <c>.trl</c>.
///
/// The fixture is built the same way <c>MumbleLinkTests</c> builds its block: field by field from
/// the format as measured against Teh's Trails, rather than from whatever the reader happens to
/// do. Every number here is one the real files were checked to carry.
/// </summary>
public class TrailPackTests
{
    private static byte[] File(int version, int mapId, params float[] values)
    {
        var bytes = new byte[8 + values.Length * 4];

        BinaryPrimitives.WriteInt32LittleEndian(bytes, version);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), mapId);

        for (var i = 0; i < values.Length; i++)
            BinaryPrimitives.WriteInt32LittleEndian(
                bytes.AsSpan(8 + i * 4), BitConverter.SingleToInt32Bits(values[i]));

        return bytes;
    }

    /// <summary>The first trail read out of the real pack, near enough: map 1428, points in
    /// metres with y as height.</summary>
    [Fact]
    public void Reads_a_map_id_and_a_line()
    {
        var trail = TrailPack.Read(File(0, 1428,
            -817.38f, 2.12f, 521.23f,
            -829.52f, 1.97f, 533.26f));

        Assert.NotNull(trail);
        Assert.Equal(1428, trail.MapId);

        var points = Assert.Single(trail.Segments);

        Assert.Equal(2, points.Count);
        Assert.Equal(-817.38, points[0].X, 2);
        Assert.Equal(521.23, points[0].Z, 2);
    }

    /// <summary>Height is not a position on a flat map, exactly as for markers.</summary>
    [Fact]
    public void Drops_the_height()
    {
        var trail = TrailPack.Read(File(0, 15, 10f, 999f, 20f, 30f, -999f, 40f));

        var points = Assert.Single(trail!.Segments);

        Assert.Equal([(10.0, 20.0), (30.0, 40.0)], points);
    }

    /// <summary>
    /// A zero triple is the pen lifting. Drawn as a point it would run every route through the
    /// north-west corner of the map.
    /// </summary>
    [Fact]
    public void Breaks_the_line_at_a_zero_triple()
    {
        var trail = TrailPack.Read(File(0, 15,
            10f, 0f, 10f,
            20f, 0f, 20f,
            0f, 0f, 0f,
            50f, 0f, 50f,
            60f, 0f, 60f));

        Assert.Equal(2, trail!.Segments.Count);
        Assert.Equal(4, trail.Points);
        Assert.Equal((50.0, 50.0), trail.Segments[1][0]);
    }

    /// <summary>
    /// A break a metre or two wide is the author pausing, not a port — 62 of 76 on Lady Elyssa's
    /// Silverwastes trails. Joined, the walk stays one walk.
    /// </summary>
    [Fact]
    public void Joins_over_a_break_that_goes_nowhere()
    {
        var trail = TrailPack.Read(File(0, 15,
            10f, 0f, 10f,
            20f, 0f, 20f,
            0f, 0f, 0f,
            21f, 0f, 21f,
            30f, 0f, 30f));

        Assert.Equal(4, Assert.Single(trail!.Segments).Count);
        Assert.Equal(4, trail.Heights![0].Count);
    }

    /// <summary>A line needs two ends; packs do contain runs of one.</summary>
    [Fact]
    public void Drops_a_run_of_a_single_point()
    {
        var trail = TrailPack.Read(File(0, 15,
            10f, 0f, 10f,
            0f, 0f, 0f,
            50f, 0f, 50f,
            60f, 0f, 60f));

        Assert.Single(trail!.Segments);
        Assert.Equal((50.0, 50.0), trail.Segments[0][0]);
    }

    [Fact]
    public void Answers_nothing_for_a_file_with_no_line_in_it()
    {
        Assert.Null(TrailPack.Read(File(0, 15, 10f, 0f, 10f)));
        Assert.Null(TrailPack.Read(File(0, 15)));
        Assert.Null(TrailPack.Read(new byte[] { 1, 2, 3 }));
    }

    /// <summary>
    /// Every one of the 526 trails measured carried version 0. Another number is a layout this
    /// reader has never seen, and reading it anyway would produce plausible-looking rubbish.
    /// </summary>
    [Fact]
    public void Refuses_a_version_it_has_never_been_measured_against()
    {
        Assert.Null(TrailPack.Read(File(1, 15, 10f, 0f, 10f, 20f, 0f, 20f)));
    }

    [Fact]
    public void Refuses_a_map_id_that_cannot_be_a_map()
    {
        Assert.Null(TrailPack.Read(File(0, 0, 10f, 0f, 10f, 20f, 0f, 20f)));
        Assert.Null(TrailPack.Read(File(0, -1, 10f, 0f, 10f, 20f, 0f, 20f)));
    }

    /// <summary>A trailing partial point — a truncated download — is ignored rather than fatal.</summary>
    [Fact]
    public void Ignores_bytes_that_do_not_make_a_whole_point()
    {
        var whole = File(0, 15, 10f, 0f, 10f, 20f, 0f, 20f);
        var truncated = whole.Concat(new byte[] { 7, 7, 7 }).ToArray();

        var trail = TrailPack.Read(truncated);

        Assert.Equal(2, Assert.Single(trail!.Segments).Count);
    }

    [Fact]
    public void Ends_a_run_at_a_point_that_is_not_a_number()
    {
        var trail = TrailPack.Read(File(0, 15,
            10f, 0f, 10f,
            20f, 0f, 20f,
            float.NaN, 0f, 5f,
            50f, 0f, 50f,
            60f, 0f, 60f));

        Assert.Equal(2, trail!.Segments.Count);
    }

    [Fact]
    public void Stops_at_the_ceiling_it_is_given()
    {
        var values = new List<float>();

        for (var i = 0; i < 100; i++) values.AddRange([i + 1f, 0f, i + 1f]);

        var trail = TrailPack.Read(File(0, 15, [.. values]), mostPoints: 10);

        Assert.Equal(10, trail!.Points);
    }
}
