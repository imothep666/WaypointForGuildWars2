using Avalonia.Controls;
using Avalonia.Interactivity;

using GuildWarsCompanion.Ava.Shell;
using GuildWarsCompanion.Host;
using GuildWarsCompanion.Services;
using GuildWarsCompanion.ViewModels;

using Microsoft.Extensions.DependencyInjection;

namespace GuildWarsCompanion.Ava.Views;

public partial class ItemView : PageView
{
    public ItemView() => InitializeComponent();

    private ItemViewModel ViewModel => (ItemViewModel)DataContext!;

    private static INavigator Navigator => App.Services.GetRequiredService<INavigator>();

    public override bool CanRefresh => true;

    public override Task RefreshAsync() => ViewModel.LoadAsync();

    /// <summary>
    /// An ingredient is an item like any other, so it opens the same screen — which lets you walk
    /// a recipe down to its materials one tap at a time.
    /// </summary>
    private async void OnIngredientClicked(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is CraftLineView line)
            await Navigator.GoToAsync($"item?id={line.ItemId}");
    }

    /// <summary>And the same walk in the other direction: from a material up to what it becomes.</summary>
    private async void OnUsedInClicked(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is UsedInItemRow row)
            await Navigator.GoToAsync($"item?id={row.ItemId}");
    }
}
