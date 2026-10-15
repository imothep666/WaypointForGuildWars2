using Android.Content;

using AndroidX.Core.Content;

using GuildWarsCompanion.Ava.Host;
using GuildWarsCompanion.Ava.Shell;

namespace GuildWarsCompanion.Ava.Droid;

/// <summary>
/// The clipboard and the browser as every Avalonia platform does them, and Android's own share
/// sheet for a file.
///
/// The shared version opens a file with whatever the system has for it, which is a desktop's
/// answer; on Android a file path means nothing to another app. The log goes out instead as a
/// content:// URI from the FileProvider declared in the manifest, readable for this one share.
/// </summary>
public sealed class AndroidSystemActions(Shells shells) : AvaloniaSystemActions(shells)
{
    private const string Authority = "com.mpsoft.gw2companion.files";

    /// <summary>
    /// Android's own clipboard rather than the window's: a notification's button copies with no
    /// screen open, where the shared version has no window to ask and would build one off the UI
    /// thread.
    /// </summary>
    public override Task CopyAsync(string text)
    {
        var context = global::Android.App.Application.Context;

        if (context.GetSystemService(Context.ClipboardService) is ClipboardManager clipboard)
            clipboard.PrimaryClip = ClipData.NewPlainText(text, text);

        return Task.CompletedTask;
    }

    public override Task ShareFileAsync(string title, string path)
    {
        if (MainActivity.Current is not { } activity) return Task.CompletedTask;

        var uri = FileProvider.GetUriForFile(activity, Authority, new Java.IO.File(path));

        var send = new Intent(Intent.ActionSend);
        send.SetType("text/plain");
        send.PutExtra(Intent.ExtraStream, uri);
        send.PutExtra(Intent.ExtraSubject, title);
        send.AddFlags(ActivityFlags.GrantReadUriPermission);

        activity.StartActivity(Intent.CreateChooser(send, title));

        return Task.CompletedTask;
    }
}
