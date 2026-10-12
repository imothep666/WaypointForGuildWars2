using GuildWarsCompanion.Gw2;

namespace GuildWarsCompanion.Services;

/// <summary>What the account is, as opposed to what it owns.</summary>
public sealed record StandingView(
    IReadOnlyList<StandingRow> Rows, string Note, bool HasAny,
    IReadOnlyList<Owned> Expansions, string OwnedSummary);

/// <summary>
/// Account-wide standing: luck, fractal level, WvW rank, mastery points.
///
/// These apply to every character and appear nowhere else in the app. Two of them —
/// fractal level and WvW rank — were already arriving in the /v2/account response and being
/// discarded, which is the cheapest kind of missing feature.
/// </summary>
public sealed class StandingService(Gw2Client client, ApiKeySession session, OfflineReader offline, TestEvidence evidence)
{
    public async Task<StandingView> GetAsync(CancellationToken ct = default)
    {
        var rows = new List<StandingRow>();

        var account = (await offline.ReadAsync(
            "account", Gw2JsonContext.Default.Account, client.GetAccountAsync, ct)).Value;

        // The account's array has lacked it though the expansion is bought; see ROADMAP.md.
        if (account.Access.Contains("VisionsOfEternity", StringComparer.OrdinalIgnoreCase))
            evidence.Saw("voe-access");

        // Luck needs its own scopes; without them the rest of the screen still stands.
        if (session.HasPermission(Gw2Permissions.Progression) &&
            session.HasPermission(Gw2Permissions.Unlocks))
        {
            try
            {
                var luck = (await offline.ReadAsync(
                    "account/luck", Gw2JsonContext.Default.ListAccountLuck,
                    client.GetAccountLuckAsync, ct)).Value;

                rows.Add(AccountStanding.Luck(luck.Sum(entry => entry.Value)));
            }
            catch (Gw2ApiException)
            {
                // Nothing to say rather than a wrong number.
            }
        }

        if (AccountStanding.Fractals(account.FractalLevel) is { } fractals) rows.Add(fractals);
        if (AccountStanding.Wvw(account.WvwRank) is { } wvw) rows.Add(wvw);

        var (earned, spent) = await ReadMasteryAsync(ct);
        if (AccountStanding.Mastery(earned, spent) is { } points) rows.Add(points);

        return new StandingView(
            rows,
            L.T("Account-wide, so these apply to every character. Magic find is the base figure " +
                "luck alone buys — achievement points, food, boosters and guild bonuses stack on " +
                "top. The API carries no conversion for it, so the app ships the wiki's table."),
            rows.Count > 0,
            // Free with every account read, and discarded until now.
            Access.Expansions(account.Access),
            Access.Summarise(account.Access));
    }

    /// <summary>
    /// Points earned and spent. Both halves: the tally is a lifetime figure and what is left
    /// is the number that decides anything.
    /// </summary>
    private async Task<(int? Earned, int? Spent)> ReadMasteryAsync(CancellationToken ct)
    {
        if (!session.HasPermission(Gw2Permissions.Progression)) return (null, null);

        var points = await offline.ReadOptionalAsync(
            "account/mastery/points", Gw2JsonContext.Default.MasteryPoints,
            client.GetMasteryPointsAsync, ct);

        var totals = points.Value?.Totals;

        return totals is null
            ? (null, null)
            : (totals.Sum(total => total.Earned), totals.Sum(total => total.Spent));
    }
}
