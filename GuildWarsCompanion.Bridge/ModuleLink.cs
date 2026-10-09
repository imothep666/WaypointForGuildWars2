using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

using GuildWarsCompanion.Bridge.Protocol;

namespace GuildWarsCompanion.Bridge;

/// <summary>How asking the module went.</summary>
public enum ModuleOutcome
{
    Done,

    /// <summary>The module was there and would not: the game unfocused, chat already open.</summary>
    Busy,

    NotConnected,

    /// <summary>Connected, asked, and silent past the deadline.</summary>
    NoAnswer
}

public sealed record ModuleAnswer(ModuleOutcome Outcome, string Reason)
{
    public bool IsDone => Outcome == ModuleOutcome.Done;
}

/// <summary>
/// The bridge's end of the wire to its Blish HUD module. See <see cref="ModuleWire"/> for what is
/// said on it and why the module exists at all.
///
/// One module at a time. A second hello replaces the first rather than joining it, because two
/// Blish HUDs typing one emote is two emotes, and the newer connection is the one most likely to
/// belong to the game being played.
/// </summary>
public sealed class ModuleLink(string cardPath) : IDisposable
{
    private readonly ConcurrentDictionary<int, TaskCompletionSource<ModuleAnswer>> _pending = new();
    private readonly SemaphoreSlim _writing = new(1, 1);
    private readonly object _gate = new();

    private TcpListener? _listener;
    private CancellationTokenSource? _stopping;
    private Connection? _current;
    private string _token = string.Empty;
    private int _nextId;

    /// <summary>The last trail the phone sent, as <see cref="GuideWire"/> text, or null for none.
    /// Kept so a module that connects later — Blish HUD started after the objective was chosen,
    /// or restarted — is handed it at once rather than drawing nothing until the next choice.</summary>
    private volatile string? _trail;

    /// <summary>
    /// The objective the module last said the character reached, or null. Read by the game poll
    /// and put in the report the phone reads, which is how the phone hears of it: the phone asks,
    /// the bridge never calls it. Cleared by the next trail or clear from the phone, so an arrival
    /// is only ever about the trail being drawn.
    /// </summary>
    public TrailArrival? Arrival { get; private set; }

    /// <summary>Raised on a module arriving or leaving, from a socket thread.</summary>
    public event Action? Changed;

    public bool IsConnected
    {
        get
        {
            lock (_gate) return _current is not null;
        }
    }

    public string? Problem { get; private set; }

    /// <summary>
    /// Listens on loopback and writes the card the module reads to find it.
    ///
    /// A fresh token every run: the card is readable by anything running as this user, and a
    /// token that outlived the bridge would be a key left under the mat.
    /// </summary>
    public void Start(int port)
    {
        try
        {
            _token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));

            _listener = new TcpListener(IPAddress.Loopback, port);
            _listener.Start();

            Directory.CreateDirectory(Path.GetDirectoryName(cardPath)!);
            File.WriteAllText(cardPath, ModuleWire.Card(port, _token));

            _stopping = new CancellationTokenSource();
            _ = AcceptLoopAsync(_listener, _stopping.Token);

