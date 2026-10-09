namespace GuildWarsCompanion.Services.Alerts;

/// <summary>
/// Stand-ins for platforms that have no implementation yet. The app is Android-only today;
/// these keep a future iOS or Windows head from failing to start before its own notifier
/// exists.
/// </summary>
public sealed class NullNotifier : INotifier
{
    public Task<bool> RequestPermissionAsync() => Task.FromResult(false);

    public Task NotifyAsync(AccountAlert alert, CancellationToken ct = default) => Task.CompletedTask;
}

public sealed class NullWatchScheduler : IWatchScheduler
{
    public bool IsEnabled => false;

    public void Enable() { }

    public void Disable() { }
}
