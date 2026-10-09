using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

using GuildWarsCompanion.Ava.Shell;
using GuildWarsCompanion.Ava.Ui;
using GuildWarsCompanion.Services;
using GuildWarsCompanion.ViewModels;

namespace GuildWarsCompanion.Ava.Views;

public partial class TodayView : PageView
{
    private DispatcherTimer? _timer;

    /// <summary>The look at the game between the minute's ticks: often enough that a new map is
    /// on the page soon after its loading screen, cheap because it is one request to the PC.</summary>
    private DispatcherTimer? _gameTimer;

    public TodayView()
    {
        InitializeComponent();

        DataContextChanged += (_, _) =>
        {
            if (DataContext is TodayViewModel model) model.PropertyChanged += OnModelChanged;
        };
    }

    /// <summary>While someone is in the game, the masthead stands on the loading screen of their map.</summary>
    private void OnModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TodayViewModel.PlayingMap)) ShowMapScene(ViewModel.PlayingMap);
    }

    private TodayViewModel ViewModel => (TodayViewModel)DataContext!;

    public override bool CanRefresh => true;

    public override Task RefreshAsync() => ViewModel.RefreshAsync();

    public override async void OnAppearing()
    {
        // Right away: coming back to the page, the countdown would otherwise show the minute it
        // was left at until the timer came round. A reset passed meanwhile means a fresh list.
        var passed = ViewModel.UpdateReset();

        // Keep the countdown to reset honest while the screen is open: one tick per minute, on
        // the minute, which is when the number changes. The reset falls on a whole minute, so
        // the remaining minutes change exactly when the clock's do.
        _timer ??= new DispatcherTimer();
        _timer.Tick -= OnTick;
        _timer.Tick += OnTick;
        ScheduleTick();

        _gameTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _gameTimer.Tick -= OnGameTick;
        _gameTimer.Tick += OnGameTick;
        _gameTimer.Start();

        if (ViewModel.Sections.Count == 0 || passed) await ViewModel.RefreshAsync();
        else await ViewModel.RefreshGoalsIfChangedAsync();
    }

    public override void OnDisappearing()
    {
        if (_gameTimer is not null)
        {
            _gameTimer.Tick -= OnGameTick;
            _gameTimer.Stop();
            _gameTimer = null;
        }

        if (_timer is null) return;

        _timer.Tick -= OnTick;
        _timer.Stop();
        _timer = null;
    }

    /// <summary>
    /// The next tick, just after the coming minute. Re-armed every time rather than left at a
    /// fixed interval, so it cannot drift, and a computer that slept wakes to the right number.
    /// </summary>
    private void ScheduleTick()
    {
        if (_timer is null) return;

        var now = DateTime.UtcNow;
        var next = new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerMinute, DateTimeKind.Utc).AddMinutes(1);

        _timer.Stop();
        _timer.Interval = next - now + TimeSpan.FromMilliseconds(50);
        _timer.Start();
    }

    private async void OnTick(object? sender, EventArgs e)
    {
        ScheduleTick();

        // The countdown wrapped round: the daily, the Vault and the bosses are new, and the list
        // on screen is still yesterday's. Otherwise the Here line, whose times are minutes too.
        if (ViewModel.UpdateReset()) await ViewModel.RefreshAsync();
        else await ViewModel.LookAtGameAsync(clockChanged: true);
    }

    private async void OnGameTick(object? sender, EventArgs e) => await ViewModel.LookAtGameAsync(clockChanged: false);

    private void OnRowClicked(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is TodayRow row) ViewModel.OpenCommand.Execute(row);
    }
}
