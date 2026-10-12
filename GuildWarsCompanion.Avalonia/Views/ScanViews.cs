using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

using GuildWarsCompanion.Ava.Host;
using GuildWarsCompanion.Ava.Shell;
using GuildWarsCompanion.Bridge.Protocol;
using GuildWarsCompanion.Gw2;
using GuildWarsCompanion.Host;
using GuildWarsCompanion.ViewModels;

using Microsoft.Extensions.DependencyInjection;

namespace GuildWarsCompanion.Ava.Views;

/// <summary>
/// A route that runs the scanner once and goes back with what it read.
///
/// What was read is handed to Settings rather than acted on here, and Settings uses it as it
/// reappears: doing the work from the scanner raced with that page's own load, and the two
/// cancelled each other.
/// </summary>
public abstract class ScanPage : PageView
{
    private bool _started;

    protected ScanPage()
    {
        Content = new TextBlock
        {
            Text = L.T("Opening the scanner…"),
            Classes = { "caption" },
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(24, 40)
        };
    }

    protected abstract ScanRequest Request { get; }

    /// <summary>Hands an accepted payload to Settings.</summary>
    protected abstract void Deliver(SettingsViewModel settings, string payload);

    public override async void OnAppearing()
    {
        // Coming back to this page from the scanner must not open a second one.
        if (_started) return;
        _started = true;

        var services = App.Services;
        var payload = await services.GetRequiredService<IQrScanner>().ScanAsync(Request);

        if (payload is not null) Deliver(services.GetRequiredService<SettingsViewModel>(), payload);

        await services.GetRequiredService<INavigator>().GoToAsync("..");
    }
}

/// <summary>
/// The <c>scan</c> route: an API key off a QR code. Keys are 72 characters of hex — typing one on
/// a phone is the worst part of setting the app up.
/// </summary>
public sealed class ScanKeyView : ScanPage
{
    protected override ScanRequest Request { get; } = new(
        L.T("Point the camera at the QR code holding your API key."),
        Gw2ApiKey.LooksValid,
        L.T("That QR code does not hold a Guild Wars 2 API key. Try another."),
        L.T("Choose a picture of the QR code holding your API key"));

    protected override void Deliver(SettingsViewModel settings, string payload) =>
        settings.ScannedKey = payload.Trim();
}

/// <summary>
/// The <c>scanbridge</c> route: a PC's pairing off the QR code in the bridge window.
///
/// The QR is what makes the rest of the bridge work the way it does. A code short enough to read
/// across a room is short enough to attack offline, so it cannot prove which PC answered; a
/// scanned one carries 260 bits and can. The card carries no address on purpose: where the PC is
/// is the fact that goes stale.
/// </summary>
public sealed class ScanBridgeView : ScanPage
{
    protected override ScanRequest Request { get; } = new(
        L.T("Point the camera at the QR code in the bridge window."),
        payload => BridgeCard.TryParse(payload) is not null,
        L.T("That QR code is not a bridge pairing. Try the one in the bridge window."),
        L.T("Choose a picture of the QR code in the bridge window"));

    protected override void Deliver(SettingsViewModel settings, string payload) =>
        settings.ScannedCard = BridgeCard.TryParse(payload);
}
