using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;
using GuildWarsCompanion.Ava.Droid;
using GuildWarsCompanion.Services.Alerts;

using R = _Microsoft.Android.Resource.Designer.Resource;

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
        var tradingPost = alert.TradingPostItem is { } item
            ? new TradingPostItem(item, alert.TradingPostName ?? alert.Title)
            : null;

        Post(global::Android.App.Application.Context, Interlocked.Increment(ref _nextId),
            alert.Title, alert.Body, alert.Route, tradingPost);

        return Task.CompletedTask;
    }

    /// <summary>The item a notification offers to send to the trading post's search.</summary>
    internal sealed record TradingPostItem(int Id, string Name);

    /// <summary>
    /// Posts, or replaces, notification <paramref name="id"/>. Replaced by the trading post
    /// button's receiver with where the link went, quietly and without the button.
    /// </summary>
    internal static void Post(
        Context context, int id, string title, string body, string? route, TradingPostItem? tradingPost,
        bool quiet = false)
    {
        var manager = NotificationManagerCompat.From(context);
        if (manager is null || !manager.AreNotificationsEnabled()) return;

        EnsureChannel(context);

        // Tapping the alert opens the app where the news is, or as it was left when the news has
        // no screen of its own. The id as the request code keeps each notification's route its
        // own: one shared code would hand every alert the last one's.
        var open = new Intent(context, typeof(MainActivity));
        open.AddFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop | ActivityFlags.SingleTop);
        if (route is not null) open.PutExtra(MainActivity.RouteExtra, route);

        var pending = PendingIntent.GetActivity(context, id, open,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

        // The builder's fluent setters are nullable-annotated in the binding; calling them
        // as statements keeps the chain out of the nullable analysis.
        var builder = new NotificationCompat.Builder(context, ChannelId);
        builder.SetContentTitle(title);
        builder.SetContentText(body);
        builder.SetStyle(new NotificationCompat.BigTextStyle().BigText(body));
        builder.SetSmallIcon(R.Drawable.notify_icon);
        builder.SetAutoCancel(true);
        builder.SetContentIntent(pending);
        builder.SetPriority(NotificationCompat.PriorityDefault);
        builder.SetOnlyAlertOnce(quiet);

        if (tradingPost is not null)
            builder.AddAction(0, L.T("SEND TO TP"),
                TradingPostLinkReceiver.Pending(context, id, title, route, tradingPost));

        manager.Notify(id, builder.Build());
    }

    private static void EnsureChannel(Context context)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26)) return;

        var channel = new NotificationChannel(ChannelId, L.T("Account reminders"), NotificationImportance.Default)
        {
            Description = L.T("Wizard's Vault rewards waiting to be claimed, and trades that closed.")
        };

        var manager = (NotificationManager?)context.GetSystemService(Context.NotificationService);
        manager?.CreateNotificationChannel(channel);
    }
}
