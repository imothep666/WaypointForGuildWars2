using System.IO.MemoryMappedFiles;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;

using GuildWarsCompanion.Bridge.Protocol;

// Windows only, because the game's MumbleLink is a named shared memory block and .NET has named
// maps only on Windows. On Linux the bridge reads a file a helper copies out of the Wine prefix.
[assembly: SupportedOSPlatform("windows")]

namespace GuildWarsCompanion.MumbleSim;

/// <summary>
/// Writes the MumbleLink block Guild Wars 2 would write, so the rest of the stack can be built
/// without the game running.
///
/// Everything downstream — the bridge's reader, the SignalR stream, the compass screen — treats
/// this exactly as it treats the real thing, because it is the same mapping, the same layout and
/// the same offsets, compiled from the same file.
///
/// What it deliberately does NOT settle: whether the real game's axis conventions match what
/// <see cref="GameState.HeadingRadians"/> assumes. A simulator written from the same assumption
/// as the reader will agree with it and prove nothing. That check needs Tyria.
/// </summary>
public static class Program
{
    /// <summary>
    /// Sixty a second, like the game.
    ///
    /// The bridge only polls ten times a second, so this is faster than anything needs — but the
    /// tick rate is what <see cref="Liveness"/> uses to decide the game is running, and a
    /// simulator that ticked slowly would be exercising a different code path from the real one.
    /// </summary>
    private static readonly TimeSpan Frame = TimeSpan.FromMilliseconds(16);

    public static async Task<int> Main(string[] args)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("MumbleLink is a Windows memory mapping; this only runs there.");
            return 1;
        }

        var walk = Walk.Parse(args);

        Console.WriteLine($"Simulating {walk.Character} the {walk.ProfessionName} in map {walk.MapId}.");
        Console.WriteLine($"  Walking a {walk.Radius:0} unit circle about ({walk.CentreX:0}, {walk.CentreY:0})");
        Console.WriteLine($"  one lap every {walk.LapSeconds:0} seconds.");
        Console.WriteLine();
        Console.WriteLine("Start the bridge and it will read this as a live game. Ctrl+C to stop.");
        Console.WriteLine();

        using var quitting = new CancellationTokenSource();

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            quitting.Cancel();
        };

        try
        {
            await RunAsync(walk, quitting.Token);
        }
        catch (OperationCanceledException)
        {
            // Ctrl+C. The mapping goes when this process does.
        }

        Console.WriteLine("Stopped. The block stops advancing, so the bridge will call the game closed.");

        return 0;
    }

    /// <summary>
    /// A bearing as a number a person can read: degrees in [0, 360).
    ///
    /// The raw angle accumulates a full turn per lap, so after ten minutes the console was
    /// reporting a heading of 5772 degrees. The block was always right — it is written through
    /// sin and cos, which do not care — but a log line that looks broken makes everything read
    /// from it suspect.
    /// </summary>
    private static double Compass(double radians)
    {
        var degrees = radians * 180 / Math.PI % 360;

        return degrees < 0 ? degrees + 360 : degrees;
    }

    private static async Task RunAsync(Walk walk, CancellationToken ct)
    {
        // CreateOrOpen, for the same reason MumbleSource does it: whichever of the two starts
        // first brings the mapping into being and the other joins it.
        using var file = MemoryMappedFile.CreateOrOpen(MumbleLink.MapName, MumbleLink.Size);
        using var view = file.CreateViewAccessor(0, MumbleLink.Size);

        var block = new byte[MumbleLink.Size];
        var identity = Encoding.Unicode.GetBytes(walk.Identity() + "\0");

        using var timer = new PeriodicTimer(Frame);

        var started = DateTimeOffset.UtcNow;
        uint tick = 0;
        var reported = 0;

        while (await timer.WaitForNextTickAsync(ct))
        {
            var elapsed = (DateTimeOffset.UtcNow - started).TotalSeconds;

            walk.Write(block, identity, ++tick, elapsed);

            view.WriteArray(0, block, 0, block.Length);

            // One line a second, so the console shows it is alive without becoming a firehose.
            if ((int)elapsed > reported)
            {
                reported = (int)elapsed;

                var (x, y, bearing) = walk.At(elapsed);

                Console.WriteLine(
                    $"  tick {tick,7}  ({x,8:0.0}, {y,8:0.0})  " +
                    $"facing {Compass(bearing),5:0.0}°");
            }
        }
    }
}

