using GuildWarsCompanion.Gw2;

namespace GuildWarsCompanion.Services;

/// <summary>Everything the fractals page shows.</summary>
/// <param name="Level">The account's personal fractal level; zero before the first fractal.</param>
/// <param name="Today">Today's dailies at the account's tier, and the recommended scales.</param>
/// <param name="Tomorrow">Tomorrow's fractals and scales in one line, or null when the API gave none.</param>
/// <param name="Wallet">Relics, pristine relics and essence, or empty without the wallet permission.</param>
/// <param name="IsYesterday">Read from storage offline, and from before the last reset: the list is
/// yesterday's, and the page says so rather than presenting it as today's.</param>
public sealed record FractalsView(
    int Level, IReadOnlyList<FractalTask> Today, string? Tomorrow, IReadOnlyList<WalletRow> Wallet,
    bool IsYesterday)
{
    public int Tier => Fractals.TierFor(Level);

    /// <summary>What Today counts: the fractals and the scales the account can enter.</summary>
    public IReadOnlyList<FractalTask> Counted => [.. Today.Where(task => task.Counts)];
}

/// <summary>
/// The daily fractals and the fractal currencies, read for Today's row and for the page behind it.
///
/// Four reads, each kept for the underground like the rest of Today: the category (which names
/// today's and tomorrow's achievements), those achievements in English, the account's fractal
/// level, and which achievements it has done. The wallet is a fifth, and only for the page.
/// </summary>
public sealed class FractalService(
    Gw2Client client, OfflineReader offline, AccountDataService account, ApiKeySession session)
{
    public async Task<FractalsView> GetAsync(bool withWallet, CancellationToken ct = default)
    {
        var category = await offline.ReadAsync(
            "fractals/daily", Gw2JsonContext.Default.DailyCategory, client.GetDailyFractalsAsync, ct);

        var today = category.Value.Achievements.Select(entry => entry.Id).ToList();
        var tomorrow = (category.Value.Tomorrow ?? []).Select(entry => entry.Id).ToList();

        var named = (await offline.ReadAsync(
            "fractals/daily/names", Gw2JsonContext.Default.ListAchievement,
            token => client.GetEnglishAchievementsAsync([.. today, .. tomorrow], token), ct)).Value;

        var level = (await offline.ReadAsync(
            "account", Gw2JsonContext.Default.Account, client.GetAccountAsync, ct)).Value.FractalLevel;

        var done = (await offline.ReadAsync(
                "account/achievements", Gw2JsonContext.Default.ListAccountAchievement,
                client.GetAccountAchievementsAsync, ct)).Value
            .Where(progress => progress.Done)
            .Select(progress => progress.Id)
            .ToHashSet();

        var local = await LocalAsync([.. today, .. tomorrow], ct);

        var day = Fractals.Read(named.Where(achievement => today.Contains(achievement.Id)), local);
        var next = Fractals.Read(named.Where(achievement => tomorrow.Contains(achievement.Id)), local);

        var wallet = withWallet ? await WalletAsync(ct) : [];

        return new FractalsView(
            level, Fractals.Tasks(day, level, done), next.IsEmpty ? null : Fractals.Describe(next), wallet,
            category.FetchedAt is { } at && at < Resets.LastDaily(DateTimeOffset.UtcNow));
    }

    /// <summary>
    /// The same achievements in the phone's language, for the fractals' own names there. Null on an
    /// English phone, and when they cannot be had: the English names are still right, only foreign.
    /// </summary>
    private async Task<IReadOnlyDictionary<int, Achievement>?> LocalAsync(
        IReadOnlyList<int> ids, CancellationToken ct)
    {
        if (client.Language == Gw2Language.English) return null;

        try
        {
            var local = (await offline.ReadAsync(
                "fractals/daily/local", Gw2JsonContext.Default.ListAchievement,
                token => client.GetAchievementsAsync(ids, token), ct)).Value;

            return local.ToDictionary(achievement => achievement.Id);
        }
        catch (Gw2ApiException)
        {
            return null;
        }
    }

    /// <summary>The three fractal currencies, in the wallet's own names and art. Nothing at all
    /// without the permission, rather than a page that fails for want of it.</summary>
    private async Task<IReadOnlyList<WalletRow>> WalletAsync(CancellationToken ct)
    {
        if (!session.HasPermission(Gw2Permissions.Wallet)) return [];

        try
        {
            var rows = (await account.GetWalletAsync(Fractals.Currencies, ct)).Value;

            return [.. Fractals.Currencies.SelectMany(id => rows.Where(row => row.CurrencyId == id))];
        }
        catch (Gw2ApiException)
        {
            return [];
        }
    }
}
