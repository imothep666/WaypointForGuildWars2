using Avalonia.Controls;
using Avalonia.Interactivity;

using GuildWarsCompanion.Ava.Host;
using GuildWarsCompanion.Ava.Shell;
using GuildWarsCompanion.Services;
using GuildWarsCompanion.ViewModels;

using Microsoft.Extensions.DependencyInjection;

namespace GuildWarsCompanion.Ava.Views;

public partial class SettingsView : PageView
{
    public SettingsView()
    {
        InitializeComponent();

        var camera = App.Services.GetRequiredService<IQrScanner>().UsesCamera;

        ScanButton.Content = camera ? L.T("SCAN QR") : L.T("QR FROM IMAGE");
        ScanBridgeButton.Content = camera ? L.T("SCAN A PC") : L.T("PC FROM IMAGE");
    }

    private SettingsViewModel ViewModel => (SettingsViewModel)DataContext!;

    public override async void OnAppearing() => await ViewModel.LoadAsync();

    private void OnTabSelected(PageTab tab) => ViewModel.Select(tab);

    /// <summary>
    /// A pack row's UPDATE. The handler resolves the row from the button's own DataContext, which
    /// cannot miss — unlike a command reached out of a template.
    /// </summary>
    private async void OnUpdatePack(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is MarkerPackRow row) await ViewModel.UpdateMarkerPackAsync(row);
    }

    private void OnRemovePack(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is MarkerPackRow row) ViewModel.RemoveMarkerPack(row);
    }

    private void OnForgetBridge(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is KnownBridge bridge) ViewModel.ForgetBridgeCommand.Execute(bridge);
    }
}
