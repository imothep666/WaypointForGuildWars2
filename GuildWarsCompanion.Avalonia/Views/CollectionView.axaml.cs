using Avalonia.Controls;
using Avalonia.Interactivity;

using GuildWarsCompanion.Ava.Shell;
using GuildWarsCompanion.Services;
using GuildWarsCompanion.ViewModels;

namespace GuildWarsCompanion.Ava.Views;

public partial class CollectionView : PageView
{
    public CollectionView() => InitializeComponent();

    private CollectionViewModel ViewModel => (CollectionViewModel)DataContext!;

    public override bool CanRefresh => true;

    public override Task RefreshAsync() => ViewModel.LoadAsync();

    private void OnRowClicked(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is CollectionRow row) ViewModel.OpenCommand.Execute(row);
    }
}
