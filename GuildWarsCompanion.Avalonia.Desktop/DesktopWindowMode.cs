using CommunityToolkit.Mvvm.ComponentModel;

using GuildWarsCompanion.Host;

namespace GuildWarsCompanion.Ava.Desktop;

/// <summary>Persists fullscreen mode for the desktop companion window.</summary>
public sealed class DesktopWindowMode(ISettings settings) : ObservableObject, IWindowMode
{
    private const string FullScreenKey = "desktop.fullscreen";

    private Action<bool>? _applyFullScreen;
    private bool _fullScreen = settings.Get(FullScreenKey, false);

    public bool IsSupported => true;

    public bool FullScreen
    {
        get => _fullScreen;
        set
        {
            if (!SetProperty(ref _fullScreen, value)) return;

            settings.Set(FullScreenKey, value);
            _applyFullScreen?.Invoke(value);
        }
    }

    public void Attach(Action<bool> applyFullScreen)
    {
        _applyFullScreen = applyFullScreen;
        applyFullScreen(_fullScreen);
    }
}
