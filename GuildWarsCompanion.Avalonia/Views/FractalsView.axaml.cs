using GuildWarsCompanion.Ava.Shell;
using GuildWarsCompanion.ViewModels;

namespace GuildWarsCompanion.Ava.Views;

public partial class FractalsView : PageView
{
    public FractalsView() => InitializeComponent();

    private FractalsViewModel ViewModel => (FractalsViewModel)DataContext!;

    public override bool CanRefresh => true;

    public override Task RefreshAsync() => ViewModel.RefreshAsync();

    /// <summary>Read every time: it is opened from Today's row, usually after a fractal was run.</summary>
    public override async void OnAppearing() => await ViewModel.RefreshAsync();
}
