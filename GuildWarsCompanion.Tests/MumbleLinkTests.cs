using System.Text;
using GuildWarsCompanion.Bridge.Protocol;
using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// Reading the block Guild Wars 2 publishes for Mumble.
///
/// Everything here runs against a block this file builds, because the game is not installed on
/// the machine this was written on — and because a fixture that agrees with the reader by
/// construction would prove nothing. <see cref="Block"/> therefore walks the documented struct
/// field by field, accumulating offsets from field <em>sizes</em>, rather than restating the
/// constants in <see cref="MumbleLink"/>. Get an offset wrong in the reader and these fail.
/// </summary>
public class MumbleLinkTests
{
    /// <summary>
    /// Mumble's LinkedMem with Guild Wars 2's MumbleContext in its context field, laid out from
    /// the wiki's struct definition. Sizes in bytes, in declaration order.
    /// </summary>
    private sealed class Block
    {
        private readonly byte[] _bytes = new byte[MumbleLink.Size];
        private int _at;

        private Block Skip(int bytes)
        {
            _at += bytes;
            return this;
        }

        private Block U32(uint value)
        {
            BitConverter.GetBytes(value).CopyTo(_bytes, _at);
            return Skip(4);
        }

        private Block F32(float value)
        {
            BitConverter.GetBytes(value).CopyTo(_bytes, _at);
            return Skip(4);
        }

        private Block U16(ushort value)
        {
            BitConverter.GetBytes(value).CopyTo(_bytes, _at);
            return Skip(2);
        }

        private Block U8(byte value)
        {
            _bytes[_at] = value;
            return Skip(1);
        }

        /// <summary>A fixed UTF-16 buffer holding a null-terminated string, then old rubbish.</summary>
        private Block Text(string value, int bufferBytes, string leftover = "")
        {
            var start = _at;
            var payload = Encoding.Unicode.GetBytes(value + "\0" + leftover);
            payload.AsSpan(0, Math.Min(payload.Length, bufferBytes)).CopyTo(_bytes.AsSpan(start));

            _at = start + bufferBytes;
            return this;
        }

        public static byte[] Build(
            uint tick = 42,
            string identity = """{"name":"Imothep","profession":4,"spec":55,"race":3,"map_id":1,"world_id":268435538,"commander":true}""",
            uint mapId = 1155,
            uint mapType = 5,
            uint uiState = 0,
            float playerX = 12.5f,
            float playerY = -34.25f,
            byte mount = 0,
            float frontX = 0f,
            float frontZ = 0f,
            string leftover = "")
        {
            var b = new Block();

            b.U32(1)                       // uiVersion
             .U32(tick)                    // uiTick
             .Skip(12)                     // fAvatarPosition
             .F32(frontX).F32(0).F32(frontZ) // fAvatarFront, the facing vector
             .Skip(12)                     // fAvatarTop
             .Text("Imothep", 512)         // name[256] wchar
             .Skip(12).Skip(12).Skip(12)   // fCameraPosition / Front / Top
             .Text(identity, 512, leftover) // identity[256] wchar
             .U32(48);                     // context_len

            var contextStart = b._at;

            b.Skip(28)                     // serverAddress[28]
             .U32(mapId)
             .U32(mapType)
             .U32(7)                       // shardId
             .U32(0)                       // instance
             .U32(160000)                  // buildId
             .U32(uiState)
             .U16(360).U16(360)            // compassWidth / Height
             .F32(0)                       // compassRotation
             .F32(playerX)
             .F32(playerY)
             .F32(0).F32(0)                // mapCenterX / Y
             .F32(1)                       // mapScale
             .U32(4242)                    // processId
             .U8(mount);

            // The context is a fixed 256-byte field whatever is written into it.
            b._at = contextStart + 256;
            b.Skip(4096);                  // description[2048] wchar

            Assert.Equal(MumbleLink.Size, b._at);
            return b._bytes;
        }
    }

    /// <summary>The block builder, for tests outside this class. Keeps <see cref="Block"/>
    /// private so its offsets stay one file's business.</summary>
    internal static byte[] BuildBlock(float frontX = 0f, float frontZ = 0f) =>
        Block.Build(frontX: frontX, frontZ: frontZ);

    [Fact]
    public void The_layout_is_the_size_the_struct_adds_up_to()
    {
        // 4 + 4 + 108 + 512 + 512 + 4 + 256 + 4096. If this is wrong every offset after it is.
        Assert.Equal(5460, MumbleLink.Size);
    }

