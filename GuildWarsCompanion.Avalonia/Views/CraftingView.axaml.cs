using Avalonia.Controls;
using Avalonia.Interactivity;

using GuildWarsCompanion.Ava.Shell;
using GuildWarsCompanion.Host;
using GuildWarsCompanion.Services;
using GuildWarsCompanion.ViewModels;

using Microsoft.Extensions.DependencyInjection;

namespace GuildWarsCompanion.Ava.Views;

public partial class CraftingView : PageView
{
    public CraftingView() => InitializeComponent();

    private CraftingViewModel ViewModel => (CraftingViewModel)DataContext!;

    public override bool CanRefresh => true;

    public override Task RefreshAsync() => ViewModel.RefreshAsync();

    public override async void OnAppearing()
    {
        if (ViewModel.Disciplines.Count == 0) await ViewModel.RefreshAsync();
    }

    /// <summary>Picking a discipline moves the advice to it.</summary>
    private async void OnDisciplineClicked(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is DisciplineRow row) await ViewModel.ChooseAsync(row);
    }

    /// <summary>A suggestion is an item; open it to see the full recipe.</summary>
    private async void OnSuggestionClicked(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is LevellingRow row)
            await App.Services.GetRequiredService<INavigator>().GoToAsync($"item?id={row.ItemId}");
    }
}
