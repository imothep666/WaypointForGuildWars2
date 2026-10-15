using Avalonia.Controls;
using Avalonia.Interactivity;

using GuildWarsCompanion.Ava.Shell;
using GuildWarsCompanion.Services;
using GuildWarsCompanion.ViewModels;

namespace GuildWarsCompanion.Ava.Views;

public partial class WatchView : PageView
{
    /// <summary>The watch the open question is about, or null when none is open.</summary>
    private WatchRow? _confirming;

    public WatchView() => InitializeComponent();

    private WatchViewModel ViewModel => (WatchViewModel)DataContext!;

    public override bool CanRefresh => true;

    public override Task RefreshAsync() => ViewModel.RefreshAsync();

    public override async void OnAppearing() => await ViewModel.RefreshAsync();

    public override void OnDisappearing() => CloseConfirm();

    private void OnCandidateClicked(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is WatchCandidate item) ViewModel.Choose(item);
    }

    /// <summary>Confirms before deleting: a tap on a list is easy to make by accident.</summary>
    private void OnWatchClicked(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not WatchRow row) return;

        _confirming = row;
        ConfirmTitle.Text = row.Name;
        ConfirmMessage.Text = $"{row.Condition}\n\n{L.T("Stop watching this?")}";
        ConfirmLayer.IsVisible = true;
    }

    /// <summary>Inside the row's own button, so it marks the click handled: no deletion asked.</summary>
    private async void OnSendClicked(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;

        if ((sender as Control)?.DataContext is WatchRow row) await ViewModel.SendToTradingPostAsync(row);
    }

    private async void OnStopClicked(object? sender, RoutedEventArgs e)
    {
        var row = _confirming;
        CloseConfirm();

        if (row is not null) await ViewModel.RemoveAsync(row);
    }

    private void OnKeepClicked(object? sender, RoutedEventArgs e) => CloseConfirm();

    private void CloseConfirm()
    {
        _confirming = null;
        ConfirmLayer.IsVisible = false;
    }
}
