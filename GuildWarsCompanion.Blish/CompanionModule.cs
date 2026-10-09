using System;
using System.Collections.Concurrent;
using System.ComponentModel.Composition;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Blish_HUD;
using Blish_HUD.Modules;

using GuildWarsCompanion.Bridge.Protocol;

using Microsoft.Xna.Framework;

namespace GuildWarsCompanion.Blish;

/// <summary>
/// Does the emote the phone asked for, through Blish HUD's own chat service, and draws the trail to
/// the phone's objective in the world.
///
/// No window, no settings, no API key: it connects to the bridge on this PC, waits for
/// "emote 7 /wave" or a trail, and acts on them. The bridge has already checked that an emote is
/// exactly one from the app's list and that a trail is a trail, so what is left here is whether the
/// game is in a state to receive an emote, and drawing the line.
/// </summary>
[Export(typeof(Module))]
public sealed class CompanionModule : Module
{
    private static readonly Logger Logger = Logger.GetLogger<CompanionModule>();

    /// <summary>Emotes that arrived on the socket, waiting for the game thread.</summary>
    private readonly ConcurrentQueue<ModuleLine> _asked = new ConcurrentQueue<ModuleLine>();

    private readonly object _writing = new object();

    private StreamWriter? _writer;
    private CancellationTokenSource? _stopping;

    private readonly TrailEntity _trail = new TrailEntity();

    /// <summary>A trail that arrived on the socket, for the game thread to put in the world. Set
    /// to the trail, or to <see cref="Cleared"/>; null means nothing new.</summary>
    private volatile object? _arrivedTrail;

    private static readonly object Cleared = new object();

    [ImportingConstructor]
    public CompanionModule([Import("ModuleParameters")] ModuleParameters moduleParameters)
        : base(moduleParameters)
    {
    }

    protected override Task LoadAsync()
    {
        GameService.Graphics.World.AddEntity(_trail);

        // Told once the trail has played out and gone, not when the character arrives: the phone
        // answers by clearing its objective, and a clear arriving mid-animation would cut it short.
        _trail.Reached += reached =>
        {
            Reply(ModuleWire.Arrived(reached));
            Logger.Info($"Objective reached on map {reached.MapId}.");
        };

        _stopping = new CancellationTokenSource();

        var ct = _stopping.Token;
        _ = Task.Run(() => KeepConnectedAsync(ct));

        return Task.CompletedTask;
    }

