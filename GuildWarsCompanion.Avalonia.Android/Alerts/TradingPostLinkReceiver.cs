using Android.App;
using Android.Content;
using GuildWarsCompanion.Host;
using GuildWarsCompanion.Services;

namespace GuildWarsCompanion.Platforms.Android;

/// <summary>
/// The SEND TO TP button on a price notification: the item's chat link to the PC, without opening
/// the app. The notification then says where the link went, in place of what it said before, so the
/// answer is where the tap was.
///
/// Not exported: only this app's own notification can send it.
/// </summary>
[BroadcastReceiver(Exported = false)]
public sealed class TradingPostLinkReceiver : BroadcastReceiver
{
    private const string NotificationExtra = "notification";
    private const string TitleExtra = "title";
    private const string RouteExtra = "route";
    private const string ItemExtra = "item";
    private const string NameExtra = "name";

    /// <summary>What the notification's button sends here.</summary>
    internal static PendingIntent Pending(
        Context context, int notification, string title, string? route, AndroidNotifier.TradingPostItem item)
    {
        var intent = new Intent(context, typeof(TradingPostLinkReceiver));
        intent.PutExtra(NotificationExtra, notification);
        intent.PutExtra(TitleExtra, title);
        intent.PutExtra(RouteExtra, route);
        intent.PutExtra(ItemExtra, item.Id);
        intent.PutExtra(NameExtra, item.Name);

        return PendingIntent.GetBroadcast(context, notification, intent,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;
    }

    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent is null) return;

        var notification = intent.GetIntExtra(NotificationExtra, 0);
        var title = intent.GetStringExtra(TitleExtra) ?? string.Empty;
        var route = intent.GetStringExtra(RouteExtra);
        var item = intent.GetIntExtra(ItemExtra, 0);
        var name = intent.GetStringExtra(NameExtra) ?? title;

        if (item <= 0) return;

        // The bridge is a network call; GoAsync keeps the process alive for it after this returns.
        var pending = GoAsync();

        _ = Task.Run(async () =>
        {
            try
            {
                // HostServices, as the reminder poll does: the tap can start a cold process.
                if (HostServices.Current?.GetService(typeof(TradingPostLink)) is not TradingPostLink link) return;

                // Inside the system's ten seconds for a receiver, with room to post the answer.
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                var said = await link.SendAsync(item, name, ct: deadline.Token);

                AndroidNotifier.Post(context, notification, title, said, route, null, quiet: true);
            }
            catch (Exception)
            {
                // A button in the shade has nowhere to show a crash; the notification stays as it was.
            }
            finally
            {
                pending?.Finish();
            }
        });
    }
}
