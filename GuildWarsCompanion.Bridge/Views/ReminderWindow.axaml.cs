using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

using GuildWarsCompanion.Bridge.Protocol;

namespace GuildWarsCompanion.Bridge.Views;

/// <summary>
/// The panel that tells the player a world boss is close, on the screen they are looking at.
///
/// Sized to its text, pinned above the game, and gone again in a few seconds. It never takes
/// focus — see the note in the markup — because the one thing worse than missing a boss is
/// swallowing a dodge.
/// </summary>
public partial class ReminderWindow : Window
{
    /// <summary>
    /// How long it stays.
    ///
    /// Twelve seconds: long enough to be read by somebody whose attention is elsewhere, short
    /// enough not to sit over the fight it is announcing. It also goes on a click, which is what
    /// anybody tries first.
    /// </summary>
    private static readonly TimeSpan Showing = TimeSpan.FromSeconds(12);

    private readonly DispatcherTimer _closing;

    public ReminderWindow()
    {
        InitializeComponent();

        _closing = new DispatcherTimer { Interval = Showing };
        _closing.Tick += (_, _) => Close();
        _closing.Start();

        PointerPressed += (_, _) => Close();
    }

    public ReminderWindow(BossDue due) : this()
    {
        DataContext = due;
    }

    /// <summary>
    /// Bottom right of the screen the pointer is on, above the taskbar.
    ///
    /// Where every notification on this platform appears, so it is where the eye already goes.
    /// Done by hand because a borderless window has no manager placing it, and in physical pixels
    /// because <see cref="Screen"/> works in those while the window is measured in the scaled
    /// units Avalonia lays out with.
    /// </summary>
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        if (Screens.Primary is not { } screen) return;

        var area = screen.WorkingArea;
        var scale = RenderScaling;

        Position = new Avalonia.PixelPoint(
            area.X + area.Width - (int)(Width * scale) - 24,
            area.Y + area.Height - (int)(Bounds.Height * scale) - 24);
    }

    protected override void OnClosed(EventArgs e)
    {
        _closing.Stop();

        base.OnClosed(e);
    }
}