    /// <summary>
    /// Connects to the bridge, and connects again whenever that stops working.
    ///
    /// The card is read afresh each time, because the bridge makes a new token every run: a
    /// bridge restarted while Blish HUD stays up must not leave the module knocking with last
    /// run's key.
    /// </summary>
    private async Task KeepConnectedAsync(CancellationToken ct)
    {
        var card = ModuleWire.CardPath(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));

        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (File.Exists(card) && ModuleWire.TryReadCard(File.ReadAllText(card), out var port, out var token))
                    await TalkAsync(port, token, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                Logger.Debug(ex, "Lost the Waypoint bridge; trying again shortly.");
            }
            finally
            {
                lock (_writing) _writer = null;
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task TalkAsync(int port, string token, CancellationToken ct)
    {
        using var client = new TcpClient();

        // net48 has no cancellable connect; closing the client is how a stop interrupts it.
        using var stop = ct.Register(() => client.Close());

        await client.ConnectAsync(IPAddress.Loopback, port);

        var stream = client.GetStream();
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        using var reader = new StreamReader(stream, utf8);
        var writer = new StreamWriter(stream, utf8) { NewLine = "\n", AutoFlush = true };

        await writer.WriteLineAsync(ModuleWire.Hello(token));

        if (ModuleWire.Parse(await reader.ReadLineAsync())?.Kind != ModuleLineKind.Ok) return;

        lock (_writing) _writer = writer;

        Logger.Info("Connected to the Waypoint bridge.");

        while (!ct.IsCancellationRequested)
        {
            var text = await reader.ReadLineAsync();
            if (text is null) return;

            switch (ModuleWire.Parse(text))
            {
                case { Kind: ModuleLineKind.Emote } line:
                    _asked.Enqueue(line);
                    break;

                case { Kind: ModuleLineKind.Trail }:
                    await ReadTrailAsync(reader);
                    break;
            }
        }
    }

    /// <summary>
    /// The body of a trail block, up to its end line. Parsed here, off the game thread; handed over
    /// whole, so a frame never draws half of one. A block that does not parse is dropped and the line
    /// already drawn is left alone.
    /// </summary>
    private async Task ReadTrailAsync(StreamReader reader)
    {
        var body = new StringBuilder();

        // A bound on the block, on top of GuideWire's own: a bridge that never sends "end" must not
        // grow this for ever.
        for (var lines = 0; lines < GuideWire.MostPoints + 8; lines++)
        {
            var text = await reader.ReadLineAsync();
            if (text is null) return;

            if (ModuleWire.Parse(text) is { Kind: ModuleLineKind.End })
            {
                var trail = GuideWire.Parse(body.ToString(), out var clear);

                if (trail is not null) _arrivedTrail = trail;
                else if (clear) _arrivedTrail = Cleared;

                Logger.Info(trail is null
                    ? clear ? "Trail cleared." : "Ignored a trail that did not parse."
                    : trail.IsObjectiveOnly
                        ? $"Objective on map {trail.MapId}, no trail, height {(trail.HeightKnown ? "known" : "unknown")}."
                        : $"Trail on map {trail.MapId}, {trail.Points.Count} points.");

                return;
            }

            body.Append(text).Append('\n');
        }
    }

    /// <summary>
    /// Every frame, on the game thread: Blish HUD's services are read from here in every module,
    /// and the chat service expects to be driven from here too.
    /// </summary>
    protected override void Update(GameTime gameTime)
    {
        if (Interlocked.Exchange(ref _arrivedTrail, null) is { } arrived)
            _trail.Show(arrived as GuidedTrail);

        while (_asked.TryDequeue(out var line))
        {
            var why = WhyNot(line.Text);

            if (why is null) GameService.GameIntegration.Chat.Send(line.Text);

            Reply(why is null ? ModuleWire.Done(line.Id) : ModuleWire.Busy(line.Id, why));
        }
    }

    /// <summary>
    /// Why the emote cannot be done right now, or null if it can.
    ///
    /// Blish HUD's chat service makes the same checks and silently does nothing when one fails;
    /// asking first is what lets the phone be told why, and the bridge fall back to the clipboard.
    /// Chat already being open is the one it does not check, and the one that matters most: the
    /// emote would be pasted into the middle of whatever the player was typing.
    /// </summary>
    private static string? WhyNot(string command)
    {
        // A second look at what the bridge already checked, kept because it costs nothing: a
        // chat line with a space in it is not an emote, whatever sent it.
        if (!command.StartsWith("/", StringComparison.Ordinal) || command.IndexOf(' ') >= 0 || command.Length > 40)
            return "not an emote";

        var gw2 = GameService.GameIntegration.Gw2Instance;

        if (!gw2.Gw2IsRunning || !gw2.IsInGame) return "not in the game";
        if (!gw2.Gw2HasFocus) return "the game is not in front";
        if (GameService.Gw2Mumble.UI.IsTextInputFocused) return "a text box is open";
        if (GameService.Gw2Mumble.UI.IsMapOpen) return "the map is open";

        return null;
    }

    private void Reply(string text)
    {
        lock (_writing)
        {
            try
            {
                _writer?.WriteLine(text);
            }
            catch (Exception ex)
            {
                // The bridge went away between asking and hearing back. It stops waiting on its
                // own, and the reconnect loop notices the socket is gone.
                Logger.Debug(ex, "Could not answer the Waypoint bridge.");
            }
        }
    }

    protected override void Unload()
    {
        GameService.Graphics.World.RemoveEntity(_trail);
        _trail.Dispose();

        _stopping?.Cancel();
        _stopping?.Dispose();
        _stopping = null;
    }
}
