using System.ComponentModel;

namespace GuildWarsCompanion.Host;

/// <summary>
/// Controls window modes that a platform may or may not support. Raises PropertyChanged for
/// <see cref="FullScreen"/>, because more than the settings switch can change it: F11, or the
/// system taking the window out of fullscreen.
/// </summary>
public interface IWindowMode : INotifyPropertyChanged
{
    bool IsSupported { get; }

    bool FullScreen { get; set; }

    void Attach(Action<bool> applyFullScreen);
}

/// <summary>The mobile/default implementation, where the app does not own a desktop window.</summary>
public sealed class NoWindowMode : IWindowMode
{
    public bool IsSupported => false;

    public bool FullScreen
    {
        get => false;
        set { }
    }

    public event PropertyChangedEventHandler? PropertyChanged
    {
        add { }
        remove { }
    }

    public void Attach(Action<bool> applyFullScreen) { }
}
