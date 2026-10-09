using Avalonia.Controls;
using Avalonia.Interactivity;

using GuildWarsCompanion.Ava.Shell;
using GuildWarsCompanion.Services;
using GuildWarsCompanion.ViewModels;

namespace GuildWarsCompanion.Ava.Views;

public partial class AchievementsView : PageView
{
    public AchievementsView() => InitializeComponent();

    private AchievementsViewModel ViewModel => (AchievementsViewModel)DataContext!;

    public override bool CanRefresh => true;

    public override Task RefreshAsync() => ViewModel.RefreshAsync();

    public override async void OnAppearing()
    {
        if (ViewModel.Rows.Count == 0) await ViewModel.RefreshAsync();
    }

    private void OnRowClicked(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is AchievementRow row) ViewModel.OpenCommand.Execute(row);
    }
}
