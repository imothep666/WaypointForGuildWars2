using Avalonia.Controls;
using Avalonia.Interactivity;

using GuildWarsCompanion.Ava.Shell;
using GuildWarsCompanion.ViewModels;

namespace GuildWarsCompanion.Ava.Views;

public partial class MarkerPacksView : PageView
{
    public MarkerPacksView() => InitializeComponent();

    private MarkerPacksViewModel ViewModel => (MarkerPacksViewModel)DataContext!;

    public override bool CanRefresh => true;

    public override Task RefreshAsync() => ViewModel.RefreshAsync();

    public override async void OnAppearing() => await ViewModel.LoadAsync();

    private async void OnAddPack(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is PackRow row) await ViewModel.AddAsync(row);
    }
}
