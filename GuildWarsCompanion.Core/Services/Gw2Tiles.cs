using GuildWarsCompanion.Host;

namespace GuildWarsCompanion.Services;

/// <summary>Which tile: continent, floor, zoom and grid position.</summary>
public readonly record struct TileKey(int Continent, int Floor, int Zoom, int X, int Y)
{
    public string Path => $"{Continent}/{Floor}/{Zoom}/{X}/{Y}.jpg";

    /// <summary>A file name that cannot collide with another tile's, for a flat cache folder.</summary>
    public string FileName => $"{Continent}-{Floor}-{Zoom}-{X}-{Y}.jpg";
}

/// <summary>
/// The game's own map, one 256-pixel square at a time.
///
/// <c>tiles.guildwars2.com</c> serves the same artwork the in-game map is drawn from. It needs no
/// key and carries nothing about the account, which is what makes it usable here on the same
/// footing as <see cref="MapIcons"/> — and like those icons it is the game's own art rather than
/// something invented, so a player recognises the map they are standing in without learning
/// anything.
///
/// <b>Two caches, for two different reasons.</b> Disk, because a tile is immutable — the same
/// square of Queensdale for the life of the build — so a second fetch could only repeat the
/// first, and because a phone whose Wi-Fi reaches a PC hotspot and nothing else should still draw
/// a map it has seen. Memory, because decoding a JPEG per frame would be absurd.
///
/// <b>The memory cache is bounded, and that is the whole design.</b> A tile is seven kilobytes on
/// the wire and a quarter of a megabyte decoded, and a phone screen holds around sixty of them at
/// once. The compass has already killed this app with per-frame image work — <c>Scudo ERROR:
/// internal map failure (error desc=Out of memory)</c> — so this never grows past
/// <see cref="MostHeld"/>.
///
/// <b>Least recently used, not "not on screen this frame", and the difference is the whole feel of
/// a pan.</b> The first version dropped everything the last frame had not drawn. It was correct
/// and it was horrible: panning a tile's width threw away the row that had just left the screen,
/// so panning back re-decoded it, and the map visibly tore itself down and rebuilt at the edges.
/// Now the screen only says which tiles it used, that stamps them, and something is evicted when
/// there is no room for a new one. Nothing is thrown away while there is space to keep it.
///
/// <b>A fetch that fails for want of a network is asked again by itself.</b> It used to be asked
/// again only on the next redraw, and on a map nobody was touching there was no next redraw: a
/// square that failed on a bad connection stayed a hole until a pan or a zoom. Now the tile is
/// let go after <see cref="RetryAfter"/>, and <see cref="Redraw"/> asks the screen for a frame,
/// so only a tile the screen still shows is fetched again.
/// </summary>
public sealed class Gw2Tiles
{
    private const string Host = "https://tiles.guildwars2.com";

    /// <summary>
    /// A ceiling the screen cannot talk this class out of.
    ///
    /// Ninety-six tiles is about 24 MB decoded, which is survivable; six hundred is not. It is
    /// also comfortably more than a screenful and its ring — about seventy at the deepest zoom on
    /// this handset — which is the number that matters: a cache smaller than what one frame asks
    /// for evicts tiles it is about to be asked for again, and thrashes.
    /// </summary>
    private const int MostHeld = 96;

    /// <summary>
    /// Guards every collection below. Downloads finish on whichever pool thread they finish on,
    /// several at once, while the render thread reads; plain dictionaries do not survive that.
    /// </summary>
    private readonly Lock _gate = new();

    private readonly Dictionary<TileKey, IImage> _held = [];

    /// <summary>When each held tile was last drawn, as a counter rather than a clock: the only
    /// question ever asked of it is which of two is older.</summary>
    private readonly Dictionary<TileKey, long> _used = [];

    private long _tick;

    /// <summary>Tiles already asked for, so a miss is not requested again on every frame.</summary>
    private readonly HashSet<TileKey> _asked = [];

    /// <summary>
    /// Tiles the service does not have, remembered for the run.
    ///
    /// Not every index inside the grid is rendered — zoom 8 over core Tyria answers
    /// <c>NoSuchKey</c> — and without this the screen would re-request the same missing square
    /// every time it was panned back into view.
    /// </summary>
    private readonly HashSet<TileKey> _missing = [];

