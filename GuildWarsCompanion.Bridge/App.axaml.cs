using System.Runtime.InteropServices;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

using GuildWarsCompanion.Bridge.ViewModels;
using GuildWarsCompanion.Bridge.Views;

namespace GuildWarsCompanion.Bridge;

public partial class App : Application
{
    private MainViewModel? _viewModel;
    private MainWindow? _window;

    /// <summary>Held for as long as the app runs: a registration that is collected stops listening.</summary>
    private readonly List<PosixSignalRegistration> _signals = [];

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _viewModel = new MainViewModel();
            _window = new MainWindow { DataContext = _viewModel };

            // Without this, hiding the only window ends the process — which is exactly what
            // "keep running in the tray" must not do. Quitting is now only ever explicit:
            // the tray's Quit item, or the window's close button when the setting is off.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.MainWindow = _window;

            _viewModel.ShowRequested += Reveal;
            _viewModel.ReminderRequested += Remind;
            _viewModel.QuitRequested += () => Quit(desktop);

            // The window's X, with "keep running in the tray" off: the window is gone, and with
            // an explicit shutdown mode nothing else would end the process.
            _window.Closed += (_, _) => Quit(desktop);

            // The session ending: the system asks, and the bridge tidies up before it goes.
            desktop.ShutdownRequested += (_, _) => _viewModel.Shutdown();

            ListenForStopSignals(desktop);

            _viewModel.Updates.Start();
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Puts a boss reminder on screen.
    ///
    /// A fresh window each time and no reference kept: it closes itself after a few seconds, and
    /// holding one open to reuse would mean deciding what to do when a second boss arrives while
    /// the first is still showing. There is only ever one — see BossReminder.Next.
    /// </summary>
    private static void Remind(Protocol.BossDue due) => new ReminderWindow(due).Show();

    /// <summary>
    /// Brings the window back, from wherever it went.
    ///
    /// All three calls are needed and for different reasons: it may be hidden, it may be
    /// minimised, and it may simply be behind something. Activate alone does nothing to a
    /// window that was hidden.
    /// </summary>
    private void Reveal()
    {
        if (_window is null) return;

        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    /// <summary>
    /// `kill`, a logout or a shutdown asks with SIGTERM, a terminal with Ctrl+C. Both now quit
    /// the way the tray's Quit does, so the hub and the server let go of their ports and the log
    /// says "stop". Before, the hub's host took these signals for itself and the bridge stayed
    /// running until a shutdown gave up waiting and killed it.
    /// </summary>
    private void ListenForStopSignals(IClassicDesktopStyleApplicationLifetime desktop)
    {
        foreach (var signal in new[] { PosixSignal.SIGTERM, PosixSignal.SIGINT })
        {
            try
            {
                _signals.Add(PosixSignalRegistration.Create(signal, context =>
                {
                    // Not the runtime's own handling, which would end the process at once.
                    context.Cancel = true;
                    Dispatcher.UIThread.Post(() => Quit(desktop));
                }));
            }
            catch (PlatformNotSupportedException)
            {
                // A platform without that signal has nothing to listen for.
            }
        }
    }

    private void OnTrayClicked(object? sender, EventArgs e) => Reveal();

    private void OnTrayShow(object? sender, EventArgs e) => Reveal();

    private void OnTrayQuit(object? sender, EventArgs e)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) Quit(desktop);
    }

    /// <summary>
    /// The one way out: the view model lets go of the server, the hub and the game first, then
    /// the app ends. Every route comes here — Quit in the window or the tray, the window's X,
    /// an update's restart, SIGTERM — because Avalonia's Shutdown() does not raise
    /// ShutdownRequested, and the routes that called only that ended without tidying up.
    /// </summary>
    private void Quit(IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (_quitting) return;
        _quitting = true;

        _viewModel?.Shutdown();
        desktop.Shutdown();
    }

    private bool _quitting;
}
