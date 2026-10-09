using GuildWarsCompanion.Ava.Shell;
using GuildWarsCompanion.ViewModels;

namespace GuildWarsCompanion.Ava.Views;

public partial class DeliveryView : PageView
{
    public DeliveryView() => InitializeComponent();

    private DeliveryViewModel ViewModel => (DeliveryViewModel)DataContext!;

    public override bool CanRefresh => true;

    public override Task RefreshAsync() => ViewModel.RefreshAsync();

    public override async void OnAppearing() => await ViewModel.RefreshAsync();
}