/// <summary>
/// A character walking a circle, and everything needed to write them into the block.
/// </summary>
/// <param name="CentreX">Continent coordinates, the same space the map API publishes and the
/// same space the context's playerX/playerY use.</param>
public sealed record Walk(
    string Character, int ProfessionId, int RaceId, int MapId,
    float CentreX, float CentreY, float Radius, double LapSeconds, byte Mount)
{
    /// <summary>
    /// Queensdale, about the middle of its continent_rect [[42624, 28032], [46208, 30464]].
    ///
    /// A real map with real landmarks around the character rather than an invented one, so the
    /// compass screen has genuine hearts and waypoints to point at.
    /// </summary>
    private const int DefaultMap = 15;

    private const float DefaultCentreX = 44416f;
    private const float DefaultCentreY = 29248f;

    public string ProfessionName =>
        new GameState(1, Character, ProfessionId, 0, RaceId, false, MapId, 1, 1, Mount,
            UiState.GameHasFocus, 0, 0).Profession;

    /// <summary>
    /// Reads the command line, with every field optional.
    ///
    /// Positional rather than flagged because there are five of them and this is a tool one
    /// person runs from a terminal they are already in.
    /// </summary>
    public static Walk Parse(string[] args)
    {
        string Arg(int i, string fallback) => args.Length > i ? args[i] : fallback;

        float Number(int i, float fallback) =>
            args.Length > i && float.TryParse(args[i], out var value) ? value : fallback;

        return new Walk(
            Arg(0, "Imothep"),
            // Ranger. One-based, per the wiki: professions[0] is Guardian.
            4,
            3,
            (int)Number(2, DefaultMap),
            Number(3, DefaultCentreX),
            Number(4, DefaultCentreY),
            Number(5, 400f),
            Number(6, 60f),
            0);
    }

    /// <summary>The JSON Guild Wars 2 puts in the identity field, field for field.</summary>
    public string Identity() =>
        JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["name"] = Character,
            ["profession"] = ProfessionId,
            ["spec"] = 0,
            ["race"] = RaceId,
            ["map_id"] = MapId,
            ["world_id"] = 268435538L,
            ["commander"] = false
        });

    /// <summary>
    /// Where the character is and which way they face, at a moment.
    ///
    /// A Lissajous figure rather than a circle, and the two frequencies are in the golden ratio
    /// so the path does not close on itself for a very long time. A circle was the obvious first
    /// choice and turned out to be useless for judging the compass's trail: the character retraced
    /// its own steps every lap, so the tail lay on top of itself and read as a fan of spokes
    /// whether the drawing was right or wrong.
    ///
    /// Facing is the direction of travel, taken from the derivative rather than assumed. On a
    /// circle "a quarter turn ahead of the radius" happened to be correct; on any other path it
    /// is not, and a simulator whose heading disagrees with its own movement would send somebody
    /// hunting for a bug in the reader.
    /// </summary>
    public (float X, float Y, double Bearing) At(double seconds)
    {
        var t = seconds / LapSeconds * Math.Tau;

        // 1.618: irrational enough that the figure keeps finding new ground for hours.
        const double Ratio = 1.6180339887;

        var x = CentreX + Radius * Math.Cos(t);
        var y = CentreY + Radius * Math.Sin(t * Ratio);

        // Velocity, for the heading. Map coordinates grow east and south, so north is -Y.
        var dx = -Radius * Math.Sin(t);
        var dy = Radius * Ratio * Math.Cos(t * Ratio);

        return ((float)x, (float)y, Math.Atan2(dx, -dy));
    }

    /// <summary>
    /// Lays the block out by walking the documented struct, field by field.
    ///
    /// Offsets are accumulated from field sizes rather than restated from
    /// <see cref="MumbleLink"/>'s constants, exactly as the test fixture does, so that a wrong
    /// offset in the reader shows up as a wrong reading rather than being written to match.
    /// </summary>
    public void Write(byte[] block, byte[] identity, uint tick, double seconds)
    {
        Array.Clear(block);

        var (x, y, bearing) = At(seconds);

        // The facing vector, in the game's metres-and-Y-up world: X east, Z north. A bearing of
        // zero is map north, which is +Z — see GameState.HeadingRadians for how that sign was
        // settled, and why the simulator cannot settle it.
        var frontX = (float)Math.Sin(bearing);
        var frontZ = (float)Math.Cos(bearing);

        var at = 0;

        void U32(uint value)
        {
            BitConverter.GetBytes(value).CopyTo(block, at);
            at += 4;
        }

        void F32(float value)
        {
            BitConverter.GetBytes(value).CopyTo(block, at);
            at += 4;
        }

        void U16(ushort value)
        {
            BitConverter.GetBytes(value).CopyTo(block, at);
            at += 2;
        }

        void Skip(int bytes) => at += bytes;

        U32(2);                                  // uiVersion
        U32(tick);                               // uiTick

        Skip(12);                                // fAvatarPosition
        F32(frontX); F32(0); F32(frontZ);        // fAvatarFront
        Skip(12);                                // fAvatarTop

        Skip(512);                               // name[256] wchar, unused by the reader

        Skip(12); Skip(12); Skip(12);            // fCameraPosition / Front / Top

        identity.AsSpan(0, Math.Min(identity.Length, 512)).CopyTo(block.AsSpan(at));
        Skip(512);                               // identity[256] wchar

        U32(48);                                 // context_len

        var contextStart = at;

        Skip(28);                                // serverAddress[28]
        U32((uint)MapId);
        U32(1);                                  // mapType: a public map
        U32(7);                                  // shardId
        U32(0);                                  // instance
        U32(160000);                             // buildId
        U32((uint)(UiState.GameHasFocus | UiState.CompassTopRight));
        U16(360); U16(360);                      // compassWidth / Height
        F32(0);                                  // compassRotation, the minimap's, not the player's
        F32(x);                                  // playerX, continent coordinates
        F32(y);                                  // playerY
        F32(CentreX); F32(CentreY);              // mapCenterX / Y
        F32(1);                                  // mapScale
        U32((uint)Environment.ProcessId);        // processId: this stands in for the game
        block[at] = Mount;                       // mountIndex

        at = contextStart + 256;
        Skip(4096);                              // description[2048] wchar

        if (at != MumbleLink.Size)
            throw new InvalidOperationException($"Wrote {at} bytes, the block is {MumbleLink.Size}.");
    }
}
