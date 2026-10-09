using Avalonia.Controls;
using Avalonia.Interactivity;

using GuildWarsCompanion.Ava.Shell;
using GuildWarsCompanion.Ava.Ui;
using GuildWarsCompanion.Services;
using GuildWarsCompanion.ViewModels;

namespace GuildWarsCompanion.Ava.Views;

public partial class MaterialsView : PageView
{
    private FlatGroups? _flat;

    public MaterialsView() => InitializeComponent();

    private MaterialsViewModel ViewModel => (MaterialsViewModel)DataContext!;

    public override bool CanRefresh => true;

    public override Task RefreshAsync() => ViewModel.RefreshAsync();

    public override async void OnAppearing()
    {
        if (_flat is null)
        {
            _flat = new FlatGroups(ViewModel.Sections);
            List.ItemsSource = _flat.Items;
        }

        if (ViewModel.Sections.Count == 0) await ViewModel.RefreshAsync();
    }

    private void OnRowClicked(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is MaterialRow row) ViewModel.OpenCommand.Execute(row);
    }
}
