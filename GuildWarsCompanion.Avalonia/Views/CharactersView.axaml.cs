using Avalonia.Controls;
using Avalonia.Interactivity;

using GuildWarsCompanion.Ava.Shell;
using GuildWarsCompanion.Services;
using GuildWarsCompanion.ViewModels;

namespace GuildWarsCompanion.Ava.Views;

public partial class CharactersView : PageView
{
    public CharactersView() => InitializeComponent();

    private CharactersViewModel ViewModel => (CharactersViewModel)DataContext!;

    public override bool CanRefresh => true;

    public override Task RefreshAsync() => ViewModel.RefreshAsync();

    public override async void OnAppearing()
    {
        if (ViewModel.Characters.Count == 0) await ViewModel.RefreshAsync();
    }

    private void OnRowClicked(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is CharacterRow row) ViewModel.OpenCommand.Execute(row);
    }
}
