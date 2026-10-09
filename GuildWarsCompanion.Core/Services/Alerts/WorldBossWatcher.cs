using GuildWarsCompanion.Gw2;

namespace GuildWarsCompanion.Services.Alerts;

/// <summary>
/// Tells you a world boss you still need is about to appear.
///
/// This is the one alert the game itself cannot give you: it knows the timetable and it knows
/// what you have killed today, but only while you are logged in and looking. The timetable is
/// hardcoded because the API has no event schedule at all — see <see cref="EventSchedule"/>.
/// </summary>
public sealed class WorldBossWatcher(Gw2Client client) : IAccountWatcher
{
    public string Name => "worldbosses";

    /// <summary>Reading what has already been killed today needs "progression".</summary>
    public IReadOnlyList<string> RequiredPermissions { get; } = [Gw2Permissions.Progression];

    /// <summary>
    /// No baseline. This reports appearances in the next half hour, never a backlog, so
    /// swallowing the first run would just lose the first boss for no reason.
    /// </summary>
    public bool NeedsBaseline => false;

    public async Task<IReadOnlyList<AccountAlert>> CheckAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        // The schedule is the universe of bosses that can be timed, so what is left to do is
        // simply the ones the account has not reported killing.
        var completed = (await client.GetCompletedWorldBossesAsync(ct)).ToHashSet();

        var appearances = EventSchedule.NextAppearances(now);
        var remaining = appearances
            .Select(boss => boss.ApiId)
            .Where(id => !completed.Contains(id))
            .ToHashSet();

        return BossAlerts
            .Due(appearances, remaining, now)
            .Select(call => new AccountAlert(call.Key, BossAlerts.Title(call), BossAlerts.Body(call)))
            .ToList();
    }
}
