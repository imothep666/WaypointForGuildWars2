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
using Blish_HUD.Controls;
using Blish_HUD.Modules;
using Blish_HUD.Settings;

using GuildWarsCompanion.Bridge.Protocol;

using Microsoft.Xna.Framework;

namespace GuildWarsCompanion.Blish;

/// <summary>
/// Does the emote the phone asked for, through Blish HUD's own chat service, and draws the trail to
/// the phone's objective in the world.
///
/// No API key: it connects to the bridge on this PC, waits for "emote 7 /wave" or a trail, and acts
/// on them. The bridge has already checked that an emote is exactly one from the app's list and that
/// a trail is a trail, so what is left here is whether the game is in a state to receive an emote,
/// and drawing the line.
///
/// Its only window is the objective card — the name, distance and height of what the trail leads
/// to — and its only icon the corner one, which says whether the bridge is there and shows or hides
/// the card.
/// </summary>
[Export(typeof(Module))]
public sealed class CompanionModule : Module
{
    private static readonly Logger Logger = Logger.GetLogger<CompanionModule>();

    /// <summary>Emotes that arrived on the socket, waiting for the game thread.</summary>
    private readonly ConcurrentQueue<ModuleLine> _asked = new ConcurrentQueue<ModuleLine>();

    /// <summary>Alerts that arrived on the socket, with when, waiting for the game to be in view.</summary>
    private readonly ConcurrentQueue<(ModuleLine Line, DateTime At)> _alerts = new ConcurrentQueue<(ModuleLine, DateTime)>();

    /// <summary>
    /// How long an alert waits for the game to be in view — a loading screen, the character
    /// select — before it is dropped. A boss due in five minutes is no news ten minutes on.
    /// </summary>
    private static readonly TimeSpan AlertsKeep = TimeSpan.FromMinutes(3);

    private readonly object _writing = new object();

    private StreamWriter? _writer;
    private CancellationTokenSource? _stopping;

    private readonly TrailEntity _trail = new TrailEntity();

    /// <summary>A trail that arrived on the socket, for the game thread to put in the world. Set
    /// to the trail, or to <see cref="Cleared"/>; null means nothing new.</summary>
    private volatile object? _arrivedTrail;

    private static readonly object Cleared = new object();

    private readonly ModuleParameters _parameters;

    /// <summary>Whether the link to the bridge is up; written on the socket's thread.</summary>
    private volatile bool _connected;

    private SettingEntry<bool>? _showCard;
    private SettingEntry<bool>? _showAlerts;
    private SettingEntry<int>? _cardX;
    private SettingEntry<int>? _cardY;

    private ObjectiveCard? _card;
    private CornerIcon? _icon;
    private bool _iconSaysConnected;
    private bool _cardPlaced;

    [ImportingConstructor]
    public CompanionModule([Import("ModuleParameters")] ModuleParameters moduleParameters)
        : base(moduleParameters)
    {
        _parameters = moduleParameters;
    }

    protected override void DefineSettings(SettingCollection settings)
    {
        _showCard = settings.DefineSetting("ShowObjectiveCard", true, () => Words.ShowCard, () => Words.ShowCardWhy);
        _showAlerts = settings.DefineSetting("ShowAlerts", true, () => Words.ShowAlerts, () => Words.ShowAlertsWhy);

        // The bridge is told at once, so the phone stops sending alerts here the moment they are
        // switched off rather than having them dropped.
        _showAlerts.SettingChanged += (_, e) => Reply(ModuleWire.Alerts(e.NewValue));

        // Where the card was dragged to: kept, never shown in the settings. Negative until it has
        // been moved, and then placed beside the top of the screen.
        var place = settings.AddSubCollection("ObjectiveCardPlace");
        _cardX = place.DefineSetting("X", -1);
        _cardY = place.DefineSetting("Y", -1);
    }

