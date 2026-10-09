using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;
using GuildWarsCompanion.Services.Alerts;

namespace GuildWarsCompanion.Platforms.Android;

/// <summary>
/// Posts alerts to the Android notification shade.
///
/// Asking for the permission is handed in, because only the front end has the activity a
/// permission prompt has to come from: MainActivity.
/// </summary>
public sealed class AndroidNotifier(Func<Task<bool>> requestPermission) : INotifier
{
    private const string ChannelId = "account_alerts";

    /// <summary>Ids must differ or each notification would replace the last.</summary>
    private static int _nextId = 1000;

    public Task<bool> RequestPermissionAsync() => requestPermission();

    public Task NotifyAsync(AccountAlert alert, CancellationToken ct = default)
    {
        var context = global::Android.App.Application.Context;
        var manager = NotificationManagerCompat.From(context);
        if (manager is null || !manager.AreNotificationsEnabled()) return Task.CompletedTask;

        EnsureChannel(context);

        // Tapping the alert opens the app rather than dropping the player nowhere.
        var launch = context.PackageManager?.GetLaunchIntentForPackage(context.PackageName!);
        var pending = launch is null
            ? null
            : PendingIntent.GetActivity(context, 0, launch,
                PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

        // The builder's fluent setters are nullable-annotated in the binding; calling them
        // as statements keeps the chain out of the nullable analysis.
        var builder = new NotificationCompat.Builder(context, ChannelId);
        builder.SetContentTitle(alert.Title);
        builder.SetContentText(alert.Body);
        builder.SetStyle(new NotificationCompat.BigTextStyle().BigText(alert.Body));
        builder.SetSmallIcon(global::Android.Resource.Drawable.StatSysWarning);
        builder.SetAutoCancel(true);
        builder.SetContentIntent(pending);
        builder.SetPriority(NotificationCompat.PriorityDefault);

        manager.Notify(Interlocked.Increment(ref _nextId), builder.Build());
        return Task.CompletedTask;
    }

    private static void EnsureChannel(Context context)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26)) return;

        var channel = new NotificationChannel(ChannelId, "Account reminders", NotificationImportance.Default)
        {
            Description = "Wizard's Vault rewards waiting to be claimed, and trades that closed."
        };

        var manager = (NotificationManager?)context.GetSystemService(Context.NotificationService);
        manager?.CreateNotificationChannel(channel);
    }
}
