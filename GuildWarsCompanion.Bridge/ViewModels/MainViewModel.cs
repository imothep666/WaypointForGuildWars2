using System.Collections.ObjectModel;
using System.Net;
using System.Reflection;

using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using GuildWarsCompanion.Bridge.Protocol;
using GuildWarsCompanion.Bridge.Stream;
using GuildWarsCompanion.Host;

namespace GuildWarsCompanion.Bridge.ViewModels;

/// <summary>
/// The whole application, really: hold a code, listen, put what arrives on the clipboard, and
/// show what happened.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly BridgeServer _server;
    private readonly BridgeBeacon _beacon;
    private readonly GameStreamHost _stream;

    /// <summary>
    /// The durable record, beside the config in %APPDATA%.
    ///
    /// The window's own list is memory only and capped at a hundred rows, which is the right
    /// shape for glancing at and useless the morning after. This is for the morning after.
    /// </summary>
    private readonly Journal _journal = new(BridgeSettings.LogPath);
    private readonly GameReadout _game = new();
    private BridgeConfig _config;
    private IClipboard? _clipboard;
    private DispatcherTimer? _gameTimer;

    /// <summary>Its own timer, because the game poll's interval is retuned between two seconds
    /// and a tenth of one depending on who is listening, and neither suits this.</summary>
    private DispatcherTimer? _bossTimer;

    /// <summary>Appearances already mentioned, so one boss is announced once.</summary>
    private readonly HashSet<string> _said = [];

    /// <summary>The bridge's own updates; see VelopackUpdates. Started by App once the window is up.</summary>
    public IAppUpdates Updates { get; }

    [RelayCommand]
    private Task CheckForUpdateAsync() => Updates.CheckAsync();

    [RelayCommand]
    private void RestartToUpdate() => Updates.RestartToUpdate();

    public MainViewModel()
    {
        _config = BridgeSettings.Load();
        _page = _config.PhoneSeen == true ? "status" : "pair";
        _minimiseToTray = _config.KeepInTray;
        _bossReminders = _config.RemindsAboutBosses;
        _startBlishHud = _config.StartsBlishHud;

        // First line of every session, so a log spanning days can be cut into runs. The id is
        // here because it is what a phone matches on, and never the secret.
        _journal.Write("start", $"bridge {Version} up, port {_config.Port}, id {_config.Id}, " +
                                $"name {_config.DisplayName}, os {Environment.OSVersion}");

        // Quitting for an update is the tray's Quit: the server and the icon go first.
        Updates = new GuildWarsCompanion.Ava.Desktop.VelopackUpdates(new BridgeMainThread(), _journal, Program.Args,
            exit: () => Dispatcher.UIThread.Post(() => QuitRequested?.Invoke()));
        _server = new BridgeServer(
            () => _config, OnRequestAsync, RequestShow, () => _game.Json, OnContact, OnGuideAsync, OnNotifyAsync);
        _beacon = new BridgeBeacon(() => _config);
        _stream = new GameStreamHost(() => _config, () => _game.Report);

        // Fires on a hub thread when a phone starts or stops streaming. All it does is retune
        // the poll, which touches a DispatcherTimer, so it has to cross to the UI thread first.
        _stream.ListenersChanged += () => Dispatcher.UIThread.Post(TuneGamePoll);

        _module.Changed += () => Dispatcher.UIThread.Post(ShowModule);
        _module.Reached += at => _journal.Write("guide", $"objective reached on map {at.MapId} — the phone hears on its next look");
    }

    /// <summary>
    /// Which build this is: 1.0.42+ee30fd6, the repository version and the commit it came from
    /// (see Directory.Build.targets). The phone's Settings page shows the same string for itself.
    /// </summary>
    public string Version => BuildVersion;

    private static readonly string BuildVersion =
        typeof(MainViewModel).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "unknown";

    /// <summary>Raised when something asks for the window back — the tray, or a second launch.</summary>
    public event Action? ShowRequested;

    /// <summary>Raised when a world boss is close enough to be worth a panel on the screen.</summary>
    public event Action<BossDue>? ReminderRequested;

    /// <summary>Raised when the user genuinely means to quit, rather than to close the window.</summary>
    public event Action? QuitRequested;

    private void RequestShow() =>
        Dispatcher.UIThread.Post(() => ShowRequested?.Invoke());

    [ObservableProperty] private string _status = "Not listening";

    /// <summary>
    /// Whether closing the window leaves the bridge running in the notification area.
    ///
    /// It has to be a setting rather than a behaviour, because a tray is not something every
    /// desktop has: see <see cref="BridgeConfig.MinimiseToTray"/>.
    /// </summary>
    [ObservableProperty] private bool _minimiseToTray;

    /// <summary>
    /// Whether a boss panel may appear. Off is remembered, because somebody who turned it off
    /// mid-fight should not have to do it again tomorrow.
    /// </summary>
    [ObservableProperty] private bool _bossReminders;

    /// <summary>Whether Blish HUD is started when the game is. See <see cref="BlishHud"/>.</summary>
    [ObservableProperty] private bool _startBlishHud;

    /// <summary>Windows only — see <see cref="BlishHud"/> for why Linux is left out.</summary>
    public bool OffersBlishHud => BlishHud.IsSupported;

    /// <summary>What the bridge's own copy of Blish HUD is doing: which version, or why not.</summary>
    [ObservableProperty] private string _blishHudLine = BlishIdle;

    private const string BlishIdle =
        "Downloaded from its GitHub releases and kept up to date. Starts and closes with the " +
        "game; close it mid-session and it stays closed.";

    [ObservableProperty] private bool _isListening;
    [ObservableProperty] private string? _problem;

    public string PairingCode => _config.PairingCode;

    public int Port => _config.Port;

    /// <summary>What the phone will call this PC — its hostname unless somebody renames it.</summary>
    public string PcName => _config.DisplayName;

    /// <summary>
    /// The pairing card as a QR code, or null on an install with no secret to offer.
    ///
    /// Rebuilt whenever the code changes rather than cached for the window's life, because
    /// "New code" has to invalidate the picture too — a QR that still pairs after the user
    /// pressed the button that un-pairs everything would be the worst kind of stale.
    /// </summary>
    public Bitmap? PairingImage => _pairingImage ??= Bridge.PairingImage.For(_config.Card);

    private Bitmap? _pairingImage;

    public bool HasPairingImage => PairingImage is not null;

    /// <summary>Every address the phone could be pointed at, ready to be typed in.</summary>
    public ObservableCollection<string> Addresses { get; } = [];

    /// <summary>Newest first. This is the audit trail as much as the feedback.</summary>
    public ObservableCollection<Arrival> Arrivals { get; } = [];

    public bool HasProblem => !string.IsNullOrEmpty(Problem);

    partial void OnProblemChanged(string? value) => OnPropertyChanged(nameof(HasProblem));

    /// <summary>
    /// The clipboard belongs to a window, so it arrives once the window does. Until then the
    /// bridge is not started at all — accepting a paste it cannot perform would be a lie.
    /// </summary>
    public void Attach(IClipboard? clipboard)
    {
        _clipboard = clipboard;
        Refresh();
        Start();
        WatchGame();

        if (StartBlishHud) UpdateBlishHud(force: true);
    }

    /// <summary>
    /// Polls MumbleLink on a timer.
    ///
    /// Two seconds because the answer is only ever read by a human glancing at it or a phone
    /// asking over the network, and a memory read is too cheap to be worth being clever about.
    /// The game rewrites the block every frame regardless of whether anyone is looking.
    /// </summary>
    /// <summary>What the window alone needs: a human glancing at a line of text.</summary>
    private static readonly TimeSpan IdlePoll = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Retunes the MumbleLink poll to whoever is watching.
    ///
    /// Two seconds is right for a window and useless for a compass needle, and ten a second is
    /// right for a needle and wasteful for a window nobody is reading. One reader either way:
    /// a second <see cref="GameWatch"/> would mean a second memory mapping and two independent
    /// opinions about whether the game is live.
    /// </summary>
    private void TuneGamePoll()
    {
        if (_gameTimer is not { } timer) return;

        var wanted = _stream.Listeners > 0 ? GameStreamHost.FrameInterval : IdlePoll;

        if (timer.Interval == wanted) return;

        timer.Interval = wanted;

        _journal.Write("stream", $"{_stream.Listeners} listening, polling every {wanted.TotalMilliseconds:0}ms");
    }

    private void WatchGame()
    {
        _gameTimer = new DispatcherTimer { Interval = IdlePoll };
        _gameTimer.Tick += (_, _) => PollGame();
        _gameTimer.Start();

        PollGame();

        _bossTimer = new DispatcherTimer { Interval = BossCheck };
        _bossTimer.Tick += (_, _) => PollBosses();
        _bossTimer.Start();
    }

    /// <summary>
    /// How often the timetable is consulted.
    ///
    /// Twenty seconds. The timetable is arithmetic over a hardcoded table, so this costs nothing;
    /// what it buys is that "in 10 minutes" is true to within a third of a minute when it is said.
    /// </summary>
    private static readonly TimeSpan BossCheck = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Mentions a boss if one is close, and only while the game is actually running.
    ///
    /// <b>Gated on the game being live, deliberately.</b> The whole argument for saying this on
    /// the PC is that the player is looking at the PC — and if Guild Wars 2 is not running, they
    /// are not, and the phone's own reminder is the right one instead. A panel appearing over a
    /// spreadsheet would be the app being rude about someone else's afternoon.
    /// </summary>
    private void PollBosses()
    {
        if (!BossReminders || !IsGameLive) return;

        var now = DateTimeOffset.UtcNow;

        if (BossReminder.Next(
                GuildWarsCompanion.Services.EventSchedule.NextAppearances(now), _said, now) is not { } due) return;

        _said.Add(due.Key);

        // Keeps the session's memory from growing without bound over a long evening. Nothing
        // older than the lead can still be pending, so anything beyond a day is spent.
        if (_said.Count > 64) _said.Clear();

        _ = RemindAsync(due);
    }

    /// <summary>
    /// In the game when the Blish HUD module shows alerts, where the player is looking; in the
    /// bridge's own small window otherwise, which a game in full screen may well cover.
    /// </summary>
    private async Task RemindAsync(BossDue due)
    {
        var inGame = await _module.NotifyAsync(due.Title, due.Where);

        _journal.Write("boss", $"{due.Title} — {due.Where}" + (inGame ? " — shown in the game" : string.Empty));

        if (!inGame) ReminderRequested?.Invoke(due);
    }

    /// <summary>
    /// How long after a phone's last request the window still calls it in touch.
    ///
    /// Ninety seconds, and the wording matters more than the number. There is no persistent
    /// connection to report — the phone asks when it has something to ask and is silent
    /// otherwise — so this cannot honestly say "connected now".
    ///
    /// Passing this deadline is <em>not</em> a fault, which is the whole point of the three
    /// states below. A heartbeat was considered instead and rejected: the phone can only answer
    /// one while the app is open, and during a play session it is not, so the light would have
    /// been dark exactly when everything was working.
    /// </summary>
    private static readonly TimeSpan PhoneRecent = TimeSpan.FromSeconds(90);

    private PhoneContact? _contact;

    /// <summary>
    /// Called from a socket thread on every request a phone makes.
    ///
    /// Marshalled because it writes what the window is bound to. Cheap enough to do on every
    /// request: it replaces a record rather than adding to a list.
    /// </summary>
    private void OnContact(PhoneContact contact) =>
        Dispatcher.UIThread.Post(() =>
        {
            // This PC talking to its own LAN address is not a phone, and IsLoopback does not
            // catch it — a request to 192.168.137.1 from 192.168.137.1 is as local as it gets
            // and still arrives with a routable source address. Found while testing this very
            // indicator, which cheerfully reported the desktop as the connected handset.
            if (_ownAddresses.Contains(contact.From)) return;

            // Every request, because the useful signal is the sequence: a burst of /hello is a
            // phone hunting for this PC, a lone /paste is somebody tapping an emote.
            _journal.Write("phone", $"{contact.From} {contact.Path}");

            _contact = contact;

            // Once ever, so the window opens on its status rather than on pairing from now on.
            if (_config.PhoneSeen != true)
            {
                _config = _config with { PhoneSeen = true };
                BridgeSettings.Save(_config);
            }

            // /hello and /game carry no verdict of their own, and a phone that is talking to
            // this PC again is evidence that whatever was refused before is over.
            if (!BridgeProtocol.PathIs(contact.Path, BridgeProtocol.PastePath)) _refusal = null;

            ShowPhone();
        });

    /// <summary>
    /// Every address this PC answers on, so its own traffic can be told from a phone's.
    ///
    /// Refreshed with the address list rather than read per request: enumerating network
    /// interfaces on every incoming connection would be real work for a question whose answer
    /// changes when somebody plugs in a cable.
    /// </summary>
    private HashSet<string> _ownAddresses = [];

    /// <summary>
    /// "192.168.137.191 · 3s ago", or that nothing has been in touch.
    ///
    /// Refreshed off the same two-second timer as the game readout so the age counts up on its
    /// own — a fixed "3s ago" that never moved would be worse than no number at all.
    /// </summary>
    private void ShowPhone()
    {
        // Nothing has knocked since this bridge started. Not a fault — the phone may simply not
        // have been picked up — so it reads as quiet rather than as a problem.
        if (_contact is not { } last)
        {
            PhoneLine = "nothing yet this session";
            SetPhone(fine: false, quiet: true);
            return;
        }

        var since = DateTimeOffset.Now - last.At;

        // A refusal stays on the light until something succeeds, because it is the one state
        // worth interrupting somebody for: a phone that is reaching this PC and being turned
        // away will not fix itself, and the reason is already known here.
        if (_refusal is { } why)
        {
            PhoneLine = $"{last.From} · {why}";
            SetPhone(fine: false, quiet: false);
            return;
        }

        if (since <= PhoneRecent)
        {
            PhoneLine = $"{last.From} · {Ago(since)} · {last.Path}";
            SetPhone(fine: true, quiet: false);
            return;
        }

        PhoneLine = $"{last.From} · quiet since {last.At:HH:mm}";
        SetPhone(fine: false, quiet: true);
    }

    /// <summary>
    /// Exactly one of the three, always. The bead's base style is red, so leaving both flags
    /// false is how a fault is drawn — which means a state nobody thought about would be drawn
    /// as a fault, and that is worth setting deliberately rather than by omission.
    /// </summary>
    private void SetPhone(bool fine, bool quiet)
    {
        IsPhoneFine = fine;
        IsPhoneQuiet = quiet;
    }

    /// <summary>
    /// Why the last request from a phone was turned away, or null if it was not.
    ///
    /// Cleared by the next request that is not refused, so the light follows the current truth
    /// rather than remembering a wrong code from an hour ago for ever.
    /// </summary>
    private string? _refusal;

    /// <summary>Seconds up to a minute, then minutes, then hours. Nothing needs more.</summary>
    private static string Ago(TimeSpan since) => since switch
    {
        { TotalSeconds: < 2 } => "just now",
        { TotalMinutes: < 1 } => $"{(int)since.TotalSeconds}s ago",
        { TotalHours: < 1 } => $"{(int)since.TotalMinutes}m ago",
        _ => $"{(int)since.TotalHours}h ago"
    };

    /// <summary>"192.168.137.191 · 3s ago · /paste", or why there is nothing to say.</summary>
    [ObservableProperty] private string _phoneLine = "nothing yet this session";

    /// <summary>A phone has been in touch within <see cref="PhoneRecent"/>. Brass.</summary>
    [ObservableProperty] private bool _isPhoneFine;

    /// <summary>Paired and nothing is wrong; there is simply no traffic. Banked-down brass.</summary>
    [ObservableProperty] private bool _isPhoneQuiet = true;

    /// <summary>The last reason a poll failed, so the log says it once rather than every tick.</summary>
    private string? _pollFailure;

    private void PollGame()
    {
        // A failed poll must not end the polling: an exception out of a timer's tick stops the
        // timer, and the phone was then sent the same last reading for as long as the bridge ran.
        try
        {
            PollGameOnce();
            _pollFailure = null;
        }
        catch (Exception ex)
        {
            if (_pollFailure != ex.Message) _journal.Write("game", $"poll FAILED: {ex.Message}");
            _pollFailure = ex.Message;
        }
    }

    private void PollGameOnce()
    {
        ShowPhone();

        _game.Update(DateTimeOffset.Now, _module.Arrival);

        StartBlishHudIfDue();
        CloseBlishHudIfGameGone();

        // Only transitions, never the poll itself. This runs every two seconds; a line each
        // time would bury the three moments that matter - logging in, logging out, changing
        // character - under thirty thousand that do not.
        if (_game.Line != GameLine || _game.IsLive != IsGameLive)
            _journal.Write("game", $"{(_game.IsLive ? "live" : "not running")} - {_game.Line}");

        GameLine = _game.Line;
        GameDetail = _game.Detail;
        IsGameLive = _game.IsLive;

        // A closed game is quiet, not broken — the same distinction the phone bead needed, and
        // missed here at first for the same reason: the bead's base style is red, so every
        // state nobody marked explicitly read as a failure.
        IsGameQuiet = !_game.IsLive && !_game.IsUnreadable;
    }

    /// <summary>The game process the Blish HUD start was last decided for. Zero before any.</summary>
    private uint _blishSession;

    /// <summary>A session began before there was a copy to start. The download finishing
    /// starts it then, rather than making somebody restart the game for it.</summary>
    private bool _blishWanted;

    private readonly BlishHudInstall _blish = new(BlishHudInstall.DefaultRoot);

    /// <summary>The update under way, if one is. One at a time: two would unpack into the same
    /// folder.</summary>
    private Task? _blishUpdate;

    private DateTimeOffset _blishCheckedAt = DateTimeOffset.MinValue;

    /// <summary>
    /// How often GitHub is asked. Blish HUD releases a few times a year, and unauthenticated
    /// API calls are sixty an hour per address, shared with anything else on the network.
    /// </summary>
    private static readonly TimeSpan BlishCheckEvery = TimeSpan.FromHours(6);

    /// <summary>
    /// Starts Blish HUD once per game session, if asked to, and uses the same moment to look for
    /// an update. The update never holds the start up: the version on disk starts now, and a
    /// newer one downloaded meanwhile is used from the next game on.
    /// </summary>
    private void StartBlishHudIfDue()
    {
        var processId = _game.Report.State?.ProcessId ?? 0;

        if (!BlishHud.IsSupported || !BlishHud.IsNewSession(_blishSession, processId, _game.IsLive))
            return;

        _blishSession = processId;

        if (!StartBlishHud) return;

        // Watched from here on, whether this start launches it or finds it already running: what
        // gets closed with the game is decided by folder, not by who started it.
        _blishGame = processId;
        _blishClosingUntil = DateTimeOffset.MinValue;

        UpdateBlishHud();

        if (_blish.Current() is { } installed) StartBlish(installed);
        else _blishWanted = true;
    }

    /// <summary>
    /// The module, as the build left it beside this executable. Null in a build that has none,
    /// in which case Blish HUD starts without it and emotes are copied as they always were.
    /// </summary>
    private static string? BundledModule =>
        Path.Combine(AppContext.BaseDirectory, "GuildWarsCompanion.Blish.bhm") is var path && File.Exists(path)
            ? path
            : null;

    private void StartBlish(InstalledBlish installed)
    {
        _blishWanted = false;

        if (!BlishHud.ShouldStart(StartBlishHud, installed.Exe, BlishHud.IsRunning)) return;

        if (BlishHud.Start(installed.Exe, BundledModule) is { } why)
        {
            Problem = $"Could not start Blish HUD — {why}";
            _journal.Write("blish", $"FAILED to start {installed.Exe}: {why}");
            return;
        }

        _journal.Write("blish", $"started {installed.Version}");
    }

    /// <summary>
    /// Checks GitHub for a newer Blish HUD and fetches it, unless that was done recently.
    ///
    /// Off the UI thread, because unpacking thirty megabytes is not something a window should
    /// freeze for. Every outcome ends up on <see cref="BlishHudLine"/>; nothing here is a reason
    /// to stop the bridge, and an offline evening plays on whatever version is already here.
    /// </summary>
    private void UpdateBlishHud(bool force = false)
    {
        if (!BlishHud.IsSupported || _blishUpdate is { IsCompleted: false }) return;

        var now = DateTimeOffset.UtcNow;

        if (!force && now - _blishCheckedAt < BlishCheckEvery && _blish.Current() is not null) return;

        _blishCheckedAt = now;
        _blishUpdate = UpdateBlishHudAsync();
    }

    private async Task UpdateBlishHudAsync()
    {
        var before = _blish.Current();

        BlishHudLine = before is null
            ? "Looking for the latest Blish HUD…"
            : Ready(before, "checking for updates…");

        try
        {
            var after = await Task.Run(() => _blish.UpdateAsync(
                BlishHud.IsRunning,
                say => Dispatcher.UIThread.Post(() => BlishHudLine = say)));

            if (after is null)
            {
                BlishHudLine = "GitHub has no Blish HUD release this can install — only releases " +
                               "that publish a checksum are installed.";
                _journal.Write("blish", "no installable release");
                return;
            }

            if (after.Version != before?.Version)
                _journal.Write("blish", $"installed {after.Version} in {_blish.Root}");

            BlishHudLine = Ready(after, "up to date.");

            // Asked for while there was nothing to start, and the game is still being played.
            if (_blishWanted && StartBlishHud && _game.IsLive) StartBlish(after);
        }
        catch (Exception ex)
        {
            // Retried at the next game start rather than in six hours: a failure is not a check.
            _blishCheckedAt = DateTimeOffset.MinValue;

            BlishHudLine = before is null
                ? $"Could not download Blish HUD — {ex.Message}"
                : Ready(before, $"could not check for updates ({ex.Message}).");

            _journal.Write("blish", $"update FAILED: {ex.Message}");
        }
    }

    /// <summary>The game process Blish HUD is running alongside, or zero when there is none to
    /// watch.</summary>
    private uint _blishGame;

    private DateTimeOffset _blishLookedAt = DateTimeOffset.MinValue;

    /// <summary>Until when a Blish HUD still running is closed. See <see cref="BlishCloseFor"/>.</summary>
    private DateTimeOffset _blishClosingUntil = DateTimeOffset.MinValue;

    private bool _blishClosing;

    /// <summary>The game poll runs ten times a second while a compass streams. Asking Windows
    /// about a process does not need to.</summary>
    private static readonly TimeSpan BlishLookEvery = TimeSpan.FromSeconds(2);

    /// <summary>
    /// How long after the game exits a Blish HUD is still closed.
    ///
    /// Not once, because Blish HUD set to stay in the tray restarts itself when the game exits,
    /// and the new process can appear after the first pass has already looked. Half a minute
    /// covers that; the game starting again ends it at once, so it never closes the Blish HUD
    /// that belongs to the next session.
    /// </summary>
    private static readonly TimeSpan BlishCloseFor = TimeSpan.FromSeconds(30);

    /// <summary>How long Blish HUD gets to close itself before it is ended.</summary>
    private static readonly TimeSpan BlishGrace = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Closes the bridge's Blish HUD once the game it was running alongside has exited.
    ///
    /// Only looked at while the tick is still, since a moving tick is a running game. A still
    /// tick is not enough on its own — see <see cref="BlishHud.IsGameRunning"/> — so the process
    /// MumbleLink named is asked about directly.
    /// </summary>
    private void CloseBlishHudIfGameGone()
    {
        if (!BlishHud.IsSupported || _game.IsLive) return;

        var now = DateTimeOffset.UtcNow;

        if (now - _blishLookedAt < BlishLookEvery) return;

        _blishLookedAt = now;

        if (_blishGame != 0)
        {
            if (BlishHud.IsGameRunning(_blishGame)) return;

            _journal.Write("blish", $"game process {_blishGame} has exited");

            _blishGame = 0;
            _blishClosingUntil = now + BlishCloseFor;
        }

        if (now < _blishClosingUntil && !_blishClosing) _ = CloseBlishHudAsync();
    }

    private async Task CloseBlishHudAsync()
    {
        _blishClosing = true;

        try
        {
            var (closed, problem) = await Task.Run(() => BlishHud.CloseFromAsync(_blish.Root, BlishGrace));

            if (closed > 0) _journal.Write("blish", $"closed {closed} with the game");
            if (problem is not null) _journal.Write("blish", $"could not close one: {problem}");
        }
        finally
        {
            _blishClosing = false;
        }
    }

    private static string Ready(InstalledBlish installed, string how) =>
        $"Blish HUD {installed.Version} · {how} Starts and closes with the game; close it " +
        "mid-session and it stays closed.";

    partial void OnStartBlishHudChanged(bool value)
    {
        // Fetched the moment it is ticked, so the first game after is not the one kept waiting.
        if (value) UpdateBlishHud(force: true);
        else BlishHudLine = BlishIdle;

        // Ticked mid-game: the session in progress counts as new, so it starts now rather than
        // next time. Unticked: nothing is watched, so nothing is closed behind somebody's back.
        _blishSession = 0;
        _blishGame = 0;

        if (value == _config.StartsBlishHud) return;

        _config = _config with { StartBlishHud = value };
        BridgeSettings.Save(_config);

        _journal.Write("blish", value ? "start with game on" : "start with game off");
    }

    /// <summary>
    /// Opens the log in whatever the desktop uses for text.
    ///
    /// <c>UseShellExecute</c> on Windows and <c>xdg-open</c> on Linux, because .NET will not
    /// launch a document by association unless told to, and the two platforms disagree about
    /// how to ask.
    /// </summary>
    [RelayCommand]
    private void OpenLog()
    {
        try
        {
            if (OperatingSystem.IsWindows())
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(_journal.Location) { UseShellExecute = true });
            else
                System.Diagnostics.Process.Start("xdg-open", _journal.Location);
        }
        catch (Exception ex)
        {
            // Say where it is, which is the thing they actually wanted.
            Problem = $"Could not open the log - {ex.Message}. It is at {_journal.Location}";
        }
    }

    /// <summary>"Imothep · Ranger", or why there is nothing to say.</summary>
    [ObservableProperty] private string _gameLine = "Looking for Guild Wars 2…";

    [ObservableProperty] private string _gameDetail = string.Empty;

    [ObservableProperty] private bool _isGameLive;

    /// <summary>The game is not running, which is ordinary. Banked-down brass, never red.</summary>
    [ObservableProperty] private bool _isGameQuiet = true;

    /// <summary>
    /// Whether to offer the Linux setup instructions.
    ///
    /// Linux only, because on Windows there is nothing to set up, and only while the game is
    /// not being read, because once the bead is brass the instructions are just clutter next to
    /// the answer they were meant to produce. Note that a Linux user who has never set the
    /// helper up sees this permanently, which is correct: for them it is not a transient state.
    /// </summary>
    public bool NeedsMumbleHelp => OperatingSystem.IsLinux() && !IsGameLive;

    partial void OnIsGameLiveChanged(bool value) => OnPropertyChanged(nameof(NeedsMumbleHelp));

    private void Start()
    {
        try
        {
            _server.Start(_config.Port);

            // Started after the listener, and its failure is not the listener's. Discovery only
            // saves the phone from being told an address; pasting works without it.
            _beacon.Start();

            // Windows only, like Blish HUD itself here. Its failure is its own: without it emotes
            // are copied, which is exactly what they were before it existed.
            if (BlishHud.IsSupported)
            {
                _module.Start(ModuleWire.PortFor(_config.Port));

                _journal.Write("module", _module.Problem is { } why
                    ? $"FAILED on {ModuleWire.PortFor(_config.Port)}: {why}"
                    : $"listening on loopback {ModuleWire.PortFor(_config.Port)}");

                ShowModule();
            }

            _journal.Write(
                "beacon", _beacon.Problem ?? $"listening on UDP {BridgeProtocol.DiscoveryPort}");

            IsListening = true;
            Problem = null;
            Status = $"Listening on port {_server.Port}";

            _journal.Write("listen", $"listening on {_server.Port}");

            // Started last and never awaited into the failure path above: the compass stream is
            // the one part of this that the bridge's original job does not depend on. A port
            // collision here costs one screen, so it is reported and stepped over.
            _ = StartStreamAsync();
        }
        catch (Exception ex)
        {
            IsListening = false;
            Status = "Not listening";

            // Almost always the port: something else has it, or a previous copy is still
            // running. Say which port, since the setting is right there to change.
            Problem = $"Could not listen on port {_config.Port} — {ex.Message}";

            _journal.Write("listen", $"FAILED on {_config.Port}: {ex.Message}");
        }
    }

    [RelayCommand]
    private void Refresh()
    {
        Addresses.Clear();

        var mine = LanAddresses.Find();

        _ownAddresses = [.. mine];

        foreach (var address in mine)
            Addresses.Add($"{address}:{_config.Port}");

        if (Addresses.Count == 0)
            Problem = "No network address found. This PC is not on a network the phone can reach.";
    }

    /// <summary>
    /// Which page the window shows. Pairing, until a phone has ever been in touch with this PC;
    /// the status from then on, because that is what is looked at.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStatusPage), nameof(IsPairPage), nameof(IsSettingsPage))]
    private string _page = "status";

    public bool IsStatusPage => Page == "status";

    public bool IsPairPage => Page == "pair";

    public bool IsSettingsPage => Page == "settings";

    [RelayCommand]
    private void ShowPage(string page) => Page = page;

    /// <summary>
    /// A new code, which un-pairs every phone that had the old one. That is the point of it —
    /// it is the only revocation there is.
    /// </summary>
    [RelayCommand]
    private void NewCode()
    {
        // Both halves, because both are credentials: leaving the scanned secret alone would
        // mean the button that un-pairs every phone quietly did not.
        _config = _config with
        {
            PairingCode = BridgeProtocol.NewPairingCode(),
            Secret = BridgeProtocol.NewPairingSecret()
        };

        BridgeSettings.Save(_config);

        _pairingImage = null;

        OnPropertyChanged(nameof(PairingCode));
        OnPropertyChanged(nameof(PairingImage));
        OnPropertyChanged(nameof(HasPairingImage));

        _journal.Write("pairing", "code and secret rotated - every paired phone is now un-paired");

        Note(new Arrival(DateTimeOffset.Now, "this PC", "Pairing code changed", PasteVerdict.NotPaired));
    }

    [RelayCommand]
    private void ClearLog() => Arrivals.Clear();

    partial void OnMinimiseToTrayChanged(bool value)
    {
        if (value == _config.KeepInTray) return;

        _config = _config with { MinimiseToTray = value };
        BridgeSettings.Save(_config);
    }

    partial void OnBossRemindersChanged(bool value)
    {
        if (value == _config.RemindsAboutBosses) return;

        _config = _config with { BossReminders = value };
        BridgeSettings.Save(_config);

        _journal.Write("boss", value ? "reminders on" : "reminders off");
    }

    [RelayCommand]
    private void Show() => ShowRequested?.Invoke();

    [RelayCommand]
    private void Quit() => QuitRequested?.Invoke();

    /// <summary>
    /// Called from a socket thread, so everything it touches is marshalled first: the clipboard
    /// belongs to the UI thread and so does the list the window is bound to.
    /// </summary>
    private Task<PasteResult> OnRequestAsync(PasteResult result, IPAddress? from) =>
        Dispatcher.UIThread.InvokeAsync(async () =>
        {
            // Done in the game when it can be, and only then skips the clipboard.
            if (await PerformAsync(result, from) is { } performed) return performed;

            var copied = result.IsAccepted && await CopyAsync(result.Text);

            // A paste that was well formed, correctly coded, and still did not reach the
            // clipboard is a failure, and it has to be said as one. This used to be reported as
            // Accepted in both directions — 204 to the phone and a brass spine in this window —
            // with only the row's text hinting that anything had gone wrong.
            var final = result.IsAccepted && !copied
                ? result with { Verdict = PasteVerdict.ClipboardRefused }
                : result;

            // The length, never the text. What arrives here is whatever the user copied, and
            // a log quietly keeping a record of everything they ever pasted is not a log
            // anybody would want sitting on their disk.
            _journal.Write("paste", $"{from?.ToString() ?? "unknown"} {final.Verdict} " +
                                    $"{result.Text.Length} chars" +
                                    (copied ? string.Empty : $" — {Problem ?? final.Reason}"));

            // Drives the red state, and is cleared by anything that works. A paste that was
            // refused is the one thing here a person has to act on.
            _refusal = copied ? null : final.Reason;

            ShowPhone();

            Note(new Arrival(
                DateTimeOffset.Now,
                from?.ToString() ?? "unknown",
                copied ? result.Text : result.IsAccepted ? Problem ?? final.Reason : result.Reason,
                final.Verdict));

            return final;
        });

    /// <summary>
    /// The link to the Blish HUD module, which does emotes in the game. See
    /// <see cref="ModuleWire"/>.
    /// </summary>
    private readonly ModuleLink _module = new(ModuleWire.CardPath(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)));

    /// <summary>
    /// How long the module gets to say done or busy. It answers within a frame when it is there;
    /// this is for when it is wedged, and leaves most of the phone's eight seconds for the
    /// clipboard fallback.
    /// </summary>
    private static readonly TimeSpan ModuleAnswerWithin = TimeSpan.FromSeconds(2);

    /// <summary>Why the last emote went to the clipboard instead of the game, for the window.</summary>
    private string? _moduleDeclined;

    /// <summary>
    /// Has the Blish HUD module do an emote, or returns null for the clipboard to take it.
    ///
    /// Three gates, all of which must pass: the phone asked for it on /emote, the text is exactly
    /// an emote from the app's list, and the module is connected. Anything else — a chat link,
    /// "/wave hello", "/gg" — never reaches the game from here, whatever the phone sent.
    /// </summary>
    private async Task<PasteResult?> PerformAsync(PasteResult result, IPAddress? from)
    {
        if (!result.IsAccepted || !result.IsEmote ||
            !GuildWarsCompanion.Services.Emotes.IsCommand(result.Text) || !_module.IsConnected)
            return null;

        var answer = await _module.PerformAsync(result.Text, ModuleAnswerWithin);

        _journal.Write("emote", $"{from?.ToString() ?? "unknown"} {result.Text} {answer.Outcome}" +
                                (answer.IsDone ? string.Empty : $" — {answer.Reason}, copying instead"));

        if (!answer.IsDone)
        {
            _moduleDeclined = answer.Reason;
            ShowModule();
            return null;
        }

        _moduleDeclined = null;
        _refusal = null;
        ShowModule();
        ShowPhone();

        var done = result with { Verdict = PasteVerdict.Performed };

        Note(new Arrival(DateTimeOffset.Now, from?.ToString() ?? "unknown", $"{result.Text} · done in game", done.Verdict));

        return done;
    }

    /// <summary>
    /// The trail the phone would like drawn in the game, passed on to the Blish HUD module.
    ///
    /// Checked here as well as in the module: the bridge is the gate between the network and the
    /// game, and a body that is not a trail stops at it. 204 whether or not a module is connected —
    /// the phone has done its part, and the trail is kept for the module to be given when it comes.
    /// </summary>
    private async Task<int> OnGuideAsync(string? body)
    {
        var trail = GuideWire.Parse(body, out var clear);

        if (trail is null && !clear) return 400;

        var text = GuideWire.Write(trail);
        var delivered = await _module.ShowTrailAsync(text);

        _journal.Write("guide", (trail is null
                                    ? "cleared"
                                    : trail.IsObjectiveOnly
                                        ? $"map {trail.MapId}, objective alone"
                                        : $"map {trail.MapId}, {trail.Points.Count} points") +
                                (delivered ? " — sent to the Blish HUD module" : " — kept for the module"));

        return 204;
    }

    /// <summary>
    /// An alert the phone found while the game is being played, shown in it by the Blish HUD module.
    /// 204 only when the module took it: otherwise the phone puts it on its own screen, which is
    /// the point of answering honestly — an alert said nowhere is worse than one said twice.
    /// </summary>
    private async Task<int> OnNotifyAsync(string? body)
    {
        if (BridgeProtocol.ParseNotify(body) is not { } alert) return 400;

        var shown = await _module.NotifyAsync(alert.Title, alert.Detail);

        _journal.Write("alert", $"{alert.Title} — " + (shown ? "shown in the game" : "no module to show it, left to the phone"));

        return shown ? 204 : 409;
    }

    /// <summary>"Emotes from the phone: done in game", or why they are copied instead.</summary>
    [ObservableProperty] private string _emoteLine = string.Empty;

    private void ShowModule() =>
        EmoteLine = _module.IsConnected
            ? _moduleDeclined is { } why
                ? $"Emotes from the phone: done in game when it can — the last one was copied instead ({why})."
                : "Emotes from the phone: done in game through the Blish HUD module."
            : _module.Problem is { } problem
                ? $"Emotes from the phone: copied — the module link could not start ({problem})."
                : "Emotes from the phone: copied to the clipboard until Blish HUD is started from here.";

    /// <summary>
    /// Puts one string on this PC's clipboard, and says whether it got there.
    ///
    /// The return value is the point. An earlier version skipped silently when the clipboard
    /// was unavailable and still logged the paste as accepted, which is the worst of both: the
    /// phone is told it worked, the window agrees, and Ctrl+V produces whatever was there
    /// before. A refusal has to be visible in both places.
    /// </summary>
    private async Task<bool> CopyAsync(string text)
    {
        if (_clipboard is null)
        {
            Problem = "No clipboard is available — this window has no display connection.";
            return false;
        }

        Exception? last = null;
        var deadline = DateTime.UtcNow + Budget;

        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            // Windows lets exactly one process hold the clipboard open at a time, and plenty of
            // things take it for a moment — clipboard managers, remote desktop, an editor
            // syntax-highlighting a copy. OpenClipboard then fails with CLIPBRD_E_CANT_OPEN and
            // Avalonia surfaces it rather than retrying, so the retry lives here. Found on the
            // first real paste this app ever did, which failed for exactly that reason.
            if (attempt > 0)
            {
                // Out of budget. Stopping here beats starting an attempt whose answer would
                // arrive after the phone had given up waiting for it.
                if (DateTime.UtcNow >= deadline) break;

                await Task.Delay(RetryPause * attempt);
            }

            try
            {
                await _clipboard.SetTextAsync(text);

                // Flushed immediately rather than at exit. On Windows and X11 the clipboard
                // holds a reference to the owning process rather than a copy of the data, so
                // without this the text would vanish the moment this app closed.
                await _clipboard.FlushAsync();

                Problem = null;
                return true;
            }
            catch (Exception ex)
            {
                last = ex;
            }
        }

        Problem = $"Could not write to the clipboard — {last?.Message}. " +
                  "Something else on this PC is holding it open.";

        // Also to stderr, because the window is not always the place someone is looking: on
        // Linux this often runs from a terminal, and a clipboard that silently does nothing is
        // the single hardest thing to diagnose about a program like this.
        Console.Error.WriteLine($"[bridge] {Problem}");

        return false;
    }

    /// <summary>Enough to ride out another program holding the clipboard, few enough that a
    /// genuinely stuck one is reported rather than hung on.</summary>
    private const int Attempts = 6;

    /// <summary>
    /// How long the clipboard may be fought over before the answer is "no".
    ///
    /// An attempt count alone was not a bound on time. Each blocked <c>SetTextAsync</c> costs
    /// about a second on a clipboard that is refusing, so six of them spent nearly seven
    /// seconds — and the phone stops listening long before that, then reports a network failure
    /// for what was a clipboard problem. That is the least useful of the two diagnoses, and it
    /// was measured on this machine, not imagined: /hello answered in 4ms while /paste took
    /// 6.9s. Answering inside the budget is what lets the phone say the true thing.
    /// </summary>
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(2);

    /// <summary>Multiplied by the attempt number, so the wait backs off: 0, 60, 120 … 300ms.</summary>
    private static readonly TimeSpan RetryPause = TimeSpan.FromMilliseconds(60);

    /// <summary>Newest first, and bounded — this runs for weeks at a time.</summary>
    private void Note(Arrival arrival)
    {
        Arrivals.Insert(0, arrival);

        while (Arrivals.Count > 100) Arrivals.RemoveAt(Arrivals.Count - 1);
    }

    private async Task StartStreamAsync()
    {
        await _stream.StartAsync(_config.Port);

        _journal.Write(
            "stream",
            _stream.Problem is { } why
                ? $"FAILED on {BridgeProtocol.HubPortFor(_config.Port)}: {why}"
                : $"hub on {BridgeProtocol.HubPortFor(_config.Port)}{BridgeProtocol.HubPath}");
    }

    /// <summary>Once only: Quit and the session ending can both ask.</summary>
    private bool _shutDown;

    public void Shutdown()
    {
        if (_shutDown) return;
        _shutDown = true;

        _journal.Write("stop", "bridge shutting down");

        _gameTimer?.Stop();
        _bossTimer?.Stop();

        // Before the readout is disposed: a hub still serving frames would otherwise read a
        // GameWatch whose memory mapping has just been closed underneath it.
        //
        // Off the UI thread, and bounded. This runs from ShutdownRequested and from the tray's
        // Quit item, both of which are on Avalonia's UI thread, and that thread carries a
        // SynchronizationContext: awaiting StopAsync there and then blocking on the result
        // deadlocked, because the continuations it schedules need the very thread the block is
        // holding. Task.Run moves the stop to a pool thread, which captures no context, so the
        // continuations have somewhere to go.
        //
        // The wait is capped as well as moved. StopAsync already gives Kestrel two seconds to
        // drain, so three here is that plus room to unwind; a hub that will not stop inside it
        // must not keep the window on screen.
        try
        {
            if (!Task.Run(() => _stream.StopAsync()).Wait(TimeSpan.FromSeconds(3)))
                _journal.Write("stop", "hub did not stop in time; closing anyway");
        }
        catch (Exception ex)
        {
            _journal.Write("stop", $"hub stop failed: {ex.Message}");
        }

        _game.Dispose();
        _module.Dispose();
        _beacon.Dispose();
        _server.Stop();
    }
}
