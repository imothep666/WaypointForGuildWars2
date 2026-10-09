using System.Diagnostics;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace GuildWarsCompanion.Bridge.Views;

/// <summary>
/// How to get MumbleLink out of the Wine prefix, for somebody who has just found out that the
/// game bead stays red on Linux until they do.
///
/// No view model: there is no state here and nothing to poll. The confirmation this window
/// leads up to happens on the main window, where the bead is.
/// </summary>
public partial class MumbleSetupWindow : AppWindow
{
    private const string JokolayUrl = "https://github.com/coderedart/jokolay";

    public MumbleSetupWindow() => AvaloniaXamlLoader.Load(this);

    /// <summary>
    /// Hands the URL to the desktop, and says nothing if that fails: the address is on screen
    /// as selectable text either way, which is why this can afford to be a convenience.
    /// </summary>
    private void OnOpenJokolay(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo(JokolayUrl) { UseShellExecute = true });
            else
                Process.Start("xdg-open", JokolayUrl);
        }
        catch (Exception)
        {
            // Nothing to report to: this window has no error line, on purpose.
        }
    }
}
