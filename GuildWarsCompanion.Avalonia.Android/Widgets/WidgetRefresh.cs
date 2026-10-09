using Android.Appwidget;
using Android.Content;
using AndroidX.Work;
using GuildWarsCompanion.Host;
using GuildWarsCompanion.Services;
using Java.Util.Concurrent;

namespace GuildWarsCompanion.Platforms.Android.Widgets;

/// <summary>
/// The periodic redraw behind both widgets.
///
/// Separate from the alerts poll on purpose: someone can want a widget without wanting
/// notifications, and switching reminders off must not silently freeze their home screen.
/// </summary>
public static class WidgetRefresh
{
    private const string WorkName = "widget-refresh";

    /// <summary>
    /// A quarter of an hour, which is both WorkManager's floor and the beat the bosses keep —
    /// they appear on :00, :15, :30 and :45, so the named boss is never more than one period out
    /// of date. The countdown itself does not depend on this: the launcher ticks the Chronometer.
    ///
    /// This is the beat of the <em>redraw</em>, which is a SQLite read and some text. Whether it
    /// also costs a request is <see cref="WidgetService.RefreshAsync"/>'s decision, and mostly it
    /// does not — see the note there on why the two cadences must not be the same one.
    /// </summary>
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    public static void Enable()
    {
        var manager = WorkManager.GetInstance(global::Android.App.Application.Context);

        var request = new PeriodicWorkRequest.Builder(
                typeof(WidgetWorker), (long)Interval.TotalMinutes, TimeUnit.Minutes!)
            .Build();

        manager.EnqueueUniquePeriodicWork(
            WorkName, ExistingPeriodicWorkPolicy.Keep!, (PeriodicWorkRequest)request);

        // Periodic work is not promised a first run for up to a full period, and a widget that
        // spends its first quarter of an hour showing em dashes reads as broken rather than as
        // waiting. This fills it in as soon as the user has finished placing it.
        manager.EnqueueUniqueWork(
            FirstFillName, ExistingWorkPolicy.Replace!,
            (OneTimeWorkRequest)new OneTimeWorkRequest.Builder(typeof(WidgetWorker)).Build());
    }

    private const string FirstFillName = "widget-first-fill";

    /// <summary>
    /// Stops the work only when no widget is placed anywhere.
    ///
    /// OnDisabled fires per provider, so removing the boss widget while the vault one is still
    /// on a home screen would otherwise take the vault widget's refresh with it.
    /// </summary>
    public static void DisableIfUnused(Context? context)
    {
        if (context is null) return;

        if (AnyPlaced(context, typeof(BossWidget)) || AnyPlaced(context, typeof(VaultWidget)) ||
            AnyPlaced(context, typeof(HereWidget)))
            return;

        WorkManager.GetInstance(global::Android.App.Application.Context).CancelUniqueWork(WorkName);
    }

    private static bool AnyPlaced(Context context, Type provider)
    {
        var manager = AppWidgetManager.GetInstance(context);
        var ids = manager?.GetAppWidgetIds(
            new ComponentName(context, Java.Lang.Class.FromType(provider)));

        return ids is { Length: > 0 };
    }
}

/// <summary>
/// Refreshes what the widgets show, then redraws them.
///
/// Unconditionally successful. A widget that goes quiet because a poll failed is worse than one
/// showing figures with their age printed on them, which is what
/// <see cref="WidgetContent.Vault"/> falls back to.
/// </summary>
public sealed class WidgetWorker(Context context, WorkerParameters parameters)
    : Worker(context, parameters)
{
    public override Result DoWork()
    {
        if (HostServices.Get<WidgetService>() is { } widgets)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                widgets.RefreshAsync(timeout.Token).GetAwaiter().GetResult();
            }
            catch (Exception)
            {
                // Draw with whatever is stored rather than skipping the redraw: the boss
                // timetable is local and correct even when nothing could be fetched.
            }
        }

        WidgetDraw.UpdateAll(ApplicationContext!);

        return Result.InvokeSuccess()!;
    }
}
