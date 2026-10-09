using GuildWarsCompanion.Data;
using GuildWarsCompanion.Gw2;

namespace GuildWarsCompanion.Services;

/// <summary>One objective, ready to show.</summary>
public sealed record VaultRow(
    string Title, int Acclaim, int ProgressCurrent, int ProgressComplete, bool Claimed)
{
    public bool IsComplete => ProgressCurrent >= ProgressComplete;

    /// <summary>Finished, reward still in the vault. This is what the reminder is about.</summary>
    public bool IsUnclaimed => IsComplete && !Claimed;

    public string ProgressText => $"{Math.Min(ProgressCurrent, ProgressComplete)}/{ProgressComplete}";

    /// <summary>What the row says under its reward: the count while it is open, then whether the
    /// reward is still waiting in the vault or has been taken.</summary>
    public string CountText => IsUnclaimed ? "ready" : Claimed ? "claimed" : ProgressText;

    public string AcclaimText => $"+{Acclaim}";

    public double Fraction =>
        ProgressComplete <= 0 ? 0 : Math.Clamp((double)ProgressCurrent / ProgressComplete, 0, 1);

    /// <summary>
    /// The row's leading spine, same device the material list uses: brass means "go and take
    /// this", dim means done and banked, nothing means still in progress.
    /// </summary>
    public Color StatusColor => IsUnclaimed
        ? Theme.Brass
        : Claimed
            ? Theme.Banked
            : Colors.Transparent;
}

/// <summary>A track — Daily, Weekly or Special.</summary>
public sealed class VaultSection(
    string name, string summary, IEnumerable<VaultRow> rows, string? warning = null)
    : List<VaultRow>(rows)
{
    public string Name { get; } = name;
    public string Summary { get; } = summary;

    /// <summary>Set when what is listed belongs to a cycle that has already ended.</summary>
    public string? Warning { get; } = warning;
}

/// <summary>Everything the vault screen shows at once.</summary>
/// <param name="ShopNote">Why the shop's limits are not what is left, when the API reports no
/// purchases; or why there is no shop.</param>
public sealed record VaultView(
    string SeasonLine, string AcclaimText, bool HasAcclaim, IReadOnlyList<VaultSection> Sections,
    IReadOnlyList<VaultShopSection> Shop, string? ShopNote);

/// <summary>
/// Reads the three Wizard's Vault tracks and the Astral Acclaim balance behind them.
/// Each track is one request; the balance rides along on the wallet the app already reads.
/// </summary>
public sealed class VaultService(
    Gw2Client client, ApiKeySession session, SnapshotStore snapshots, OfflineReader offline, ItemLookup items,
    VaultSignal vaultSignal)
{
    private const int AstralAcclaimCurrencyId = 63;

    private VaultSeason? _season;

    public async Task<VaultView> GetAsync(CancellationToken ct = default)
    {
        // The season only turns over a few times a year — once per app run is plenty.
        _season ??= (await offline.ReadAsync(
            "wizardsvault", Gw2JsonContext.Default.VaultSeason,
            client.GetVaultSeasonAsync, ct)).Value;

        var sections = new List<VaultSection>();
        foreach (var track in new[] { "daily", "weekly", "special" })
        {
            var data = (await offline.ReadAsync(
                $"account/wizardsvault/{track}", Gw2JsonContext.Default.VaultTrack,
                token => client.GetVaultTrackAsync(track, token), ct)).Value;

            // The account endpoints do not roll over at reset — they keep serving the last
            // cycle until the player next logs into the game. Nothing in the response says
            // so, so the test is whether the data has changed since the reset: unchanged
            // across it means the account has not turned over yet.
            var changedAt = await snapshots.RecordAsync(
                $"vault/{track}", data, Gw2JsonContext.Default.VaultTrack, ct);

            var warning = Resets.LastFor(track, DateTimeOffset.UtcNow) is { } reset &&
                          changedAt < reset
                ? $"This is the previous {TrackName(track).ToLowerInvariant()} cycle. The " +
                  "Wizard's Vault only updates once you log into the game."
                : null;

            vaultSignal.Saw(track, data, warning is not null);

            var rows = data.Objectives
                .Select(o => new VaultRow(o.Title, o.Acclaim, o.ProgressCurrent, o.ProgressComplete, o.Claimed))
                .OrderByDescending(r => r.IsUnclaimed)
                .ThenBy(r => r.Claimed)
                .ThenByDescending(r => r.Fraction)
                .ToList();

            if (rows.Count == 0) continue;

            var waiting = rows.Count(r => r.IsUnclaimed);
            var summary = waiting > 0
                ? $"{waiting} to claim · meta {data.MetaProgressCurrent}/{data.MetaProgressComplete}"
                : $"meta {data.MetaProgressCurrent}/{data.MetaProgressComplete}";

            sections.Add(new VaultSection(TrackName(track), summary, rows, warning));
        }

        var acclaim = await ReadAcclaimAsync(ct);
        var daysLeft = (_season.End - DateTimeOffset.UtcNow).Days;
        var (shop, shopNote) = await ReadShopAsync(acclaim, ct);

        return new VaultView(
            daysLeft > 0 ? $"{_season.Title} · {daysLeft} days left" : _season.Title,
            acclaim is null ? string.Empty : $"{acclaim:N0}",
            acclaim is not null,
            sections,
            shop,
            shopNote);
    }

    /// <summary>
    /// The shop, with names and icons from the item lookup the rest of the app uses. A failure
    /// here costs the shop tab, not the objectives.
    /// </summary>
    private async Task<(IReadOnlyList<VaultShopSection> Shop, string? Note)> ReadShopAsync(
        long? acclaim, CancellationToken ct)
    {
        try
        {
            var listings = (await offline.ReadAsync(
                "account/wizardsvault/listings", Gw2JsonContext.Default.ListVaultListing,
                client.GetVaultListingsAsync, ct)).Value;

            var resolved = await items.ResolveAsync(listings.Select(listing => listing.ItemId).Distinct().ToList(), ct);

            var shop = VaultShop.Sections(
                listings,
                id => resolved.GetValueOrDefault(id) is { } item ? (item.Name, item.Icon) : ($"Item {id}", null),
                acclaim);

            return (shop, VaultShop.Caution);
        }
        catch (Gw2ApiException)
        {
            return ([], "The vault's shop could not be read just now.");
        }
    }

    private static string TrackName(string track) => track switch
    {
        "daily" => "Daily",
        "weekly" => "Weekly",
        _ => "Special"
    };

    /// <summary>Astral Acclaim is a wallet currency; without that scope the header just omits it.</summary>
    private async Task<long?> ReadAcclaimAsync(CancellationToken ct)
    {
        if (!session.HasPermission(Gw2Permissions.Wallet)) return null;

        try
        {
            var wallet = (await offline.ReadAsync(
                "account/wallet", Gw2JsonContext.Default.ListWalletEntry,
                client.GetWalletAsync, ct)).Value;
            return wallet.FirstOrDefault(w => w.Id == AstralAcclaimCurrencyId)?.Value ?? 0;
        }
        catch (Gw2ApiException)
        {
            return null;
        }
    }
}
