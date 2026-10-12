using GuildWarsCompanion.Gw2;

namespace GuildWarsCompanion.Services;

/// <summary>One home instance row, ready to show.</summary>
public sealed record HomeRow(HomeThing Thing)
{
    public string Name => Thing.Name;

    public string Detail => Thing.Detail;

    public bool HasDetail => Thing.HasDetail;

    /// <summary>Brass for what is still to get; quiet for what is already in.</summary>
    public Color EdgeColor => Thing.Unlocked ? Theme.Banked : Theme.Brass;
}

/// <summary>The home instance, both halves of it.</summary>
public sealed record HomeView(
    string NodeSummary, IReadOnlyList<HomeRow> Nodes,
    string CatSummary, IReadOnlyList<HomeRow> Cats,
    string Note, bool HasAny);

/// <summary>
/// What the home instance holds and what it is still missing.
///
/// Four requests, all small, and none of them needs a name lookup: node ids are slugs and cats
/// carry their own hint. The catalogues change only with a game build, so they answer offline
/// once seen.
/// </summary>
public sealed class HomeService(Gw2Client client, ApiKeySession session, OfflineReader offline)
{
    public async Task<HomeView> GetAsync(CancellationToken ct = default)
    {
        // The account side needs all three scopes; the catalogues need none.
        var canRead = session.HasPermission(Gw2Permissions.Progression) &&
                      session.HasPermission(Gw2Permissions.Unlocks);

        var nodeIds = (await offline.ReadAsync(
            "home/nodes", Gw2JsonContext.Default.ListString, client.GetHomeNodesAsync, ct)).Value;

        var cats = (await offline.ReadAsync(
            "home/cats", Gw2JsonContext.Default.ListHomeCat, client.GetHomeCatsAsync, ct)).Value;

        var haveNodes = canRead
            ? await ReadAsync("account/home/nodes", Gw2JsonContext.Default.ListString,
                client.GetAccountHomeNodesAsync, ct)
            : null;

        var haveCats = canRead
            ? await ReadAsync("account/home/cats", Gw2JsonContext.Default.ListInt32,
                client.GetAccountHomeCatsAsync, ct)
            : null;

        var nodes = HomeInstance.Checklist(
                nodeIds.Select(id => (id, HomeInstance.NodeName(id), string.Empty)), haveNodes)
            .Select(thing => new HomeRow(thing))
            .ToList();

        var catRows = HomeInstance.Checklist(
                cats.Select(cat => (
                    cat.Id.ToString(),
                    HomeInstance.CatLure(cat.Hint),
                    L.T("leave this out to lure it"))),
                haveCats?.Select(id => id.ToString()))
            .Select(thing => new HomeRow(thing))
            .ToList();

        return new HomeView(
            HomeInstance.Summarise(nodeIds.Count, haveNodes?.Count, L.T("nodes")),
            nodes,
            HomeInstance.Summarise(cats.Count, haveCats?.Count, L.T("cats")),
            catRows,
            Note(canRead, haveNodes is not null),
            nodeIds.Count > 0);
    }

    /// <summary>
    /// The account half, which is allowed to be missing. A key without the scopes still gets
    /// the catalogues, and a list of everything there is to unlock is worth having on its own.
    /// </summary>
    private async Task<List<T>?> ReadAsync<T>(
        string key, System.Text.Json.Serialization.Metadata.JsonTypeInfo<List<T>> typeInfo,
        Func<CancellationToken, Task<List<T>>> fetch, CancellationToken ct)
    {
        try
        {
            return (await offline.ReadAsync(key, typeInfo, fetch, ct)).Value;
        }
        catch (Gw2ApiException)
        {
            return null;
        }
    }

    /// <summary>
    /// Three different states, and only one of them is a complete answer. A key that cannot
    /// read the account and a read that failed both leave the list looking untouched, so
    /// neither is allowed to pass as "you own none of this".
    /// </summary>
    private static string Note(bool canRead, bool known) =>
        !canRead
            ? L.T("Everything there is to collect. Which of it you already have needs an API key " +
                  "with the 'progression' and 'unlocks' permissions.")
            : !known
                ? L.T("Everything there is to collect. What you already have could not be read " +
                      "just now, so nothing here is marked.")
                : L.T("Missing first. Nodes are bought from vendors and karma merchants; cats are " +
                      "lured with the food listed, one at a time.");
}
