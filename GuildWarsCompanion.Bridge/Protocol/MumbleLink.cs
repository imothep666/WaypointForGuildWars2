using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GuildWarsCompanion.Bridge.Protocol;

/// <summary>The JSON Guild Wars 2 writes into the identity field, every frame.</summary>
internal sealed record MumbleIdentity(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("profession")] int Profession,
    [property: JsonPropertyName("spec")] int Spec,
    [property: JsonPropertyName("race")] int Race,
    [property: JsonPropertyName("map_id")] int MapId,
    [property: JsonPropertyName("world_id")] long WorldId,
    [property: JsonPropertyName("commander")] bool Commander);

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(MumbleIdentity))]
[JsonSerializable(typeof(GameReport))]
internal partial class GameJson : JsonSerializerContext;

/// <summary>
/// Reading the block Guild Wars 2 publishes for Mumble.
///
/// This is not a hook and not an injection: the game writes a shared memory block every frame
/// specifically so that third-party tools can read it, which is how overlays like TacO and
/// BlishHUD work. Nothing here writes to the game or sends it anything.
///
/// The layout is Mumble's own <c>LinkedMem</c> with Guild Wars 2's <c>MumbleContext</c> packed
/// into its 256-byte context field, both taken from the wiki's API:MumbleLink page. Offsets are
/// written out rather than expressed as a struct because a managed struct's padding is a
/// negotiation with the runtime and this is a fixed wire format.
/// </summary>
public static class MumbleLink
{
    /// <summary>The name of the mapping, and of the file jokolink copies it to on Linux.</summary>
    public const string MapName = "MumbleLink";

    /// <summary>uiVersion(4) + uiTick(4) + 9 vec3(108) + name(512) + identity(512) +
    /// context_len(4) + context(256) + description(4096).</summary>
    public const int Size = 5460;

    /// <summary>
    /// How much of the block is worth having: everything up to and including the mount byte,
    /// which is the last field Guild Wars 2 fills in.
    ///
    /// The tail is Mumble's 4096-byte description, which the game leaves empty, so requiring
    /// the full <see cref="Size"/> before decoding rejects a perfectly good block. That is not
    /// hypothetical on Linux: jokolink copies out exactly these 1193 bytes and then appends the
    /// game's X11 window id, so its file is nowhere near 5460 bytes and never will be.
    /// </summary>
    public const int GameBlockSize = MountAt + 1;

    private const int TickAt = 4;

    /// <summary>fAvatarPosition, three floats of metres. Declared for the map below rather than
    /// read: the context's playerX/playerY are already in the coordinate space the compass
    /// works in, so the metric position would only have to be converted back.</summary>
    private const int AvatarAt = 8;

    /// <summary>
    /// fAvatarFront, the three floats immediately after the position, and the only place the
    /// block says which way the character is facing.
    ///
    /// Not compassRotation (context+56), which is the rotation of the minimap and is only the
    /// character's heading while <see cref="UiState.CompassRotating"/> is set. A heading that
    /// silently means something else whenever the player pins their minimap is worse than no
    /// heading.
    /// </summary>
    private const int AvatarFrontAt = 20;

    private const int IdentityAt = 592;
    private const int ContextAt = 1108;

    // Within the context block.
    private const int MapIdAt = ContextAt + 28;
    private const int MapTypeAt = ContextAt + 32;
    private const int UiStateAt = ContextAt + 48;
    private const int PlayerXAt = ContextAt + 60;
    private const int PlayerYAt = ContextAt + 64;
    private const int ProcessIdAt = ContextAt + 80;
    private const int MountAt = ContextAt + 84;

    /// <summary>
    /// Turns a raw block into a state, or null if it holds nothing yet.
    ///
    /// A tick of zero means the game has never written here — the mapping exists because
    /// something created it, which on Windows may well be this very reader. That is not the game
    /// being idle; it is the game being absent, and it must not read as a character called "".
    /// </summary>
    public static GameState? Decode(ReadOnlySpan<byte> block)
    {
        if (block.Length < GameBlockSize) return null;

        var tick = BinaryPrimitives.ReadUInt32LittleEndian(block[TickAt..]);
        if (tick == 0) return null;

        var identity = ReadIdentity(block.Slice(IdentityAt, 512));
        if (identity is null) return null;

        return new GameState(
            tick,
            identity.Name ?? string.Empty,
            identity.Profession,
            identity.Spec,
            identity.Race,
            identity.Commander,
            // The identity carries map_id too, but the context is the one the game updates for
            // map transitions; they agree in practice and this is the authoritative half.
            (int)BinaryPrimitives.ReadUInt32LittleEndian(block[MapIdAt..]),
            identity.WorldId,
            (int)BinaryPrimitives.ReadUInt32LittleEndian(block[MapTypeAt..]),
            block[MountAt],
            (UiState)BinaryPrimitives.ReadUInt32LittleEndian(block[UiStateAt..]),
            BinaryPrimitives.ReadSingleLittleEndian(block[PlayerXAt..]),
            BinaryPrimitives.ReadSingleLittleEndian(block[PlayerYAt..]),
            // X and Z only. Y is the vertical component, which a compass drawn flat on a phone
            // has no use for, and including it would invite somebody to put it in the bearing.
            BinaryPrimitives.ReadSingleLittleEndian(block[AvatarFrontAt..]),
            BinaryPrimitives.ReadSingleLittleEndian(block[(AvatarFrontAt + 8)..]),
            BinaryPrimitives.ReadUInt32LittleEndian(block[ProcessIdAt..]));
    }

    /// <summary>
    /// The identity is a fixed 512-byte UTF-16 buffer holding a null-terminated JSON string.
    /// Everything after the terminator is whatever was there before, so it has to be cut off
    /// rather than trimmed — trailing garbage is not whitespace.
    /// </summary>
    private static MumbleIdentity? ReadIdentity(ReadOnlySpan<byte> utf16)
    {
        var text = Encoding.Unicode.GetString(utf16);

        var end = text.IndexOf('\0');
        if (end >= 0) text = text[..end];

        if (text.Length == 0) return null;

        try
        {
            return JsonSerializer.Deserialize(text, GameJson.Default.MumbleIdentity);
        }
        catch (JsonException)
        {
            // A frame caught mid-write. The next read will be along in a moment.
            return null;
        }
    }

    /// <summary>Where a Linux build looks: the file jokolink copies the mapping into.</summary>
    public static string LinuxPath => $"/dev/shm/{MapName}";

    /// <summary>
    /// What <see cref="LinuxPath"/> looks like when the helper is running: the block, plus the
    /// 8-byte X11 window id jokolink puts after it. Only used to tell somebody what to expect
    /// to see — nothing here depends on the id being there, or on the size being exactly this.
    /// </summary>
    public const int LinuxFileSize = GameBlockSize + 8;
}
