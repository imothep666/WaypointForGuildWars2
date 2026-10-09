namespace GuildWarsCompanion.Ava.Host;

/// <summary>
/// Keeps the screen from dimming and sleeping while one page is on top.
///
/// For the compass: the one screen in the app that is read rather than operated. It is propped
/// against something while both hands are on the game, so the ordinary rule — the screen sleeps
/// when nobody touches it — is exactly wrong there. A window flag rather than a device setting,
/// so it lasts as long as it is held and needs no permission.
/// </summary>
public interface IKeepAwake
{
    bool KeepScreenOn { get; set; }
}

/// <summary>For a desktop, where nothing sleeps under an open window the way a phone does.</summary>
public sealed class NoKeepAwake : IKeepAwake
{
    public bool KeepScreenOn { get; set; }
}
