using Android.Content;
using AndroidX.Work;
using GuildWarsCompanion.Host;
using GuildWarsCompanion.Services.Alerts;
using Java.Util.Concurrent;

namespace GuildWarsCompanion.Platforms.Android.Background;

/// <summary>Schedules and cancels the periodic poll.</summary>
public sealed class AndroidWatchScheduler(ISettings settings) : IWatchScheduler
{
    private const string WorkName = "account-watch";
    private const string PreferenceKey = "alerts_enabled";

    /// <summary>
    /// WorkManager will not run a periodic job more often than every 15 minutes, and the
    /// commerce endpoints are cached for 5, so half an hour costs the user nothing in
    /// freshness and saves their battery.
    /// </summary>
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);

    public bool IsEnabled => settings.Get(PreferenceKey, false);

    public void Enable()
    {
        var constraints = new Constraints.Builder()
            .SetRequiredNetworkType(NetworkType.Connected!)!
            .Build();

        var request = new PeriodicWorkRequest.Builder(
                typeof(AccountWatchWorker), (long)Interval.TotalMinutes, TimeUnit.Minutes!)
            .SetConstraints(constraints!)!
            .Build();

        WorkManager.GetInstance(global::Android.App.Application.Context)
            .EnqueueUniquePeriodicWork(WorkName, ExistingPeriodicWorkPolicy.Keep!, (PeriodicWorkRequest)request);

        settings.Set(PreferenceKey, true);
    }

    public void Disable()
    {
        WorkManager.GetInstance(global::Android.App.Application.Context)
            .CancelUniqueWork(WorkName);

        settings.Set(PreferenceKey, false);
    }
}
