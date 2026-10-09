namespace GuildWarsCompanion.Services.Alerts;

/// <summary>
/// Tells you a map meta you asked about is about to start.
///
/// Needs no key at all: the schedule is the wiki's, and nothing about the account is read. The
/// game says nothing about a meta until you are on the map, which is exactly when the reminder is
/// no longer needed, so it is not held back while playing either.
/// </summary>
public sealed class MetaWatcher(MetaSource metas, MetaReminderStore chosen) : IAccountWatcher
{
    public string Name => "metas";

    public IReadOnlyList<string> RequiredPermissions { get; } = [];

    /// <summary>Appearances in the next half hour, never a backlog; see <see cref="WorldBossWatcher"/>.</summary>
    public bool NeedsBaseline => false;

    public async Task<IReadOnlyList<AccountAlert>> CheckAsync(CancellationToken ct)
    {
        // Nothing chosen is nothing to fetch: the wiki is not asked on behalf of a reminder nobody set.
        if (chosen.All.Count == 0) return [];

        return MetaAlerts
            .Due(await metas.GetAsync(ct), chosen.All, DateTimeOffset.UtcNow)
            .Select(call => new AccountAlert(call.Key, MetaAlerts.Title(call), MetaAlerts.Body(call)))
            .ToList();
    }
}
