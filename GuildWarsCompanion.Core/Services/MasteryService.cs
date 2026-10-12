using GuildWarsCompanion.Gw2;

namespace GuildWarsCompanion.Services;

/// <summary>One track as the screen shows it.</summary>
public sealed record MasteryRow(MasteryStep Step)
{
    public string Name => Step.Name;

    /// <summary>For the screen; <see cref="MasteryStep.Region"/> stays the English join key.</summary>
    public string Region => Masteries.RegionName(Step.Region);

    public string ProgressText => Step.ProgressText;

    public string Detail => Masteries.Describe(Step);

    /// <summary>Brass for a step that can be taken now; quiet for finished and for
    /// out of reach, which are equally not actionable today.</summary>
    public Color EdgeColor => Step.Affordable ? Theme.Brass : Theme.Banked;
}

/// <summary>What the masteries screen shows.</summary>
public sealed record MasteryView(
    string Summary, IReadOnlyList<RegionPoints> Regions,
    IReadOnlyList<MasteryRow> Tracks, string Note, bool HasAny);

/// <summary>
/// Mastery tracks and what the account can still train.
///
/// The Standing screen has always shown a mastery point total and never said what it buys.
/// Three requests answer that: the track catalogue, which changes only with a game build, and
/// the two account halves.
/// </summary>
public sealed class MasteryService(Gw2Client client, ApiKeySession session, OfflineReader offline)
{
    public async Task<MasteryView> GetAsync(CancellationToken ct = default)
    {
        var tracks = (await offline.ReadAsync(
            "masteries", Gw2JsonContext.Default.ListMastery, client.GetMasteriesAsync, ct)).Value;

        if (!session.HasPermission(Gw2Permissions.Progression))
            return new MasteryView(string.Empty, [], [], L.T(NeedsProgression), false);

        var points = (await offline.ReadOptionalAsync(
            "account/mastery/points", Gw2JsonContext.Default.MasteryPoints,
            client.GetMasteryPointsAsync, ct)).Value;

        var trained = (await offline.ReadAsync(
                "account/masteries", Gw2JsonContext.Default.ListAccountMastery,
                client.GetAccountMasteriesAsync, ct))
            .Value.ToDictionary(entry => entry.Id, entry => entry.Level);

        var regions = (points?.Totals ?? [])
            .Select(total => new RegionPoints(total.Region, total.Earned, total.Spent))
            .OrderByDescending(region => region.Available)
            .ThenBy(region => region.Region, StringComparer.CurrentCulture)
            .ToList();

        var available = regions.ToDictionary(region => region.Region, region => region.Available);

        var steps = Masteries.Plan(
            tracks.Select(track => (
                track.Id,
                track.Name,
                track.Region,
                (IReadOnlyList<(string, int)>)(track.Levels ?? [])
                    .Select(level => (level.Name, level.PointCost)).ToList())),
            trained,
            available);

        return new MasteryView(
            Masteries.Summarise(regions),
            regions,
            steps.Select(step => new MasteryRow(step)).ToList(),
            L.T(Note),
            steps.Count > 0);
    }

    private const string Note =
        "Points are earned and spent per region, so Central Tyria points cannot train a " +
        "Maguuma track — what you can afford is judged against the region the track belongs " +
        "to. Tracks you can take the next level of come first.";

    private const string NeedsProgression =
        "Mastery progress needs an API key with the 'progression' permission.";
}