    [Fact]
    public void Every_field_comes_back_off_the_block()
    {
        var state = MumbleLink.Decode(Block.Build(uiState: 8 | 64, mount: 5))!;

        Assert.Equal(42u, state.Tick);
        Assert.Equal("Imothep", state.CharacterName);
        Assert.Equal(4, state.ProfessionId);
        Assert.Equal("Ranger", state.Profession);
        Assert.Equal(55, state.SpecializationId);
        Assert.Equal("Norn", state.Race);
        Assert.True(state.IsCommander);
        Assert.Equal(268435538, state.WorldId);

        // From the context, not the identity — the two carry map_id and this is the live one.
        Assert.Equal(1155, state.MapId);
        Assert.Equal(5, state.MapType);
        Assert.Equal("Raptor", state.Mount);
        Assert.True(state.IsMounted);
        Assert.Equal(12.5f, state.PlayerX);
        Assert.Equal(-34.25f, state.PlayerY);
        Assert.Equal(4242u, state.ProcessId);
    }

    [Fact]
    public void The_ui_bitmask_decodes_to_the_two_bits_worth_having()
    {
        var idle = MumbleLink.Decode(Block.Build(uiState: 8))!;
        var fighting = MumbleLink.Decode(Block.Build(uiState: 8 | 64))!;
        var away = MumbleLink.Decode(Block.Build(uiState: 1))!;

        Assert.True(idle.HasFocus);
        Assert.False(idle.InCombat);

        Assert.True(fighting.HasFocus);
        Assert.True(fighting.InCombat);

        // Map open, game not focused: someone alt-tabbed away mid-look.
        Assert.False(away.HasFocus);
        Assert.True(away.Ui.HasFlag(UiState.MapOpen));
    }

    [Fact]
    public void A_tick_of_zero_is_absence_not_a_nameless_character()
    {
        // The mapping can exist without the game ever having written to it. Reading that as a
        // live character called "" is the failure this guards.
        Assert.Null(MumbleLink.Decode(Block.Build(tick: 0)));
    }

    [Fact]
    public void A_short_block_is_refused_rather_than_read_past()
    {
        // One byte short of the last field that gets read, with a tick that would otherwise
        // pass: a length check that only rejects blank blocks is not a length check.
        Assert.Null(MumbleLink.Decode(Block.Build().AsSpan(0, MumbleLink.GameBlockSize - 1)));
        Assert.Null(MumbleLink.Decode([]));
    }

    [Fact]
    public void The_block_ends_at_the_last_field_the_game_fills_in()
    {
        // 1108 + 84 + 1: up to and including the mount byte. The 4096-byte description after
        // it is Mumble's and the game never touches it, so waiting for it is waiting forever.
        Assert.Equal(1193, MumbleLink.GameBlockSize);
    }

    [Fact]
    public void What_jokolink_writes_on_linux_decodes()
    {
        // The shape of /dev/shm/MumbleLink: the 1193 bytes the game fills in, then the game's
        // X11 window id. Requiring the whole 5460-byte struct rejected this outright, which
        // meant the Linux path could not read the one file it exists to read.
        var jokolink = new byte[MumbleLink.LinuxFileSize];
        Block.Build().AsSpan(0, MumbleLink.GameBlockSize).CopyTo(jokolink);
        BitConverter.GetBytes(0x2000028L).CopyTo(jokolink, MumbleLink.GameBlockSize);

        var state = MumbleLink.Decode(jokolink)!;

        Assert.Equal("Imothep", state.CharacterName);
        Assert.Equal(1155, state.MapId);
        Assert.Equal(12.5f, state.PlayerX);
    }

    [Fact]
    public void Whatever_follows_the_terminator_is_not_part_of_the_identity()
    {
        // The identity buffer is reused frame to frame, so a shorter name leaves the tail of a
        // longer one behind it. Trimming would not help — it is not whitespace, it is JSON.
        var state = MumbleLink.Decode(Block.Build(leftover: "\",\"junk\":true}"))!;

        Assert.Equal("Imothep", state.CharacterName);
    }

    [Fact]
    public void A_frame_caught_mid_write_reads_as_nothing_rather_than_throwing()
    {
        Assert.Null(MumbleLink.Decode(Block.Build(identity: "{\"name\":\"Imo")));
        Assert.Null(MumbleLink.Decode(Block.Build(identity: string.Empty)));
    }

    [Fact]
    public void An_id_outside_the_table_reads_as_unknown()
    {
        // A new mount ships with every expansion, and this table will be behind one day.
        var future = MumbleLink.Decode(Block.Build(
            identity: """{"name":"X","profession":99,"spec":0,"race":42,"map_id":1,"world_id":1,"commander":false}""",
            mount: 200))!;

        Assert.Equal("Unknown", future.Profession);
        Assert.Equal("Unknown", future.Race);
        Assert.Equal("Unknown", future.Mount);
    }

