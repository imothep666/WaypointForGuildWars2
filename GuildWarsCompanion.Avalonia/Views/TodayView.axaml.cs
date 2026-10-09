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

    public TodayView()
    {
        InitializeComponent();

        DataContextChanged += (_, _) =>
        {
            if (DataContext is TodayViewModel model) model.PropertyChanged += OnModelChanged;
        };
    }

    /// <summary>While someone is in the game, the masthead stands on the loading screen of their map.</summary>
    private async void OnModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(TodayViewModel.PlayingMap)) return;

        var map = ViewModel.PlayingMap;

        if (map is null)
        {
            Scene = null;
            return;
        }

        var scene = await WikiArt.ForMapAsync(map);

        // They may have moved on, or left the game, while the painting was on its way.
        if (ViewModel.PlayingMap != map) return;

        Scene = scene;
    }

    private TodayViewModel ViewModel => (TodayViewModel)DataContext!;

    public override bool CanRefresh => true;

    public override Task RefreshAsync() => ViewModel.RefreshAsync();

    public override async void OnAppearing()
    {
        // Keep the countdown to reset honest while the screen is open.
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _timer.Tick += OnTick;
        _timer.Start();

        if (ViewModel.Sections.Count == 0) await ViewModel.RefreshAsync();
        else await ViewModel.RefreshGoalsIfChangedAsync();
    }

    public override void OnDisappearing()
    {
        if (_timer is null) return;

        _timer.Tick -= OnTick;
        _timer.Stop();
        _timer = null;
    }

    private void OnTick(object? sender, EventArgs e) => ViewModel.UpdateReset();

    private void OnRowClicked(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is TodayRow row) ViewModel.OpenCommand.Execute(row);
    }
}