    /// <summary>How often each tile has failed in a row, for <see cref="RetryAfter"/>.</summary>
    private readonly Dictionary<TileKey, int> _failures = [];

    private readonly IImageDecoder _decoder;

    private readonly AppEnvironment _environment;

    private readonly HttpClient _http;

    /// <summary>The pause before a failed tile is let go: Task.Delay, or none in a test.</summary>
    private readonly Func<TimeSpan, Task> _wait;

    private string? _folder;

    public Gw2Tiles(IImageDecoder decoder, AppEnvironment environment)
        : this(decoder, environment, new HttpClientHandler(), Task.Delay)
    {
    }

    /// <summary>For the tests: a handler that answers without a network, and a wait that does
    /// not wait.</summary>
    internal Gw2Tiles(IImageDecoder decoder, AppEnvironment environment, HttpMessageHandler handler,
        Func<TimeSpan, Task> wait)
    {
        _decoder = decoder;
        _environment = environment;
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        _wait = wait;
    }

    /// <summary>
    /// Raised when the map is worth drawing again: a tile has arrived, or one that failed may be
    /// asked for again.
    /// </summary>
    public event Action? Redraw;

    /// <summary>
    /// How long a tile that failed waits before it may be asked for again: two seconds, doubling
    /// with each failure in a row, never more than a minute.
    ///
    /// Not sooner, because a phone that has lost its network fails every tile on the screen at
    /// once, and sixty requests a second into nothing would only drain the battery. Not much
    /// later, because a connection that comes back should fill the holes while the player is
    /// still looking at them.
    /// </summary>
    internal static TimeSpan RetryAfter(int failures) =>
        TimeSpan.FromSeconds(Math.Min(60, 2 << (Math.Clamp(failures, 1, 6) - 1)));

    /// <summary>
    /// A tile if it is in hand, null otherwise. Never blocks and never throws.
    ///
    /// Null is an ordinary drawing state: the map draws the background it already has under the
    /// gap. Starting the fetch is <see cref="Want"/>'s job, so a draw pass stays a draw pass.
    /// </summary>
    public IImage? Held(TileKey key)
    {
        lock (_gate) return _held.GetValueOrDefault(key);
    }

    /// <summary>
    /// Asks for a tile, at most once.
    ///
    /// Fire and forget on purpose. The caller is a draw pass; it cannot await, and a tile that
    /// arrives two hundred milliseconds late is a square that fades in rather than a stall.
    /// </summary>
    public void Want(TileKey key)
    {
        lock (_gate)
            if (_held.ContainsKey(key) || _missing.Contains(key) || !_asked.Add(key)) return;

        _ = FetchAsync(key);
    }

    /// <summary>
    /// Marks the tiles a frame actually drew, so the ones nobody has looked at for longest are
    /// the ones that go when room is needed.
    ///
    /// Called once a frame. Nothing is dropped here: a tile just off the edge of the screen is the
    /// tile the next flick of a finger needs, and letting go of it the instant it scrolls out is
    /// what made the map tear at its edges while it was being panned.
    /// </summary>
    public void Keep(IReadOnlySet<TileKey> visible)
    {
        lock (_gate)
        {
            var now = ++_tick;

            foreach (var key in visible)
                if (_held.ContainsKey(key))
                    _used[key] = now;
        }
    }

    /// <summary>
    /// Makes room for one more, by dropping the tile that has gone longest without being drawn.
    ///
    /// The ceiling is <see cref="MostHeld"/> and it is a hard one — this is the only thing between
    /// a long pan across a continent and an out-of-memory death. Called holding the gate.
    /// </summary>
    private void MakeRoom()
    {
        while (_held.Count >= MostHeld)
        {
            var oldest = default(TileKey);
            var since = long.MaxValue;

            foreach (var (key, used) in _used)
            {
                if (used >= since) continue;

                since = used;
                oldest = key;
            }

            // Held but never drawn — it arrived after the frame that asked for it. Nothing to
            // choose between, so let the whole lot go rather than spin.
            if (since == long.MaxValue)
            {
                _held.Clear();
                _used.Clear();
                _asked.Clear();

                return;
            }

            _held.Remove(oldest);
            _used.Remove(oldest);

            // Asked, not missing: the tile exists and is fetched again if it comes back into view.
            // Leaving it in _asked would mean it never is.
            _asked.Remove(oldest);
        }
    }

