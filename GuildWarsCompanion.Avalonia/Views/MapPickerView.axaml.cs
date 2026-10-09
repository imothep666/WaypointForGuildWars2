using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

using GuildWarsCompanion.Ava.Shell;
using GuildWarsCompanion.Services;
using GuildWarsCompanion.ViewModels;

namespace GuildWarsCompanion.Ava.Views;

/// <summary>The list of every map, by name. See <see cref="MapPickerViewModel"/>.</summary>
public partial class MapPickerView : PageView
{
    public MapPickerView() => InitializeComponent();

    private MapPickerViewModel ViewModel => (MapPickerViewModel)DataContext!;

    public override async void OnAppearing()
    {
        // Only the first time. The list is static data held for the run, and re-reading it on
        // every visit would throw away a search somebody is halfway through typing.
        if (ViewModel.Results.Count == 0) await ViewModel.LoadAsync();
    }

    /// <summary>
    /// Selects the whole term, after the focus has settled rather than during it: a tap moves the
    /// caret to where it landed as part of focusing, which would undo a selection made here.
    /// </summary>
    private void OnSearchFocused(object? sender, FocusChangedEventArgs e) =>
        Dispatcher.UIThread.Post(() => Search.SelectAll(), DispatcherPriority.Background);

    private void OnRowClicked(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is MapChoice choice) ViewModel.OpenCommand.Execute(choice);
    }
}
