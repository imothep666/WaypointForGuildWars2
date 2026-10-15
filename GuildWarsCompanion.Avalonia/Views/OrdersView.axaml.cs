using Avalonia.Controls;
using Avalonia.Interactivity;

using GuildWarsCompanion.Ava.Shell;
using GuildWarsCompanion.Services;
using GuildWarsCompanion.ViewModels;

namespace GuildWarsCompanion.Ava.Views;

public partial class OrdersView : PageView
{
    public OrdersView() => InitializeComponent();

    private OrdersViewModel ViewModel => (OrdersViewModel)DataContext!;

    public override bool CanRefresh => true;

    public override Task RefreshAsync() => ViewModel.RefreshAsync();

    public override async void OnAppearing()
    {
        if (ViewModel.Sections.Count == 0) await ViewModel.RefreshAsync();
    }

    private async void OnSendClicked(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is OrderRow row) await ViewModel.SendToTradingPostAsync(row);
    }
}
