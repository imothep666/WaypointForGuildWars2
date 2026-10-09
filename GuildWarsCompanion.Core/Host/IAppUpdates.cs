using System.ComponentModel;

namespace GuildWarsCompanion.Host;

/// <summary>
/// The app updating itself, where it can. Only an installed desktop build can: a phone gets its
/// updates as an APK, and a desktop build run from a folder has nothing to update in place.
///
/// Raises PropertyChanged on the UI thread, so it can be bound: the title bar shows a button once
/// an update is ready, and Settings says what the updater last did.
/// </summary>
public interface IAppUpdates : INotifyPropertyChanged
{
    bool IsSupported { get; }

    /// <summary>What the updater is doing or last found, as a line for Settings.</summary>
    string Status { get; }

    /// <summary>Checking or downloading right now.</summary>
    bool IsBusy { get; }

    /// <summary>An update is downloaded and waits only for a restart.</summary>
    bool IsReady { get; }

    /// <summary>Starts the regular checks. Called once, when the window is up.</summary>
    void Start();

    /// <summary>Checks now, and downloads what it finds.</summary>
    Task CheckAsync();

    /// <summary>Closes the app, applies the downloaded update and starts the new version.</summary>
    void RestartToUpdate();
}

/// <summary>The phone's and any build that cannot update itself.</summary>
public sealed class NoAppUpdates : IAppUpdates
{
    public bool IsSupported => false;
    public string Status => string.Empty;
    public bool IsBusy => false;
    public bool IsReady => false;

    public event PropertyChangedEventHandler? PropertyChanged
    {
        add { }
        remove { }
    }

    public void Start() { }
    public Task CheckAsync() => Task.CompletedTask;
    public void RestartToUpdate() { }
}
