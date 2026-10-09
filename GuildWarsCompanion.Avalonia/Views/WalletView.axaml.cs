using GuildWarsCompanion.Ava.Shell;
using GuildWarsCompanion.ViewModels;

namespace GuildWarsCompanion.Ava.Views;

public partial class WalletView : PageView
{
    public WalletView() => InitializeComponent();

    private WalletViewModel ViewModel => (WalletViewModel)DataContext!;

    public override bool CanRefresh => true;

    public override Task RefreshAsync() => ViewModel.RefreshAsync();

    public override async void OnAppearing()
    {
        if (ViewModel.Currencies.Count == 0) await ViewModel.RefreshAsync();
    }
}
