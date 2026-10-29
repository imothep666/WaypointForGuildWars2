using GuildWarsCompanion.Data;
using GuildWarsCompanion.Gw2;

namespace GuildWarsCompanion.Services;

/// <summary>The last session in words, for Today and for the notification.</summary>
/// <param name="Heading">"Last session · 1 h 50 min" or "This session · 40 min so far".</param>
/// <param name="When">"ended about 23:10" or "playing now".</param>
/// <param name="Worth">"+34g 20s in all · 15g 40s an hour", or null when the items were not recorded.</param>
public sealed record SessionText(
    string Heading, string When, IReadOnlyList<string> Characters, string Wallet, string? Worth = null);

/// <summary>One line on the session page: an icon, a name and a figure, and what it is worth.</summary>
public sealed record SessionLine(string? IconUrl, string Name, string Figure, string? Note = null, string? Rarity = null)
{
    public Color RarityColor => Theme.RarityColor(Rarity);

    public bool HasIcon => IconUrl is { Length: > 0 };

    public bool HasNote => Note is { Length: > 0 };
}

/// <summary>Everything the page says about one session.</summary>
/// <param name="Earned">Coin, items and their sum, and the sum an hour; empty when the items
/// were not recorded at both ends.</param>
/// <param name="Gained">Items there are more of, most valuable first.</param>
/// <param name="Spent">Items there are fewer of: used, salvaged, sold or listed.</param>
/// <param name="Progress">Achievements, vault objectives, masteries and crafting, one per line.</param>
/// <param name="Missing">What could not be told for this session, and why; null when everything was.</param>
public sealed record SessionReport(
    Session Played,
    SessionText Text,
    IReadOnlyList<SessionLine> Earned,
    IReadOnlyList<SessionLine> Wallet,
    IReadOnlyList<SessionLine> Gained,
    IReadOnlyList<SessionLine> Spent,
    IReadOnlyList<string> Progress,
    string? Missing);

/// <summary>A session in the list of earlier ones.</summary>
/// <param name="Key">When the session was measured from; what selects it.</param>
public sealed record SessionRow(DateTimeOffset Key, string Title, string Figure, string Caption, bool IsShown);

/// <summary>A day's sessions, under the day and the day's playtime.</summary>
public sealed class SessionDay(string name, string total, IEnumerable<SessionRow> rows) : List<SessionRow>(rows)
{
    public string Name { get; } = name;

    public string Total { get; } = total;
}