            Problem = null;
        }
        catch (Exception ex)
        {
            Problem = ex.Message;
            _listener?.Stop();
            _listener = null;
        }
    }

    /// <summary>
    /// Asks the module to do one emote, and says how that went.
    ///
    /// Bounded, because the phone is waiting on the answer and has its own timeout. A module
    /// that does not answer in time is treated as not having done it — the caller falls back to
    /// the clipboard — and if it does get round to it late, the answer arrives to nobody.
    /// </summary>
    public async Task<ModuleAnswer> PerformAsync(string command, TimeSpan within)
    {
        Connection? to;
        lock (_gate) to = _current;

        if (to is null) return new ModuleAnswer(ModuleOutcome.NotConnected, "Blish HUD module not connected");

        var id = Interlocked.Increment(ref _nextId);
        var answer = new TaskCompletionSource<ModuleAnswer>(TaskCreationOptions.RunContinuationsAsynchronously);

        _pending[id] = answer;

        try
        {
            if (!await SendAsync(to, ModuleWire.Emote(id, command)))
                return new ModuleAnswer(ModuleOutcome.NotConnected, "Blish HUD module went away");

            var first = await Task.WhenAny(answer.Task, Task.Delay(within));

            return first == answer.Task
                ? answer.Task.Result
                : new ModuleAnswer(ModuleOutcome.NoAnswer, "Blish HUD module did not answer");
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;

            try
            {
                client = await listener.AcceptTcpClientAsync(ct);
            }
            catch (Exception)
            {
                return;
            }

            _ = ServeAsync(client, ct);
        }
    }

    private async Task ServeAsync(TcpClient client, CancellationToken ct)
    {
        var connection = new Connection(client);

        try
        {
            // The hello has to arrive promptly and be right. Anything else on this port is
            // not the module, and is not kept waiting on.
            using (var hello = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                hello.CancelAfter(TimeSpan.FromSeconds(5));

                var first = ModuleWire.Parse(await connection.Reader.ReadLineAsync(hello.Token));

                if (first is not { Kind: ModuleLineKind.Hello } ||
                    !CryptographicOperations.FixedTimeEquals(
                        Encoding.UTF8.GetBytes(first.Text), Encoding.UTF8.GetBytes(_token)))
                    return;
            }

            if (!await SendAsync(connection, ModuleWire.Ok)) return;

            Connection? replaced;
            lock (_gate)
            {
                replaced = _current;
                _current = connection;
            }

            replaced?.Dispose();
            Changed?.Invoke();

            if (_trail is { } trail) await SendTrailAsync(connection, trail);

            while (!ct.IsCancellationRequested)
            {
                var text = await connection.Reader.ReadLineAsync(ct);
                if (text is null) break;

                if (ModuleWire.Parse(text) is not { } line) continue;

                if (line.Kind == ModuleLineKind.Arrived)
                {
                    Arrive(line.Text);
                    continue;
                }

                var answer = line.Kind switch
                {
                    ModuleLineKind.Done => new ModuleAnswer(ModuleOutcome.Done, string.Empty),
                    ModuleLineKind.Busy => new ModuleAnswer(ModuleOutcome.Busy, line.Text),
                    _ => null
                };

                if (answer is not null && _pending.TryGetValue(line.Id, out var waiting))
                    waiting.TrySetResult(answer);
            }
        }
        catch (Exception)
        {
            // A module closing mid-read, or Blish HUD being ended. Either way it is gone.
        }
        finally
        {
            var left = false;

            lock (_gate)
            {
                if (_current == connection)
                {
                    _current = null;
                    left = true;
                }
            }

            connection.Dispose();

            if (left) Changed?.Invoke();
        }
    }

    /// <summary>
    /// Hands the module the trail to draw, or takes it away with a <c>clear</c> body. Remembered
    /// either way, for the next module to connect. Returns whether a module was there to take it.
    /// </summary>
    public async Task<bool> ShowTrailAsync(string guideText)
    {
        _trail = guideText;
        Arrival = null;

        Connection? to;
        lock (_gate) to = _current;

        return to is not null && await SendTrailAsync(to, guideText);
    }

    /// <summary>
    /// The module reached an objective. Believed only when it names the trail being drawn: one the
    /// phone has replaced since is somebody else's news. The trail is forgotten too, as the module
    /// has already taken it down — a module connecting later is not handed it again.
    /// </summary>
    private void Arrive(string objective)
    {
        if (GuideWire.Parse(_trail, out _) is not { } trail ||
            ModuleWire.Arrived(trail) != $"{ModuleWire.ArrivedWord} {objective}")
            return;

        _trail = GuideWire.Write(null);
        Arrival = new TrailArrival(trail.MapId, trail.Target.X, trail.Target.Z);

        Reached?.Invoke(Arrival);
    }

    /// <summary>Raised when the module reports an objective reached, from a socket thread.</summary>
    public event Action<TrailArrival>? Reached;

    /// <summary>The block in one go, under the write lock, so an emote cannot land in the middle
    /// of a trail and be read as a point.</summary>
    private async Task<bool> SendTrailAsync(Connection to, string guideText)
    {
        await _writing.WaitAsync();

        try
        {
            await to.Writer.WriteLineAsync(ModuleWire.Trail);

            foreach (var line in guideText.Split('\n'))
                if (line.Length > 0) await to.Writer.WriteLineAsync(line);

            await to.Writer.WriteLineAsync(ModuleWire.End);
            await to.Writer.FlushAsync();

            return true;
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            _writing.Release();
        }
    }

    private async Task<bool> SendAsync(Connection to, string line)
    {
        await _writing.WaitAsync();

        try
        {
            await to.Writer.WriteLineAsync(line);
            await to.Writer.FlushAsync();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            _writing.Release();
        }
    }

    public void Dispose()
    {
        _stopping?.Cancel();
        _listener?.Stop();

        lock (_gate)
        {
            _current?.Dispose();
            _current = null;
        }

        try
        {
            // The token dies with this run, so the card that carries it goes too.
            if (File.Exists(cardPath)) File.Delete(cardPath);
        }
        catch (Exception)
        {
            // Stale is harmless: the next run overwrites it, and its token opens nothing.
        }
    }

    private sealed class Connection : IDisposable
    {
        private readonly TcpClient _client;

        public Connection(TcpClient client)
        {
            _client = client;

            var stream = client.GetStream();
            var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

            Reader = new StreamReader(stream, utf8);
            Writer = new StreamWriter(stream, utf8) { NewLine = "\n" };
        }

        public StreamReader Reader { get; }

        public StreamWriter Writer { get; }

        public void Dispose() => _client.Dispose();
    }
}
