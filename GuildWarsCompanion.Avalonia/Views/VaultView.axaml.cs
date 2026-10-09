using GuildWarsCompanion.Ava.Shell;
using GuildWarsCompanion.ViewModels;

namespace GuildWarsCompanion.Ava.Views;

public partial class VaultView : PageView
{
    public VaultView() => InitializeComponent();

    private VaultViewModel ViewModel => (VaultViewModel)DataContext!;

    public override bool CanRefresh => true;

    public override Task RefreshAsync() => ViewModel.RefreshAsync();

    public override async void OnAppearing()
    {
        if (ViewModel.Sections.Count == 0) await ViewModel.RefreshAsync();
    }

    /// <summary>
    /// Both tabs share one scroller, so the shop opened wherever the objectives had been left —
    /// partway down a list it had never shown. Each tab starts at its top.
    /// </summary>
    private void OnTabSelected(PageTab tab)
    {
        ViewModel.Select(tab);
        Scroller.ScrollToHome();
    }
}