/// <summary>
/// Keeps the ledger and reads sessions out of it.
///
/// A point is recorded by the half-hourly background poll and whenever Today is refreshed, never
/// more often than every five minutes: one request for the names, one per character for its core,
/// one for the wallet. It needs the characters and wallet scopes; without them there is no recap,
/// and nothing pretends there is.
///
/// A point the account changed by also gets a detail: storage, achievements, the vault, masteries
/// and crafting, about ten requests with every character in full among them. An account at rest
/// changes by nothing, so the poll asks for that only around play, not every half hour of the day.
/// </summary>
public sealed class LedgerService(
    Gw2Client client, LedgerStore store, ApiKeySession session, Gw2Cache cache, ItemLookup items)
{
    private static readonly TimeSpan Spacing = TimeSpan.FromMinutes(5);

    /// <summary>How many items each of the page's two lists shows before "and N more".</summary>
    private const int ItemsShown = 40;

    public bool CanRecord =>
        session.HasPermission(Gw2Permissions.Characters) && session.HasPermission(Gw2Permissions.Wallet);

    /// <summary>Takes a point unless one was taken in the last five minutes. False when it did not.</summary>
    public async Task<bool> RecordAsync(CancellationToken ct = default)
    {
        if (!CanRecord) return false;

        var now = DateTimeOffset.UtcNow;

        if (await store.LatestAtAsync(ct) is { } latest && now - latest < Spacing) return false;

        var characters = new Dictionary<string, (long, int)>();

        foreach (var name in await client.GetCharacterNamesAsync(ct))
        {
            var core = await client.GetCharacterCoreAsync(name, ct);
            characters[core.Name] = (core.AgeSeconds, core.Level);
        }

        var wallet = (await client.GetWalletAsync(ct)).ToDictionary(entry => entry.Id, entry => entry.Value);

        var point = new LedgerPoint(now, characters, wallet);
        var points = await store.LoadAsync(ct);

        // The last detail still holds while nothing moved; see SessionRecap.Changed.
        var detail = points.LastOrDefault(kept => kept.HasDetail) is { } held && !SessionRecap.Changed(held, point)
            ? null
            : await ReadDetailAsync(ct);

        await store.SaveAsync(point, detail, ct);

        points.Add(point with { HasDetail = detail is not null });
        await store.KeepDetailsAsync(SessionRecap.DetailsToKeep(points), ct);

        return true;
    }

    /// <summary>
    /// The account's holdings and progress as of now, each part read only when the key allows it.
    /// A connection failure fails the whole point, as the cheap half does: a point with half a
    /// detail would measure a session against nothing.
    /// </summary>
    private async Task<LedgerDetail> ReadDetailAsync(CancellationToken ct)
    {
        var everyone = await client.GetCharactersAsync(ct);

        IReadOnlyDictionary<int, int>? held = null;

        if (session.HasPermission(Gw2Permissions.Inventories))
        {
            var counts = new Dictionary<int, int>();

            void Add(int id, int count) => counts[id] = counts.GetValueOrDefault(id) + count;

            foreach (var material in await client.GetAccountMaterialsAsync(ct)) Add(material.Id, material.Count);

            foreach (var slot in (await client.GetBankAsync(ct)).Concat(await client.GetSharedInventoryAsync(ct)))
                if (slot is not null)
                    Add(slot.Id, slot.Count);

            foreach (var character in everyone)
            {
                foreach (var slot in character.Bags?.SelectMany(bag => bag?.Inventory ?? []) ?? [])
                    if (slot is not null)
                        Add(slot.Id, slot.Count);

                // Worn, so putting on what was in a bag moves nothing. Not what is drawn from the
                // armory, unlike the storage screen: equipping a legendary would gain one.
                foreach (var piece in character.Equipment ?? [])
                    if (piece.Location == "Equipped")
                        Add(piece.Id, 1);
            }

            held = counts;
        }

        IReadOnlyDictionary<int, AchievementMark>? achievements = null;
        IReadOnlyDictionary<string, int>? points = null;
        IReadOnlyDictionary<int, int>? masteries = null;
        IReadOnlyList<VaultMark>? vault = null;

        if (session.HasPermission(Gw2Permissions.Progression))
        {
            achievements = (await client.GetAccountAchievementsAsync(ct))
                .Where(achievement => achievement.Done || achievement.Repeated > 0)
                .ToDictionary(achievement => achievement.Id, achievement => new AchievementMark(achievement.Done, achievement.Repeated));

            points = (await client.GetMasteryPointsAsync(ct))?.Totals?
                .ToDictionary(total => total.Region, total => total.Earned);

            masteries = (await client.GetAccountMasteriesAsync(ct))
                .ToDictionary(mastery => mastery.Id, mastery => Masteries.LevelsDone(mastery.Level));

            if (session.HasPermission(Gw2Permissions.Account))
            {
                var marks = new List<VaultMark>();

                foreach (var track in new[] { "daily", "weekly", "special" })
                    marks.AddRange((await client.GetVaultTrackAsync(track, ct)).Objectives.Select(objective =>
                        new VaultMark(track, objective.Id, objective.Title,
                            objective.ProgressCurrent >= objective.ProgressComplete)));

                vault = marks;
            }
        }

        var crafting = everyone.ToDictionary(
            character => character.Name,
            character => (IReadOnlyDictionary<string, int>)(character.Crafting ?? [])
                .ToDictionary(discipline => discipline.Name, discipline => discipline.Rating));

        return new LedgerDetail(held, achievements, vault, points, masteries, crafting);
    }

    public async Task<Session?> LatestAsync(CancellationToken ct = default) =>
        SessionRecap.Latest(await store.LoadAsync(ct));

    /// <summary>Every session the ledger holds, newest first.</summary>
    public async Task<IReadOnlyList<Session>> AllAsync(CancellationToken ct = default) =>
        SessionRecap.All(await store.LoadAsync(ct)).Reverse().ToList();

    /// <summary>What the session changed beyond the wallet, or null when either end has no detail.</summary>
    public async Task<SessionChanges?> ChangesAsync(Session played, CancellationToken ct = default)
    {
        var points = await store.LoadAsync(ct);

        if (SessionRecap.DetailFor(points, played.Before) is not { } from ||
            SessionRecap.DetailFor(points, played.End) is not { } to)
            return null;

        return await store.DetailAsync(from.At, ct) is { } before && await store.DetailAsync(to.At, ct) is { } after
            ? SessionChanges.Between(before, after)
            : null;
    }

    public async Task<SessionText> DescribeAsync(Session played, CancellationToken ct = default)
    {
        var changes = await ChangesAsync(played, ct);

        return await DescribeAsync(
            played, changes, changes?.Items is { } moved ? await PricesAsync(moved.Keys, ct) : null, ct);
    }

    private async Task<SessionText> DescribeAsync(
        Session played, SessionChanges? changes, IReadOnlyDictionary<int, long>? prices, CancellationToken ct)
    {
        var names = await CurrencyNamesAsync(ct);

        var duration = SessionRecap.Duration(played.PlayedSeconds);

        var worth = changes?.Items is { } moved && prices is not null
            ? SessionRecap.Worth(Coin(played) + SessionChanges.Worth(moved, prices), played.PlayedSeconds)
            : null;

        return new SessionText(
            played.IsOngoing ? L.F("This session · {0} so far", duration) : L.F("Last session · {0}", duration),
            // When the last poll still saw playtime rising: the session ended after it, before the next.
            played.IsOngoing
                ? L.T("playing now")
                : L.F("last seen playing {0:ddd HH:mm}", played.LastSeen.At.ToLocalTime()),
            played.Characters.Select(SessionRecap.Line).ToList(),
            SessionRecap.Wallet(played.WalletChange, id => names.GetValueOrDefault(id, L.F("currency {0}", id))),
            worth);
    }

    /// <summary>The page's account of one session: the recap, and everything behind it.</summary>
    public async Task<SessionReport> ReportAsync(Session played, CancellationToken ct = default)
    {
        var changes = await ChangesAsync(played, ct);
        var prices = changes?.Items is { } counted ? await PricesAsync(counted.Keys, ct) : null;

        var text = await DescribeAsync(played, changes, prices, ct);
        var currencies = await CurrenciesAsync(ct);

        var wallet = played.WalletChange
            .OrderBy(pair => pair.Key == Services.Coin.CurrencyId ? int.MinValue : currencies.GetValueOrDefault(pair.Key)?.Order ?? int.MaxValue)
            .Select(pair => new SessionLine(
                currencies.GetValueOrDefault(pair.Key)?.Icon,
                currencies.GetValueOrDefault(pair.Key)?.Name ?? L.F("currency {0}", pair.Key),
                Signed(pair.Key, pair.Value)))
            .ToList();

        var earned = new List<SessionLine>();
        var gained = new List<SessionLine>();
        var spent = new List<SessionLine>();

        if (changes?.Items is { } moved && prices is not null)
        {
            var known = await NamesAsync(moved.Keys, ct);

            var coin = Coin(played);
            var worth = SessionChanges.Worth(moved, prices);

            earned.Add(new SessionLine(null, L.T("Coin"), Signed(Services.Coin.CurrencyId, coin)));
            earned.Add(new SessionLine(null, L.T("Items"), Signed(Services.Coin.CurrencyId, worth),
                L.T("at what buyers offer now")));
            earned.Add(new SessionLine(null, L.T("In all"), Signed(Services.Coin.CurrencyId, coin + worth)));

            if (SessionRecap.PerHour(coin + worth, played.PlayedSeconds) is { } hourly)
                earned.Add(new SessionLine(null, L.T("An hour"), Signed(Services.Coin.CurrencyId, hourly)));

            SessionLine Line(KeyValuePair<int, int> pair)
            {
                var item = known.GetValueOrDefault(pair.Key);
                var each = prices.GetValueOrDefault(pair.Key);

                return new SessionLine(
                    item?.Icon,
                    item?.Name ?? L.F("Item {0}", pair.Key),
                    pair.Value > 0 ? $"+{pair.Value:N0}" : $"−{-pair.Value:N0}",
                    each > 0 ? Services.Coin.Format(Math.Abs(pair.Value) * each) : null,
                    item?.Rarity);
            }

            // Most valuable first; what has no price after, the most of it first.
            IEnumerable<KeyValuePair<int, int>> Ranked(IEnumerable<KeyValuePair<int, int>> side) =>
                side.OrderByDescending(pair => Math.Abs(pair.Value) * prices.GetValueOrDefault(pair.Key))
                    .ThenByDescending(pair => Math.Abs(pair.Value));

            gained.AddRange(Ranked(moved.Where(pair => pair.Value > 0)).Select(Line));
            spent.AddRange(Ranked(moved.Where(pair => pair.Value < 0)).Select(Line));
        }

        return new SessionReport(
            played, text, earned, wallet,
            Capped(gained), Capped(spent),
            changes is null ? [] : await ProgressAsync(changes, ct),
            Missing(changes));
    }

    /// <summary>The list of sessions, a day at a time, with the one on the page marked.</summary>
    public async Task<IReadOnlyList<SessionDay>> DaysAsync(
        IReadOnlyList<Session> sessions, DateTimeOffset? shown, CancellationToken ct = default)
    {
        var names = await CurrencyNamesAsync(ct);

        return sessions
            .GroupBy(played => played.LastSeen.At.ToLocalTime().Date)
            .Select(day => new SessionDay(
                L.F("{0:dddd d MMMM}", day.Key),
                SessionRecap.Duration(day.Sum(played => played.PlayedSeconds)),
                day.Select(played => new SessionRow(
                    played.Before.At,
                    SessionRecap.Who(played),
                    SessionRecap.Duration(played.PlayedSeconds),
                    (played.IsOngoing
                        ? L.T("playing now")
                        : L.F("last seen playing {0:HH:mm}", played.LastSeen.At.ToLocalTime())) +
                    " · " + SessionRecap.Wallet(played.WalletChange,
                        id => names.GetValueOrDefault(id, L.F("currency {0}", id)), most: 2),
                    played.Before.At == shown))))
            .ToList();
    }

    private static IReadOnlyList<SessionLine> Capped(List<SessionLine> lines) =>
        lines.Count <= ItemsShown
            ? lines
            : [..lines.Take(ItemsShown), new SessionLine(null, L.F("and {0} more", lines.Count - ItemsShown), string.Empty)];

    private static string? Missing(SessionChanges? changes)
    {
        if (changes is null)
            return L.T("What this session gained in items and progress was not recorded: the ledger keeps " +
                       "that only for sessions since this version, starting from a point before them.");

        var parts = new List<string>();
        if (changes.Items is null) parts.Add(L.T("items need the 'inventories' permission"));
        if (changes.Achievements is null) parts.Add(L.T("achievements and masteries need the 'progression' permission"));

        return parts.Count == 0 ? null : L.F("Not shown: {0}.", string.Join("; ", parts));
    }

    /// <summary>One line each, in the order a player would tell it: the vault, achievements, masteries, crafting.</summary>
    private async Task<IReadOnlyList<string>> ProgressAsync(SessionChanges changes, CancellationToken ct)
    {
        var lines = new List<string>();

        foreach (var mark in changes.Vault ?? [])
            lines.Add(L.F("Wizard's Vault · {0}", mark.Title));

        if (changes.Achievements is { Count: > 0 } finished)
        {
            var named = await Optional(() => client.GetAchievementsAsync(finished, ct), []);
            var names = named.ToDictionary(achievement => achievement.Id, achievement => achievement.Name);

            lines.AddRange(finished.Select(id =>
                L.F("Achievement · {0}", names.GetValueOrDefault(id) ?? L.F("number {0}", id))));
        }

        foreach (var (region, earned) in changes.MasteryPoints ?? new Dictionary<string, int>())
            lines.Add(L.F("+{0} mastery points · {1}", earned, Masteries.RegionName(region)));

        if (changes.Masteries is { Count: > 0 } trained)
        {
            var tracks = (await Optional(() => client.GetMasteriesAsync(ct), []))
                .ToDictionary(track => track.Id, track => track.Name);

            lines.AddRange(trained.Select(rise => L.F("Mastery · {0} · level {1} → {2}",
                tracks.GetValueOrDefault(rise.Track) ?? L.F("number {0}", rise.Track), rise.From, rise.To)));
        }

        lines.AddRange((changes.Crafting ?? []).Select(rise => L.F("{0} · {1} {2} → {3}",
            rise.Character, CraftingLevels.DisciplineName(rise.Discipline), rise.From, rise.To)));

        return lines;
    }

    private static long Coin(Session played) => played.WalletChange.GetValueOrDefault(Services.Coin.CurrencyId);

    private static string Signed(int currency, long value) =>
        (value < 0 ? "−" : "+") + Services.Coin.FormatAmount(currency, Math.Abs(value));

    /// <summary>
    /// What buyers offer for one of each, now. Prices are never stored, so offline this is null,
    /// and the recap says nothing about worth rather than counting every item at nothing.
    /// </summary>
    private async Task<IReadOnlyDictionary<int, long>?> PricesAsync(IEnumerable<int> ids, CancellationToken ct) =>
        (await Optional<List<ItemPrice>?>(async () => await client.GetPricesAsync(ids, ct), null))?
        .Where(price => price.Buys?.UnitPrice is > 0)
        .ToDictionary(price => price.Id, price => (long)price.Buys!.UnitPrice);

    private async Task<IReadOnlyDictionary<int, Item>> NamesAsync(IEnumerable<int> ids, CancellationToken ct) =>
        await Optional(() => items.ResolveAsync(ids.ToList(), ct), new Dictionary<int, Item>());

    private async Task<Dictionary<int, string>> CurrencyNamesAsync(CancellationToken ct) =>
        (await CurrenciesAsync(ct)).ToDictionary(pair => pair.Key, pair => pair.Value.Name);

    private async Task<Dictionary<int, Currency>> CurrenciesAsync(CancellationToken ct)
    {
        var currencies = await cache.GetCurrenciesAsync(ct);

        if (currencies.Count == 0)
        {
            try
            {
                currencies = await client.GetCurrenciesAsync(ct);
                await cache.SaveCurrenciesAsync(currencies, ct);
            }
            catch (Gw2ApiException)
            {
                // Numbered rather than named; the amounts are still right.
            }
        }

        return currencies.ToDictionary(currency => currency.Id);
    }

    /// <summary>Names and prices are a bonus on a recap; without a connection it still stands.</summary>
    private static async Task<T> Optional<T>(Func<Task<T>> read, T otherwise)
    {
        try
        {
            return await read();
        }
        catch (Gw2ApiException)
        {
            return otherwise;
        }
    }
}
