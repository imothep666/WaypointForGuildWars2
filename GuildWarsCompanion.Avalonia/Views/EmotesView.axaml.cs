using Avalonia.Controls;
using Avalonia.Interactivity;

using GuildWarsCompanion.Ava.Shell;
using GuildWarsCompanion.Services;
using GuildWarsCompanion.ViewModels;

namespace GuildWarsCompanion.Ava.Views;

public partial class EmotesView : PageView
{
    public EmotesView() => InitializeComponent();

    private EmotesViewModel ViewModel => (EmotesViewModel)DataContext!;

    public override bool CanRefresh => true;

    public override Task RefreshAsync() => ViewModel.RefreshAsync();

    public override async void OnAppearing()
    {
        // Before anything is shown: the outcome belongs to the visit that produced it.
        ViewModel.ClearOutcome();

        if (ViewModel.Emotes.Count == 0) await ViewModel.RefreshAsync();
    }

    private void OnEmoteClicked(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is EmoteRow row) ViewModel.CopyCommand.Execute(row);
    }
}
