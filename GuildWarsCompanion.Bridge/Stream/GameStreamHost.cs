using GuildWarsCompanion.Bridge.Protocol;

using Microsoft.AspNetCore.Builder;
// Not implicit here: this is Microsoft.NET.Sdk, not Microsoft.NET.Sdk.Web, so the Web SDK's
// implicit usings do not apply and UseUrls has to be brought in by hand.
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GuildWarsCompanion.Bridge.Stream;

/// <summary>
/// Kestrel, one hub, and nothing else.
///
/// Runs beside <see cref="BridgeServer"/> rather than replacing it. The four request/response
/// endpoints work, are covered by tests, and reach the phone through the transport that beats
/// Android's routing table; moving them here would risk all of that to gain nothing. What this
/// adds is the one thing a socket-per-request design cannot do — a connection that stays open.
///
/// Kestrel is a managed socket server, so binding every interface needs no urlacl and no
/// elevation. That was measured rather than assumed; see ROADMAP.md.
/// </summary>
/// <param name="config">
/// Read fresh per connection rather than captured, matching <see cref="BridgeServer"/>: the
/// pairing code can change while the bridge runs, and a stream must not outlive the credential
/// it was opened with.
/// </param>
/// <param name="report">The latest reading. Supplied rather than polled here so that one
/// <see cref="GameWatch"/> feeds both the window and the wire.</param>
public sealed class GameStreamHost(Func<BridgeConfig> config, Func<GameReport> report)
{
    /// <summary>
    /// Ten a second.
    ///
    /// Fast enough that a compass needle moves rather than steps, slow enough that it is nothing
    /// next to what the game itself is doing on the same machine. The game rewrites MumbleLink
    /// every frame regardless, so this costs a memory read and a serialise.
    /// </summary>
    public static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(100);

    private WebApplication? _app;
    private int _listeners;

    public Func<BridgeConfig> Config => config;

    public Func<GameReport> Report => report;

    /// <summary>
    /// How many phones are streaming right now.
    ///
    /// The bridge's only true presence signal. Every other part of this protocol is
    /// request/response, which is why the window can say a phone was "in touch recently" and
    /// nothing stronger — a held connection is the one case where "connected now" is honest.
    /// </summary>
    public int Listeners => Volatile.Read(ref _listeners);

    /// <summary>Raised when the first phone joins or the last one leaves, so the poll rate can
    /// follow. Fires on a request thread; marshal before touching anything bound to a window.</summary>
    public event Action? ListenersChanged;

    public bool IsListening => _app is not null;

    /// <summary>Why the hub is not running, or null when it is.</summary>
    public string? Problem { get; private set; }

    public void Joined()
    {
        Interlocked.Increment(ref _listeners);
        ListenersChanged?.Invoke();
    }

    public void Left()
    {
        Interlocked.Decrement(ref _listeners);
        ListenersChanged?.Invoke();
    }

    /// <summary>
    /// Starts the hub, or records why it could not start.
    ///
    /// Failure is survivable and must not be fatal: the bridge's whole existing job — pasting,
    /// discovery, answering /game — works without this. A port collision here should cost the
    /// compass screen and nothing else, so it is caught rather than thrown.
    /// </summary>
    public async Task StartAsync(int bridgePort)
    {
        await StopAsync();

        try
        {
            var builder = WebApplication.CreateBuilder();

            // Every interface, cleartext, like the rest of this protocol. A LAN bridge cannot
            // have a certificate for an address that changes with the lease; see the phone's
            // network_security_config.xml.
            builder.WebHost.UseUrls($"http://0.0.0.0:{BridgeProtocol.HubPortFor(bridgePort)}");

            // Quiet by default. This is a desktop tray app with no console to write to, and the
            // journal is where anything worth keeping already goes.
            builder.Logging.SetMinimumLevel(LogLevel.Warning);

            builder.Services.AddSignalR();
            builder.Services.AddSingleton(this);

            var app = builder.Build();

            app.MapHub<GameHub>(BridgeProtocol.HubPath);

            await app.StartAsync();

            _app = app;
            Problem = null;
        }
        catch (Exception ex)
        {
            Problem = ex.Message;
            _app = null;
        }
    }

    public async Task StopAsync()
    {
        if (_app is not { } app) return;

        _app = null;
        Volatile.Write(ref _listeners, 0);

        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));

            await app.StopAsync(deadline.Token);
            await app.DisposeAsync();
        }
        catch (Exception)
        {
            // Shutting down. A hub that will not stop cleanly is not worth surfacing to someone
            // who is closing the app.
        }
    }
}
