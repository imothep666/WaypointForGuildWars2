using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

using GuildWarsCompanion.Ava.Shell;
using GuildWarsCompanion.Services;
using GuildWarsCompanion.ViewModels;

namespace GuildWarsCompanion.Ava.Views;

public partial class SessionView : PageView
{
    public SessionView() => InitializeComponent();

    private SessionViewModel ViewModel => (SessionViewModel)DataContext!;

    public override bool CanRefresh => true;

    public override Task RefreshAsync() => ViewModel.RefreshAsync();

    /// <summary>Opened from Today's recap, which is always the latest session.</summary>
    public override async void OnAppearing() => await ViewModel.ShowLatestAsync();

    /// <summary>An earlier session takes the page, read from the top.</summary>
    private async void OnSessionClicked(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not SessionRow row) return;

        await ViewModel.ShowAsync(row);
        Scroller.Offset = new Vector(0, 0);
    }
}