    protected override Task LoadAsync()
    {
        GameService.Graphics.World.AddEntity(_trail);

        _card = new ObjectiveCard { Parent = GameService.Graphics.SpriteScreen, Visible = false };
        _card.Dropped += at =>
        {
            if (_cardX is null || _cardY is null) return;
            _cardX.Value = at.X;
            _cardY.Value = at.Y;
        };

        _icon = new CornerIcon
        {
            Icon = _parameters.ContentsManager.GetTexture("waypoint.png"),
            BasicTooltipText = Words.Waiting,
            Priority = 1_645_017
        };
        _icon.Click += (_, _) =>
        {
            if (_showCard is not null) _showCard.Value = !_showCard.Value;
        };

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
                _connected = false;
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
        _connected = true;

        Reply(ModuleWire.Alerts(_showAlerts?.Value != false));

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

                case { Kind: ModuleLineKind.Notify } line:
                    _alerts.Enqueue((line, DateTime.UtcNow));
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

        if (_icon is not null && _iconSaysConnected != _connected)
        {
            _iconSaysConnected = _connected;
            _icon.BasicTooltipText = _connected ? Words.Connected : Words.Waiting;
        }

        ShowAlerts();
        UpdateCard();
    }

    /// <summary>
    /// Puts waiting alerts on screen in Blish HUD's own way, once the game is in view. They wait
    /// through a loading screen; past <see cref="AlertsKeep"/> they are dropped.
    /// </summary>
    private void ShowAlerts()
    {
        if (_alerts.IsEmpty) return;

        var gw2 = GameService.GameIntegration.Gw2Instance;
        var inView = gw2.Gw2IsRunning && gw2.IsInGame;

        while (_alerts.TryPeek(out var waiting))
        {
            if (DateTime.UtcNow - waiting.At > AlertsKeep || _showAlerts?.Value == false)
            {
                _alerts.TryDequeue(out _);
                continue;
            }

            if (!inView) return;

            _alerts.TryDequeue(out _);

            var text = waiting.Line.Detail.Length == 0
                ? waiting.Line.Text
                : $"{waiting.Line.Text} · {waiting.Line.Detail}";

            ScreenNotification.ShowNotification(text, ScreenNotification.NotificationType.Info, _icon?.Icon?.Texture, 6);
        }
    }

    /// <summary>
    /// The card, every frame: shown while there is an objective and the game is in view, and
    /// saying how far it is from where the character now stands.
    /// </summary>
    private void UpdateCard()
    {
        if (_card is null) return;

        _card.Drag();

        var gw2 = GameService.GameIntegration.Gw2Instance;
        var trail = _trail.Current;

        _card.Visible = trail is not null && _showCard?.Value != false &&
                        gw2.IsInGame && !GameService.Gw2Mumble.UI.IsMapOpen;

        if (!_card.Visible || trail is null) return;

        if (GameService.Gw2Mumble.CurrentMap.Id != trail.MapId)
        {
            // MumbleLink's metres restart on every map: no distance means anything from here.
            _card.Say(trail.Name, Words.OtherMap, null, quiet: true);
        }
        else if (_trail.HasArrived)
        {
            _card.Say(trail.Name, Words.Arrived, null, quiet: false);
        }
        else
        {
            // Blish HUD's character is in its own world, z up; the trail is in MumbleLink's.
            var character = GameService.Gw2Mumble.PlayerCharacter.Position;
            var across = Math.Sqrt(Math.Pow(trail.Target.X - character.X, 2) + Math.Pow(trail.Target.Z - character.Y, 2));

            _card.Say(trail.Name, CardText.Distance(across),
                CardText.Climb(trail.Target.Up - character.Z, trail.HeightKnown), quiet: false);
        }

        if (!_cardPlaced && _cardX is not null && _cardY is not null)
        {
            _cardPlaced = true;

            // First shown: where it was left, or beside the top of the screen, clear of the target
            // frame in the middle.
            var screen = GameService.Graphics.SpriteScreen.Size;
            _card.Location = _card.Keep(_cardX.Value >= 0
                ? new Point(_cardX.Value, _cardY.Value)
                : new Point(screen.X / 2 + 200, 40));
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

        _card?.Dispose();
        _card = null;
        _icon?.Dispose();
        _icon = null;

        _stopping?.Cancel();
        _stopping?.Dispose();
        _stopping = null;
    }
}
