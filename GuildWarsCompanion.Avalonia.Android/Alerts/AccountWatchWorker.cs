using Android.Content;
using AndroidX.Work;
using GuildWarsCompanion.Host;
using GuildWarsCompanion.Services.Alerts;

namespace GuildWarsCompanion.Platforms.Android.Background;

/// <summary>
/// The periodic poll. WorkManager runs this off the main thread, survives reboots and
/// respects Doze, which is as close to "push" as the Guild Wars 2 API allows.
/// </summary>
public sealed class AccountWatchWorker(Context context, WorkerParameters parameters)
    : Worker(context, parameters)
{
    public override Result DoWork()
    {
        // HostServices rather than the activity's container: a poll can start a cold process
        // with no screen at all.
        var services = HostServices.Current;
        if (services is null) return Result.InvokeRetry()!;

        var alerts = services.GetService(typeof(AlertService)) as AlertService;
        if (alerts is null) return Result.InvokeFailure()!;

        try
        {
            // Already on a background thread; WorkManager expects DoWork to block.
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            alerts.CheckAndNotifyAsync(timeout.Token).GetAwaiter().GetResult();
            return Result.InvokeSuccess()!;
        }
        catch (OperationCanceledException)
        {
            return Result.InvokeRetry()!;
        }
        catch (Exception)
        {
            // Never crash the worker: a failed poll just waits for the next window.
            return Result.InvokeRetry()!;
        }
    }
}
