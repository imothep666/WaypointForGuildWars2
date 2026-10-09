using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

using GuildWarsCompanion.Ava.Shell;
using GuildWarsCompanion.Services;
using GuildWarsCompanion.ViewModels;

namespace GuildWarsCompanion.Ava.Views;

public partial class FindView : PageView
{
    public FindView() => InitializeComponent();

    private FindViewModel ViewModel => (FindViewModel)DataContext!;

    public override bool CanRefresh => true;

    public override Task RefreshAsync() => ViewModel.RefreshAsync();

    public override async void OnAppearing()
    {
        // Reading every storage place is expensive, so it happens once and then the search
        // runs against what is already here.
        if (!ViewModel.IsLoaded) await ViewModel.RefreshAsync();
    }

    /// <summary>The keyboard's return key searches, as MAUI's Entry ReturnCommand does.</summary>
    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;

        ViewModel.FindCommand.Execute(null);
        e.Handled = true;
    }

    private void OnItemClicked(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is FoundItem item) ViewModel.OpenCommand.Execute(item);
    }
}
