using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

using GuildWarsCompanion.Bridge.ViewModels;
using GuildWarsCompanion.Bridge.Views;

namespace GuildWarsCompanion.Bridge;

public partial class App : Application
{
    private MainViewModel? _viewModel;
    private MainWindow? _window;

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
            _viewModel.QuitRequested += () => desktop.Shutdown();

            desktop.ShutdownRequested += (_, _) => _viewModel.Shutdown();
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

    private void OnTrayClicked(object? sender, EventArgs e) => Reveal();

    private void OnTrayShow(object? sender, EventArgs e) => Reveal();

    private void OnTrayQuit(object? sender, EventArgs e)
    {
        _viewModel?.Shutdown();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }
}
