using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

using GuildWarsCompanion.Ava.Shell;
using GuildWarsCompanion.Host;

using Microsoft.Maui.Graphics.Skia;

using IImage = Microsoft.Maui.Graphics.IImage;

namespace GuildWarsCompanion.Ava.Host;

// The answers to Core's Host\ interfaces that are the same on every Avalonia platform. A head
// that can do better registers its own first; App only fills in what is still missing.

public sealed class AvaloniaMainThread : IMainThread
{
    public void BeginInvokeOnMainThread(Action action) => Dispatcher.UIThread.Post(action);

    public Task InvokeOnMainThreadAsync(Action action) =>
        Dispatcher.UIThread.InvokeAsync(action).GetTask();

    public Task InvokeOnMainThreadAsync(Func<Task> action) => Dispatcher.UIThread.InvokeAsync(action);
}

/// <summary>
/// The clipboard and the launcher, through whichever window the shell is in.
///
/// Sharing a file is a desktop answer here: the file is opened with whatever the system has for
/// it. The Android head replaces this with the share sheet.
/// </summary>
public class AvaloniaSystemActions(Shells shells) : ISystemActions
{
    protected TopLevel Top =>
        TopLevel.GetTopLevel(shells.Current) ?? throw new InvalidOperationException("The shell is not on screen.");

    public virtual async Task ShareFileAsync(string title, string path) =>
        await Top.Launcher.LaunchFileInfoAsync(new FileInfo(path));

    public async Task CopyAsync(string text)
    {
        if (Top.Clipboard is { } clipboard) await clipboard.SetTextAsync(text);
    }

    public async Task OpenLinkAsync(string url) => await Top.Launcher.LaunchUriAsync(new Uri(url));
}

/// <summary>Decodes into a Skia bitmap, the same library Avalonia renders with.</summary>
public sealed class SkiaImageDecoder : IImageDecoder
{
    public IImage? Decode(Stream stream) => SkiaImage.FromStream(stream);
}
