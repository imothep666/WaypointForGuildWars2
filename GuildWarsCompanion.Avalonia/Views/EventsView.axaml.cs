using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

using GuildWarsCompanion.Ava.Shell;
using GuildWarsCompanion.Services;
using GuildWarsCompanion.ViewModels;

namespace GuildWarsCompanion.Ava.Views;

public partial class EventsView : PageView
{
    private DispatcherTimer? _timer;

    public EventsView() => InitializeComponent();

    private EventsViewModel ViewModel => (EventsViewModel)DataContext!;

    public override bool CanRefresh => true;

    public override Task RefreshAsync() => ViewModel.RefreshAsync();

    public override async void OnAppearing()
    {
        // A countdown that does not count down is just a timestamp. Fifteen seconds is enough
        // for minute resolution, and the timer only lives between OnAppearing and
        // OnDisappearing, so it costs nothing off-screen.
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _timer.Tick += OnTick;
        _timer.Start();

        if (ViewModel.Bosses.Count == 0) await ViewModel.RefreshAsync();
    }

    public override void OnDisappearing()
    {
        if (_timer is null) return;

        _timer.Tick -= OnTick;
        _timer.Stop();
        _timer = null;
    }

    private async void OnTick(object? sender, EventArgs e)
    {
        // Once the first entry is in the past the order is wrong, so reload to re-sort.
        if (ViewModel.NeedsReorder)
            await ViewModel.RefreshAsync();
        else
            ViewModel.Tick();
    }

    private void OnRowClicked(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is EventRow row) ViewModel.ShowOnMapCommand.Execute(row);
    }
}
