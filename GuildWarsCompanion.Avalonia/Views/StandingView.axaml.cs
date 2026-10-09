using GuildWarsCompanion.Ava.Shell;
using GuildWarsCompanion.ViewModels;

namespace GuildWarsCompanion.Ava.Views;

public partial class StandingView : PageView
{
    public StandingView() => InitializeComponent();

    private StandingViewModel ViewModel => (StandingViewModel)DataContext!;

    public override bool CanRefresh => true;

    public override Task RefreshAsync() => ViewModel.RefreshAsync();

    public override async void OnAppearing()
    {
        if (ViewModel.Rows.Count == 0) await ViewModel.RefreshAsync();
    }
}
