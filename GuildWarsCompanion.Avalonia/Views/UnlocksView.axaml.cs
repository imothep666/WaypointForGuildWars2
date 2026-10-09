using Avalonia.Controls;
using Avalonia.Interactivity;

using GuildWarsCompanion.Ava.Shell;
using GuildWarsCompanion.ViewModels;

namespace GuildWarsCompanion.Ava.Views;

public partial class UnlocksView : PageView
{
    public UnlocksView() => InitializeComponent();

    private UnlocksViewModel ViewModel => (UnlocksViewModel)DataContext!;

    public override bool CanRefresh => true;

    public override Task RefreshAsync() => ViewModel.RefreshAsync();

    public override async void OnAppearing()
    {
        if (ViewModel.Categories.Count == 0) await ViewModel.RefreshAsync();
    }

    private async void OnCategoryClicked(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is UnlockCategoryRow row) await ViewModel.OpenAsync(row);
    }
}
