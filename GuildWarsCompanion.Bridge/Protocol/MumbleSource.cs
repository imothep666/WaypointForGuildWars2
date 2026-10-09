using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace GuildWarsCompanion.Bridge.Protocol;

/// <summary>
/// Gets the raw MumbleLink block off this machine, however this machine keeps it.
///
/// Two very different situations behind one interface:
///
/// <b>Windows</b> — a named shared memory mapping, created here if it does not exist yet,
/// mapped, copied out. The reader creating it looks wrong and is not: Guild Wars 2 writes into
/// the mapping if it is there, but does not bring it into being, so a bridge that only ever
/// opened it would wait forever next to a running game. Measured on this machine: with the
/// mapping absent nothing appeared for minutes while a character stood in a map, and within
/// four seconds of another process creating it the tick was climbing sixty a second. It is why
/// every other tool that reads MumbleLink — BlishHUD, TacO, jokolink — creates it too.
///
/// <b>Linux</b> — Guild Wars 2 has no native client, so it runs under Proton and the mapping
/// lives inside the Wine prefix where a native process cannot reach it. The established answer
/// is a small Windows-side helper running in the same prefix that copies the block out to
/// /dev/shm, where this can read it as an ordinary file; jokolink is the reference
/// implementation. So the Linux path here is trivial and the Linux <em>setup</em> is not, which
/// is the opposite of how it looks.
/// </summary>
public sealed class MumbleSource : IDisposable
{
    private MemoryMappedAccess? _windows;
    private readonly byte[] _buffer = new byte[MumbleLink.Size];

    /// <summary>Why there is nothing to read, when there is nothing to read.</summary>
    public string? Problem { get; private set; }

    /// <summary>
    /// The current block, or null if it cannot be reached. Cheap enough to call on a timer.
    /// </summary>
    public ReadOnlySpan<byte> Read()
    {
        try
        {
            if (OperatingSystem.IsWindows()) return ReadWindows();
            if (OperatingSystem.IsLinux()) return ReadLinux();

            Problem = "MumbleLink is only readable on Windows and Linux.";
            return default;
        }
        catch (Exception ex)
        {
            Problem = ex.Message;
            return default;
        }
    }

    [SupportedOSPlatform("windows")]
    private ReadOnlySpan<byte> ReadWindows()
    {
        // Created rather than only opened, because the game will not create it. The empty block
        // that makes is the price, and it is paid elsewhere: a zero tick is absence to
        // MumbleLink.Decode, and a tick that stops moving is a closed game to Liveness. Neither
        // reads as somebody playing, which is the only thing that would have made this unsafe.
        _windows ??= MemoryMappedAccess.CreateOrOpen(MumbleLink.MapName);

        if (_windows is null)
        {
            Problem = "Could not open the MumbleLink shared memory on this PC.";
            return default;
        }

        if (!_windows.Copy(_buffer))
        {
            // Only reachable if the view went out from under us — this holds the mapping open
            // for as long as the bridge runs, so the game closing does not take it away. Drop
            // it and make a new one next time rather than reading from nothing.
            _windows.Dispose();
            _windows = null;
            Problem = "Lost the MumbleLink mapping; reopening it.";
            return default;
        }

        Problem = null;
        return _buffer;
    }

    [SupportedOSPlatform("linux")]
    private ReadOnlySpan<byte> ReadLinux()
    {
        var path = MumbleLink.LinuxPath;

        if (!File.Exists(path))
        {
            Problem = $"No {path}. Guild Wars 2 runs under Proton, so a helper inside the Wine " +
                      "prefix has to copy MumbleLink out — see \"Set up MumbleLink\".";
            return default;
        }

        using var file = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        // GameBlockSize, not Size: the helper copies out what the game fills in and stops,
        // so this file is about 1200 bytes rather than 5460. Asking for the whole LinkedMem
        // here meant every real jokolink file was rejected as "too short" — the Linux path
        // could only ever have worked against a file no tool produces.
        var read = file.ReadAtLeast(_buffer, MumbleLink.GameBlockSize, throwOnEndOfStream: false);
        if (read < MumbleLink.GameBlockSize)
        {
            Problem = $"{path} is {read} bytes; a MumbleLink block is " +
                      $"{MumbleLink.GameBlockSize}. Is the helper writing it?";
            return default;
        }

        Problem = null;

        // Only what was actually read. The rest of the buffer is the previous poll, and the
        // whole point of the length check in MumbleLink.Decode is that it means something.
        return _buffer.AsSpan(0, read);
    }

    public void Dispose()
    {
        // Only Windows ever opens the mapping; on Linux there is nothing to let go of.
        if (!OperatingSystem.IsWindows()) return;

        _windows?.Dispose();
        _windows = null;
    }
}

/// <summary>The Windows half: CreateFileMapping and MapViewOfFile, and nothing else.</summary>
[SupportedOSPlatform("windows")]
internal sealed class MemoryMappedAccess : IDisposable
{
    private const uint FileMapRead = 0x0004;
    private const uint PageReadWrite = 0x0004;

    /// <summary>Backed by the paging file rather than a file on disk, which is what a mapping
    /// with no file behind it means.</summary>
    private static readonly IntPtr Pagefile = new(-1);

    private IntPtr _handle;
    private IntPtr _view;

    private MemoryMappedAccess(IntPtr handle, IntPtr view)
    {
        _handle = handle;
        _view = view;
    }

    /// <summary>
    /// The mapping, whoever made it. Creating a named mapping that already exists returns the
    /// existing one at its existing size, so this is the same call whether the game has been
    /// running for an hour or has not started yet.
    /// </summary>
    public static MemoryMappedAccess? CreateOrOpen(string name)
    {
        var handle = CreateFileMapping(Pagefile, IntPtr.Zero, PageReadWrite, 0, MumbleLink.Size, name);

        // Another reader got there first and left one this process may not write to. Reading is
        // all that is wanted anyway, so take it on those terms rather than giving up.
        if (handle == IntPtr.Zero) handle = OpenFileMapping(FileMapRead, false, name);

        if (handle == IntPtr.Zero) return null;

        var view = MapViewOfFile(handle, FileMapRead, 0, 0, (UIntPtr)MumbleLink.Size);
        if (view == IntPtr.Zero)
        {
            CloseHandle(handle);
            return null;
        }

        return new MemoryMappedAccess(handle, view);
    }

    public bool Copy(byte[] into)
    {
        if (_view == IntPtr.Zero) return false;

        Marshal.Copy(_view, into, 0, into.Length);
        return true;
    }

    public void Dispose()
    {
        if (_view != IntPtr.Zero) UnmapViewOfFile(_view);
        if (_handle != IntPtr.Zero) CloseHandle(_handle);

        _view = IntPtr.Zero;
        _handle = IntPtr.Zero;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFileMapping(
        IntPtr file, IntPtr attributes, uint protect, uint sizeHigh, uint sizeLow, string name);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenFileMapping(uint access, bool inheritHandle, string name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr MapViewOfFile(IntPtr map, uint access, uint offsetHigh, uint offsetLow, UIntPtr bytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool UnmapViewOfFile(IntPtr view);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
