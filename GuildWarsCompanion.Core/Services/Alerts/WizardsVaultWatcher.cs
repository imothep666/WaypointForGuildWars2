using GuildWarsCompanion.Gw2;

namespace GuildWarsCompanion.Services.Alerts;

/// <summary>
/// Watches the three Wizard's Vault tracks for objectives that are finished but not yet
/// claimed — the reward sits in the vault until the player logs in and takes it, and a
/// daily one is lost at reset.
/// </summary>
public sealed class WizardsVaultWatcher(Gw2Client client) : IAccountWatcher
{
    public string Name => "wizardsvault";

    /// <summary>The vault panel marks a finished objective in the game.</summary>
    public bool GameSaysItToo => true;

    public IReadOnlyList<string> RequiredPermissions { get; } =
        [Gw2Permissions.Account, Gw2Permissions.Progression];

    public async Task<IReadOnlyList<AccountAlert>> CheckAsync(CancellationToken ct)
    {
        var alerts = new List<AccountAlert>();

        foreach (var (track, period) in new[]
                 {
                     ("daily", DailyPeriod()),
                     ("weekly", WeeklyPeriod()),
                     ("special", "season")
                 })
        {
            VaultTrack data;
            try
            {
                data = await client.GetVaultTrackAsync(track, ct);
            }
            catch (Gw2ApiException ex) when (ex.Kind is Gw2ErrorKind.MissingScope or Gw2ErrorKind.NotFound)
            {
                continue;
            }

            var label = char.ToUpperInvariant(track[0]) + track[1..];

            foreach (var objective in data.Objectives.Where(o => o.IsUnclaimedCompletion))
                alerts.Add(new AccountAlert(
                    // The period is part of the key: the same daily objective comes round
                    // again tomorrow and deserves to be announced again.
                    $"vault:{track}:{period}:{objective.Id}",
                    $"{label} objective complete",
                    $"{objective.Title} — {objective.Acclaim} Astral Acclaim waiting to be claimed."));

            if (data.IsMetaUnclaimed)
                alerts.Add(new AccountAlert(
                    $"vault:{track}:{period}:meta",
                    $"{label} meta reward ready",
                    $"{data.MetaRewardAstral} Astral Acclaim waiting in the Wizard's Vault."));
        }

        return alerts;
    }

    /// <summary>Dailies reset at 00:00 UTC.</summary>
    private static string DailyPeriod() => DateTime.UtcNow.ToString("yyyy-MM-dd");

    /// <summary>Weeklies reset Monday 07:30 UTC; shift the clock back so the period rolls then.</summary>
    private static string WeeklyPeriod()
    {
        var shifted = DateTime.UtcNow.AddMinutes(-450);
        var monday = shifted.Date.AddDays(-(((int)shifted.DayOfWeek + 6) % 7));
        return monday.ToString("yyyy-MM-dd");
    }
}
