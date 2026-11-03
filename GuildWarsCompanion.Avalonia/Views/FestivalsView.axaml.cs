using GuildWarsCompanion.Ava.Shell;
using GuildWarsCompanion.ViewModels;

namespace GuildWarsCompanion.Ava.Views;

public partial class FestivalsView : PageView
{
    public FestivalsView() => InitializeComponent();

    private FestivalsViewModel ViewModel => (FestivalsViewModel)DataContext!;

    /// <summary>Every time: "in 12 days" is a day older on each visit.</summary>
    public override async void OnAppearing() => await ViewModel.RefreshAsync();
}
