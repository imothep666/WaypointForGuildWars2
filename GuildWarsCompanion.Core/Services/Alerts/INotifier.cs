namespace GuildWarsCompanion.Services.Alerts;

/// <summary>Puts an alert in front of the player. Platform-specific behind this seam.</summary>
public interface INotifier
{
    /// <summary>Android 13 and later require the user to grant notifications explicitly.</summary>
    Task<bool> RequestPermissionAsync();

    Task NotifyAsync(AccountAlert alert, CancellationToken ct = default);
}

/// <summary>
/// Turns the background poll on and off. Guild Wars 2 has no push, so "notifications" means
/// a periodic job that wakes up, calls the API and posts what it finds.
/// </summary>
public interface IWatchScheduler
{
    bool IsEnabled { get; }

    void Enable();

    void Disable();
}
