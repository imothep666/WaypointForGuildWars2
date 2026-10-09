namespace GuildWarsCompanion.Ava.Shell;

/// <summary>
/// The shell on screen, and a new one whenever Android builds a new activity.
///
/// The shell used to be a single control for the life of the process. Android can build a new
/// activity while the process lives on — a configuration change the manifest does not claim, the
/// system reclaiming a backgrounded activity, a launch that asks for a fresh task — and the new
/// activity was handed the same control, still the content of the old one. Avalonia refuses a
/// control with two parents ("MainView already has a visual parent", twice in the crash log on
/// 2026-10-02), and taking it off the old activity first only moved the crash to the next layout
/// pass ("InvalidateArrange on wrong LayoutManager"): the old window's layout queue still held
/// parts of it. A control belongs to one window, so each window gets its own. Reproduced and
/// checked with <c>am start -f 0x10008000</c>, which finishes the activity and starts another in
/// the same process.
///
/// Whatever needs the window — the clipboard, the launcher, a file picker — asks for
/// <see cref="Current"/> at the moment it needs it, never keeps one.
/// </summary>
public sealed class Shells(Router router)
{
    private MainView? _current;

    public MainView Current => _current ??= new MainView(router);

    /// <summary>A shell for a new window: the first one, or a rebuilt one with fresh pages.</summary>
    public MainView Fresh()
    {
        if (_current is null) return Current;

        _current.Release();
        _current = new MainView(router);

        router.Rebuild();

        return _current;
    }
}
