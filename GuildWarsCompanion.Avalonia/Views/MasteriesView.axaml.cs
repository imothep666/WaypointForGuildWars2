using GuildWarsCompanion.Ava.Shell;
using GuildWarsCompanion.ViewModels;

namespace GuildWarsCompanion.Ava.Views;

public partial class MasteriesView : PageView
{
    public MasteriesView() => InitializeComponent();

    private MasteriesViewModel ViewModel => (MasteriesViewModel)DataContext!;

    public override bool CanRefresh => true;

    public override Task RefreshAsync() => ViewModel.RefreshAsync();

    public override async void OnAppearing()
    {
        if (ViewModel.Tracks.Count == 0) await ViewModel.RefreshAsync();
    }
}
