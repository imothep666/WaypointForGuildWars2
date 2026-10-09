using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

using GuildWarsCompanion.Bridge.ViewModels;

namespace GuildWarsCompanion.Bridge.Views;

public partial class MainWindow : AppWindow
{
    public MainWindow() => AvaloniaXamlLoader.Load(this);

    /// <summary>
    /// The caption's X means what the system's X meant: with "keep running in the tray" on it
    /// puts the window away, and only Quit ends the process. <see cref="OnClosing"/> cannot
    /// make this decision for it, because that deliberately lets programmatic closes through.
    /// </summary>
    protected override void CloseFromCaption()
    {
        if (DataContext is MainViewModel { MinimiseToTray: true })
        {
            Hide();
            return;
        }

        Close();
    }

    /// <summary>
    /// The clipboard is a property of a TopLevel, so the view model cannot have it until there
    /// is a window — and the bridge does not start listening until it does. Accepting a paste
    /// it has nowhere to put would be worse than being briefly unreachable.
    /// </summary>
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        (DataContext as MainViewModel)?.Attach(Clipboard);
    }

    /// <summary>
    /// Opens the Linux MumbleLink instructions.
    ///
    /// Handled here rather than by a command, because it is nothing but a window: there is no
    /// state to hold and nothing for the view model to decide. Modal, so the instructions
    /// cannot end up behind the window they are about.
    /// </summary>
    private void OnMumbleSetup(object? sender, RoutedEventArgs e) =>
        new MumbleSetupWindow().ShowDialog(this);

    /// <summary>
    /// Closing hides rather than quits, when the user has asked for that.
    ///
    /// The window is not disposed, which is the point: the clipboard belongs to it, and on
    /// Windows and X11 the clipboard's contents belong to this process. Tearing the window down
    /// to a tray icon would leave the bridge listening with nowhere to put what arrives.
    /// </summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (DataContext is MainViewModel { MinimiseToTray: true } && !e.IsProgrammatic)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
    }
}