    /// <summary>Lets go of everything. For a screen leaving the foreground.</summary>
    public void Forget()
    {
        lock (_gate)
        {
            _held.Clear();
            _used.Clear();
            _asked.Clear();
            _failures.Clear();
        }
    }

    private async Task FetchAsync(TileKey key)
    {
        var folder = Folder();
        var path = folder is null ? null : System.IO.Path.Combine(folder, key.FileName);

        if (path is not null && File.Exists(path) && Load(key, path)) return;

        byte[] bytes;

        try
        {
            bytes = await _http.GetByteArrayAsync($"{Host}/{key.Path}");
        }
        catch (HttpRequestException ex) when (ex.StatusCode is System.Net.HttpStatusCode.NotFound
                                                  or System.Net.HttpStatusCode.Forbidden)
        {
            // The service answers 403 or 404 for a square it never rendered. That is an answer,
            // and it is final — remember it rather than asking again on the next pan.
            lock (_gate) _missing.Add(key);

            return;
        }
        catch (Exception)
        {
            // No network, or a timeout. Not an answer: ask again later.
            await RetryLaterAsync(key);

            return;
        }

        // Bytes that are not a picture, such as a truncated download or a captive portal's login
        // page, are a failure too, and are not cached to be failed on again next run.
        if (!Remember(key, bytes))
        {
            await RetryLaterAsync(key);

            return;
        }

        if (path is not null)
            try
            {
                await File.WriteAllBytesAsync(path, bytes);
            }
            catch (Exception)
            {
                // A full or unwritable cache costs a re-fetch next run, nothing more.
            }
    }

    /// <summary>
    /// Lets a failed tile be asked for again once <see cref="RetryAfter"/> has passed, and asks the
    /// screen for the frame that does the asking.
    ///
    /// Until then the tile stays in <c>_asked</c>, so the frames in between do not ask for it.
    /// </summary>
    private async Task RetryLaterAsync(TileKey key)
    {
        int failures;

        lock (_gate) failures = _failures[key] = _failures.GetValueOrDefault(key) + 1;

        await _wait(RetryAfter(failures));

        lock (_gate) _asked.Remove(key);

        Redraw?.Invoke();
    }

    /// <summary>
    /// Decodes and files a tile, and tells the screen there is something new to draw. False if the
    /// bytes are not a picture.
    /// </summary>
    private bool Remember(TileKey key, byte[] bytes)
    {
        IImage? image;

        try
        {
            using var stream = new MemoryStream(bytes);

            image = _decoder.Decode(stream);
        }
        catch (Exception)
        {
            image = null;
        }

        if (image is null) return false;

        lock (_gate)
        {
            MakeRoom();

            _held[key] = image;
            _used[key] = _tick;
            _failures.Remove(key);
        }

        // Outside the gate: whoever listens may well ask for a tile straight back.
        Redraw?.Invoke();

        return true;
    }

    /// <summary>A tile from the disk cache. A file that will not decode is deleted, so that the
    /// network is asked instead.</summary>
    private bool Load(TileKey key, string path)
    {
        try
        {
            if (Remember(key, File.ReadAllBytes(path))) return true;
        }
        catch (Exception)
        {
            // Unreadable counts as undecodable.
        }

        try
        {
            File.Delete(path);
        }
        catch (Exception)
        {
            // Nothing useful to do about a file that will not delete.
        }

        return false;
    }

    private string? Folder()
    {
        if (_folder is not null) return _folder;

        try
        {
            var folder = System.IO.Path.Combine(_environment.CacheDirectory, "tiles");

            Directory.CreateDirectory(folder);

            return _folder = folder;
        }
        catch (Exception)
        {
            // No disk cache. Everything still works, it just comes off the network each run.
            return null;
        }
    }
}
