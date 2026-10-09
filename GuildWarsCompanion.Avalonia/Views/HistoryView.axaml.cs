using GuildWarsCompanion.Ava.Shell;
using GuildWarsCompanion.ViewModels;

namespace GuildWarsCompanion.Ava.Views;

public partial class HistoryView : PageView
{
    public HistoryView() => InitializeComponent();

    private HistoryViewModel ViewModel => (HistoryViewModel)DataContext!;

    public override bool CanRefresh => true;

    public override Task RefreshAsync() => ViewModel.RefreshAsync();

    public override async void OnAppearing()
    {
        if (ViewModel.Sections.Count == 0) await ViewModel.RefreshAsync();
    }
}