    // --- is anyone actually playing ----------------------------------------

    private static readonly DateTimeOffset Noon = new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_moving_tick_is_a_running_game()
    {
        var live = new Liveness().Advance(100, Noon);

        Assert.True(live.IsLiveAt(Noon));
        Assert.True(live.IsLiveAt(Noon.AddSeconds(5)));
    }

    [Fact]
    public void A_tick_that_stops_moving_is_a_closed_game()
    {
        // The whole point: MumbleLink keeps its last values after the game exits, so the
        // character who logged out last night is still sitting there, apparently in combat.
        var live = new Liveness().Advance(100, Noon);
        var stalled = live.Advance(100, Noon.AddSeconds(30));

        Assert.Equal(Noon, stalled.ChangedAt);
        Assert.False(stalled.IsLiveAt(Noon.AddSeconds(30)));
    }

    [Fact]
    public void A_brief_stall_is_not_a_closed_game()
    {
        // Loading screens and alt-tabs stall the tick for a moment; flickering "closed" at
        // someone who is very much still playing would be worse than a slightly slow answer.
        var live = new Liveness().Advance(100, Noon);

        Assert.True(live.IsLiveAt(Noon.AddSeconds(4)));
    }

    [Fact]
    public void Nothing_seen_yet_is_never_live()
    {
        Assert.False(new Liveness().IsLiveAt(Noon));
    }
}

/// <summary>
/// The facing vector, and the bearing the compass screen draws from it.
///
/// Worth its own class because it is the one field added for the compass and the one whose sign
/// convention is easy to get plausibly wrong. These pin the convention down so that a later
/// change to <see cref="GameState.HeadingRadians"/> has to be deliberate.
///
/// The convention they pin down is the measured one: north is +Z, established from a compass
/// screenshot whose marks were matched against the map's own coordinates. The earlier guess of
/// -Z mirrored every heading about the east-west axis, and these tests passed under it — a test
/// can only hold a convention still, not tell you which one Tyria uses.
/// </summary>
public class HeadingTests
{
    private static GameState Facing(float frontX, float frontZ) =>
        MumbleLink.Decode(MumbleLinkTests.BuildBlock(frontX: frontX, frontZ: frontZ))!;

    [Fact]
    public void The_facing_vector_comes_off_the_block()
    {
        var state = Facing(0.6f, -0.8f);

        Assert.Equal(0.6f, state.FrontX, 3);
        Assert.Equal(-0.8f, state.FrontZ, 3);
    }

    /// <summary>
    /// Map north is +Z, so a character facing it has a bearing of zero.
    /// </summary>
    [Fact]
    public void Facing_north_is_zero()
    {
        Assert.Equal(0, Facing(0f, 1f).HeadingRadians, 4);
    }

    /// <summary>
    /// East is +X and a quarter turn clockwise from north, which is where the sign of the
    /// bearing is decided. Get this one backwards and the needle mirrors.
    /// </summary>
    [Fact]
    public void Facing_east_is_a_quarter_turn_clockwise()
    {
        Assert.Equal(Math.PI / 2, Facing(1f, 0f).HeadingRadians, 4);
    }

    [Fact]
    public void Facing_south_is_half_a_turn()
    {
        Assert.Equal(Math.PI, Math.Abs(Facing(0f, -1f).HeadingRadians), 4);
    }

    [Fact]
    public void Facing_west_is_a_quarter_turn_the_other_way()
    {
        Assert.Equal(-Math.PI / 2, Facing(-1f, 0f).HeadingRadians, 4);
    }

    /// <summary>
    /// A bridge older than this field sends nothing, which deserialises to a zero vector. That
    /// is not a direction and must not read as north, or a stale PC would point the needle up
    /// and look confident about it.
    /// </summary>
    [Fact]
    public void A_zero_vector_is_not_a_heading()
    {
        Assert.False(Facing(0f, 0f).HasHeading);
        Assert.True(Facing(0f, 1f).HasHeading);
    }
}

/// <summary>Where the compass hub sits relative to the bridge proper.</summary>
public class HubPortTests
{
    [Fact]
    public void The_hub_is_two_past_the_bridge()
    {
        // Two, so request/discovery/stream read as one block in a firewall dialog: the UDP
        // beacon already owns the port in between.
        Assert.Equal(BridgeProtocol.DiscoveryPort + 1, BridgeProtocol.HubPortFor(BridgeProtocol.DefaultPort));
        Assert.Equal(47625, BridgeProtocol.HubPortFor(BridgeProtocol.DefaultPort));
    }

    [Fact]
    public void It_follows_a_bridge_moved_to_another_port()
    {
        Assert.Equal(9002, BridgeProtocol.HubPortFor(9000));
    }
}
