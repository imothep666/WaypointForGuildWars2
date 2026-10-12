using GuildWarsCompanion.Data;
using GuildWarsCompanion.Gw2;

namespace GuildWarsCompanion.Services.Alerts;

/// <summary>
/// The words for the reminder before the daily reset, or none.
/// </summary>
public static class ResetReminder
{
    /// <summary>
    /// How long before the reset the reminder can come. An hour: long enough to log in and do a
    /// daily, and the background poll runs every thirty minutes, so at least one poll lands inside
    /// it.
    /// </summary>
    public static readonly TimeSpan Window = TimeSpan.FromHours(1);

    /// <summary>
    /// The reminder, or null when it is not the hour before the reset or nothing is left.
    /// </summary>
    /// <param name="untilReset">Time to the next 00:00 UTC.</param>
    /// <param name="vaultLeft">Wizard's Vault dailies not claimed; null when not read or not current.</param>
    /// <param name="craftsLeft">Time-gated crafts not done; null when not read.</param>
    /// <param name="day">The day the reset ends, for the key: one reminder per day, however many
    /// polls land in the hour.</param>
    public static AccountAlert? Describe(TimeSpan untilReset, int? vaultLeft, int? craftsLeft, DateOnly day)
    {
        if (untilReset <= TimeSpan.Zero || untilReset > Window) return null;

        var parts = new List<string>();

        if (vaultLeft is > 0 and var vault)
            parts.Add(vault == 1 ? L.T("1 Wizard's Vault daily left") : L.F("{0} Wizard's Vault dailies left", vault));

        if (craftsLeft is > 0 and var crafts)
            parts.Add(crafts == 1 ? L.T("1 time-gated craft not done") : L.F("{0} time-gated crafts not done", crafts));

        if (parts.Count == 0) return null;

        var minutes = Math.Max(1, (int)Math.Ceiling(untilReset.TotalMinutes));

        return new AccountAlert(
            $"reset:{day:yyyy-MM-dd}",
            L.F("Daily reset in {0} min", minutes),
            string.Join(" · ", parts) + ".");
    }
}

/// <summary>
/// A reminder in the hour before the daily reset, when today's dailies are still open.
///
/// Only the two that are lost at the reset and that most players chase every day: the Wizard's
/// Vault dailies and the time-gated crafts. World bosses and map chests are on Today for anyone who
/// wants them; listing every tracker in a notification is a notification nobody reads.
///
/// Reports only inside the hour and only once a day, so it needs no baseline: the first poll in the
/// window is the reminder.
/// </summary>
public sealed class ResetWatcher(Gw2Client client, SnapshotStore snapshots) : IAccountWatcher
{
    public string Name => "reset";

    public IReadOnlyList<string> RequiredPermissions { get; } =
        [Gw2Permissions.Account, Gw2Permissions.Progression];

    public bool NeedsBaseline => false;

    public async Task<IReadOnlyList<AccountAlert>> CheckAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var reset = Resets.NextDaily(now);

        // Outside the hour nothing is asked of the API at all.
        if (reset - now > ResetReminder.Window) return [];

        var alert = ResetReminder.Describe(
            reset - now, await VaultLeftAsync(now, ct), await CraftsLeftAsync(ct),
            DateOnly.FromDateTime(reset.UtcDateTime));

        return alert is null ? [] : [alert];
    }

    /// <summary>
    /// Dailies not yet claimed — unless the vault still shows yesterday's. The vault endpoint only
    /// rolls over once the player logs in, and "3 dailies left" about a list that has already
    /// expired would be wrong in the one way that matters. The same check Today makes.
    /// </summary>
    private async Task<int?> VaultLeftAsync(DateTimeOffset now, CancellationToken ct)
    {
        try
        {
            var vault = await client.GetVaultTrackAsync("daily", ct);

            var changedAt = await snapshots.RecordAsync("vault/daily", vault, Gw2JsonContext.Default.VaultTrack, ct);

            if (changedAt < Resets.LastDaily(now)) return null;

            return vault.Objectives.Count(o => !o.Claimed);
        }
        catch (Gw2ApiException)
        {
            return null;
        }
    }

    private async Task<int?> CraftsLeftAsync(CancellationToken ct)
    {
        try
        {
            var all = await client.GetDailyCraftingIdsAsync(ct);
            var done = await client.GetCompletedDailyCraftingAsync(ct);

            return ResetProgress.Of(all, done).Remaining.Count;
        }
        catch (Gw2ApiException)
        {
            return null;
        }
    }
}
