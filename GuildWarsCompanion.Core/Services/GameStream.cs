using System.Runtime.CompilerServices;

using GuildWarsCompanion.Bridge.Protocol;

using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

namespace GuildWarsCompanion.Services;

/// <summary>Where the stream is up to, for a screen that has to say so.</summary>
public enum StreamState
{
    /// <summary>Nothing has been attempted yet.</summary>
    Idle,

    /// <summary>Finding a PC, or opening a connection to one.</summary>
    Connecting,

    /// <summary>Frames are arriving.</summary>
    Live,

    /// <summary>The connection dropped and another attempt is due.</summary>
    Retrying,

    /// <summary>No PC is paired at all. The one failure the user can act on directly.</summary>
    NotSetUp
}

/// <summary>
/// A held connection to the bridge, carrying what the game is doing frame by frame.
///
/// Separate from <see cref="BridgeClient"/> rather than a method on it, because it is a different
/// shape: everything there is one request and one answer, and this stays open. The four
/// request/response endpoints keep going through <see cref="IBridgeTransport"/> and are not
/// touched by this.
///
/// Server-sent events, not WebSockets, and that is measured rather than stylistic:
/// <c>ClientWebSocket</c> does not go through the <see cref="IStreamBinding"/> handler, so a
/// WebSocket connection cannot be pinned to Wi-Fi and never reaches a PC on a hotspot with no
/// internet. See ROADMAP.md, "SignalR for the compass, HTTP for everything else".
/// </summary>
public sealed class GameStream(
    IStreamBinding binding, BridgeFinder finder, Bridge.Protocol.Journal journal)
{
    /// <summary>
    /// How long to wait before the first retry after a connection dies.
    ///
    /// Short, because this runs behind a screen somebody is looking at while they play: a compass
    /// that stays dead long after the Wi-Fi came back is worse than one that tries too eagerly.
    /// </summary>
    private static readonly TimeSpan FirstRetry = TimeSpan.FromSeconds(3);

    /// <summary>
    /// The longest the gap between attempts is allowed to grow to.
    ///
    /// There is a backoff at all because a failed attempt is not free: it re-runs the whole
    /// BridgeFinder search, which can mean a broadcast and a subnet sweep. Doing that every three
    /// seconds for the hour somebody leaves this open beside a sleeping PC is a lot of radio for
    /// no news. Capped low enough that a PC coming back still feels prompt.
    /// </summary>
    private static readonly TimeSpan SlowestRetry = TimeSpan.FromSeconds(20);

    /// <summary>Where the connection is up to. Read by the screen; written only from the loop.</summary>
    public StreamState State { get; private set; } = StreamState.Idle;

    /// <summary>The PC being streamed from, once one has answered.</summary>
    public string? Pc { get; private set; }

    /// <summary>What went wrong last, or null. Kept for the same reason
    /// <see cref="BridgeClient.Trouble"/> is: "no PC answered" is a guess and usually the wrong
    /// one.</summary>
    public string? Trouble { get; private set; }

    /// <summary>
    /// Frames, for as long as the caller keeps enumerating.
    ///
    /// Reconnects by itself rather than ending: a screen that is open is a screen that wants the
    /// compass back when the Wi-Fi returns, and making every caller write that loop would mean
    /// several slightly different versions of it. Ends only when the caller cancels.
    ///
    /// A fresh <see cref="HubConnection"/> per attempt instead of SignalR's automatic reconnect.
    /// The stream has to be restarted after a drop regardless — a reconnected connection does
    /// not resume a stream — so the outer loop exists either way, and one mechanism is easier to
    /// reason about than two.
    /// </summary>
    public async IAsyncEnumerable<GameReport> WatchAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        var failures = 0;

        while (!ct.IsCancellationRequested)
        {
            State = StreamState.Connecting;

            if (!await finder.IsSetUpAsync())
            {
                State = StreamState.NotSetUp;
                yield break;
            }

            var connection = await OpenAsync(failures, ct);

            if (connection is null)
            {
                State = StreamState.Retrying;

                await DelayAsync(ct, ++failures);
                continue;
            }

            // Connected, so the next outage starts from the short wait again.
            failures = 0;

            await using (connection)
            {
                await foreach (var report in ReadAsync(connection, ct)) yield return report;
            }

            if (ct.IsCancellationRequested) break;

            State = StreamState.Retrying;

            await DelayAsync(ct, ++failures);
        }
    }

    /// <summary>
    /// Finds a PC and connects, or says why it could not.
    ///
    /// Resolution goes through the same <see cref="BridgeFinder"/> as everything else, so a
    /// compass screen benefits from a cached address the emote list already warmed and does not
    /// invent a second idea of where the PC is.
    /// </summary>
    private async Task<HubConnection?> OpenAsync(int failures, CancellationToken ct)
    {
        HubConnection? hub = null;

        try
        {
            var found = await FindAsync(failures, ct);

            if (found is null)
            {
                Trouble = finder.Problem ?? "No paired PC answered.";
                Pc = null;

                return null;
            }

            Pc = found.Bridge.Label;

            hub = Build(HubUrl(found.Address), found.Secret);

            await hub.StartAsync(ct);

            Trouble = null;

            var started = hub;

            // Handed to the caller. Anything still in `hub` when this leaves is rubbish.
            hub = null;

            return started;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex)
        {
            Trouble = Innermost(ex).Message;

            journal.Write("stream", $"connect failed - {Trouble}");

            return null;
        }
        finally
        {
            // Every path that does not return the connection disposes it. Not tidiness: a
            // HubConnection owns the HttpMessageHandler it was built with, and SignalR keeps
            // threads and timers behind both. Leaking one per failed attempt, at one attempt
            // every few seconds, exhausted the process's thread stacks in about nine minutes and
            // killed the app with SIGABRT - "shadow stack ... mprotect failed: Out of memory" on
            // a .NET TP Worker. Found by leaving the compass open with the bridge stopped.
            if (hub is not null) await hub.DisposeAsync();
        }
    }

    /// <summary>
    /// Where to try, without searching the network every three seconds.
    ///
    /// The address that last worked is reused directly, and a full <see cref="BridgeFinder"/>
    /// search only happens on the first attempt and then every <see cref="SearchEvery"/> failures.
    ///
    /// This is not an optimisation. A full search can end in a subnet sweep — a couple of hundred
    /// probes, each one an <c>HttpURLConnection</c> through <c>Network.OpenConnection</c>, and
    /// each of those leaves an OkHttp connection thread behind for its pool's keep-alive. Running
    /// that on a loop beside a switched-off PC took the process from 39 threads to 1834 in three
    /// minutes, measured, and 1799 of them were named "OkHttp Connecti". The retry loop is the
    /// only caller that searches over and over, which is why nothing before this hit it.
    /// </summary>
    private async Task<ResolvedBridge?> FindAsync(int failures, CancellationToken ct)
    {
        // The address that last worked, retried directly. Costs one connection attempt.
        if (_lastGood is { } known && failures % SearchEvery != 0) return known;

        // A search is the expensive path, so it is rationed by the clock rather than by the
        // failure count: with nothing paired and nothing answering there is no last-good address
        // to fall back on, and counting failures alone still searched on every single attempt.
        var since = DateTimeOffset.UtcNow - _lastSearch;

        if (_lastSearch != default && since < SearchNoOftenThan) return _lastGood;

        _lastSearch = DateTimeOffset.UtcNow;

        if (failures > 0) finder.Reset();

        // Never the sweep. This is the one caller that asks in a loop, and the sweep's cost is
        // paid in threads that do not come back.
        var found = await finder.ResolveAsync(ct, sweep: false);

        if (found is not null) _lastGood = found;

        return found;
    }

    /// <summary>How many consecutive failures before the network is searched again rather than
    /// the last known address simply retried.</summary>
    private const int SearchEvery = 5;

    /// <summary>
    /// The floor on how often the network may be searched, whatever the failure count says.
    ///
    /// A full search ends in a subnet sweep when nothing answers: a couple of hundred probes,
    /// each an HttpURLConnection through Network.OpenConnection, each leaving an OkHttp
    /// connection thread alive for its pool's keep-alive. On a retry loop beside a switched-off
    /// PC that took the process from 39 threads to 1834 in three minutes - 1799 of them named
    /// "OkHttp Connecti" - and eventually killed it with SIGABRT when a thread stack could no
    /// longer be allocated.
    ///
    /// Every other caller searches once, behind a tap. This is the only one that loops, which is
    /// why the sweep was survivable everywhere else and lethal here.
    /// </summary>
    private static readonly TimeSpan SearchNoOftenThan = TimeSpan.FromMinutes(1);

    private ResolvedBridge? _lastGood;
    private DateTimeOffset _lastSearch;

    /// <summary>
    /// Frames from one connection, ending quietly when it dies.
    ///
    /// A dropped stream is the normal case here, not an error: a phone goes through a door and
    /// the Wi-Fi goes with it. The loop above reconnects, so this only has to stop cleanly and
    /// leave a reason behind.
    /// </summary>
    private async IAsyncEnumerable<GameReport> ReadAsync(
        HubConnection hub, [EnumeratorCancellation] CancellationToken ct)
    {
        var frames = hub.StreamAsync<GameReport>("Stream", ct).GetAsyncEnumerator(ct);

        try
        {
            while (true)
            {
                GameReport report;

                try
                {
                    if (!await frames.MoveNextAsync()) break;

                    report = frames.Current;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Trouble = Innermost(ex).Message;

                    journal.Write("stream", $"dropped — {Trouble}");

                    break;
                }

                State = StreamState.Live;

                yield return report;
            }
        }
        finally
        {
            await frames.DisposeAsync();
        }
    }

    /// <summary>
    /// Where the hub lives, given where the bridge answered.
    ///
    /// The address may or may not carry a port, exactly as <see cref="BridgeFinder.Url"/> has to
    /// cope with. The hub is two past whatever that is — derived rather than discovered, so the
    /// phone needs nothing new from /hello.
    /// </summary>
    private static string HubUrl(string address)
    {
        var host = address.Trim();

        var colon = host.LastIndexOf(':');

        var port = colon > 0 && int.TryParse(host[(colon + 1)..], out var parsed)
            ? parsed
            : BridgeProtocol.DefaultPort;

        if (colon > 0) host = host[..colon];

        return $"http://{host}:{BridgeProtocol.HubPortFor(port)}{BridgeProtocol.HubPath}";
    }

    private HubConnection Build(string url, string secret) =>
        new HubConnectionBuilder()
            .WithUrl(url, options =>
            {
                options.Headers[BridgeProtocol.CodeHeader] = secret;

                // Server-sent events only, and no negotiation to fall back from. WebSockets
                // cannot be bound to Wi-Fi through the handler below, so leaving it in the set
                // would let SignalR pick the one transport that cannot reach the PC.
                options.Transports = HttpTransportType.ServerSentEvents;

                if (binding.Handler() is { } handler)
                    options.HttpMessageHandlerFactory = _ => handler;
            })
            .Build();

    /// <summary>
    /// Waits before the next attempt, treating cancellation as "stop" rather than as a fault.
    ///
    /// Doubles with each consecutive failure up to <see cref="SlowestRetry"/>, so a PC that comes
    /// back in seconds is picked up in seconds, while one switched off for an hour is asked about
    /// three times a minute instead of twenty.
    /// </summary>
    private static async Task DelayAsync(CancellationToken ct, int failures)
    {
        var wait = TimeSpan.FromMilliseconds(Math.Min(
            FirstRetry.TotalMilliseconds * Math.Pow(2, Math.Min(failures - 1, 8)),
            SlowestRetry.TotalMilliseconds));

        try
        {
            await Task.Delay(wait, ct);
        }
        catch (OperationCanceledException)
        {
            // The caller closed the screen. Nothing to report.
        }
    }

    private static Exception Innermost(Exception ex)
    {
        while (ex.InnerException is { } inner) ex = inner;

        return ex;
    }
}
