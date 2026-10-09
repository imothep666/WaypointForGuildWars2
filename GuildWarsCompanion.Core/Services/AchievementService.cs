using GuildWarsCompanion.Gw2;

namespace GuildWarsCompanion.Services;

/// <summary>One achievement, ready to show.</summary>
public sealed record AchievementRow(
    int Id,
    string Name,
    string? Requirement,
    string ProgressText,
    string RemainingText,
    string PointsText,
    double Fraction,
    bool HasChecklist = false)
{
    public bool HasPoints => PointsText.Length > 0;
    public bool HasRequirement => !string.IsNullOrWhiteSpace(Requirement);

    /// <summary>
    /// Only collections name what they are made of, so only those rows lead anywhere. The
    /// hint marks which ones, rather than leaving taps to be discovered by trial.
    /// </summary>
    public string ChecklistHint => HasChecklist ? "tap for the checklist" : string.Empty;
}

/// <summary>What the screen shows, and what it is a slice of.</summary>
public sealed record AchievementView(
    IReadOnlyList<AchievementRow> Rows, string Summary, bool HasAny);

/// <summary>An id resolved to what it is called and what it looks like.</summary>
internal sealed record Named(int Id, string Name, string? Icon);

/// <summary>One entry of a collection, named and ticked.</summary>
public sealed record CollectionRow(
    string Name, string Kind, bool Done, bool ShowKind, string? IconUrl = null,
    int? ItemId = null)
{
    public bool HasIcon => !string.IsNullOrWhiteSpace(IconUrl);

    /// <summary>
    /// Items have a page in the app — what it is, where yours are, what it crafts into.
    /// Skins, miniatures and map steps have none of that, so those go straight to the wiki,
    /// which is the only place the answer exists.
    /// </summary>
    public bool HasItemPage => ItemId is not null;

    public string WikiUrl => Wiki.UrlFor(
        ItemId is null && Kind == "Step" ? Wiki.SearchTerm(Name) : Name);

    /// <summary>Quiet once held; brass while it is still something to go and get.</summary>
    public Color EdgeColor => Done ? Theme.Banked : Theme.Brass;

    public string StatusText => Done ? "have it" : "still needed";

    /// <summary>
    /// Most collections are one kind throughout, where "Item" under all fifty rows says
    /// nothing. It earns its place only where skins, miniatures and items are mixed.
    /// </summary>
    public string KindText => ShowKind ? Kind : string.Empty;
}

/// <summary>A collection achievement, opened.</summary>
public sealed record CollectionView(
    string Name, string? Requirement, string Summary, IReadOnlyList<CollectionRow> Rows, string Note);

/// <summary>
/// The achievements nearest to finishing.
///
/// An account has touched thousands and there are over eight thousand in the game, so the
/// order of operations is what makes this affordable: rank on the account's own numbers, which
/// arrive in one request and carry no names, then fetch names for the two dozen being shown.
/// Doing it the other way round would mean forty requests to answer one screen.
/// </summary>
public sealed class AchievementService(
    Gw2Client client, OfflineReader offline, ItemLookup items)
{
    /// <summary>
    /// Enough to scroll, few enough to read. Also one bulk request, since the id ceiling is
    /// two hundred.
    /// </summary>
    private const int Show = 25;

    public async Task<AchievementView> GetAsync(CancellationToken ct = default)
    {
        var account = await offline.ReadAsync(
            "account/achievements", Gw2JsonContext.Default.ListAccountAchievement,
            client.GetAccountAchievementsAsync, ct);

        var tracked = account.Value
            .Select(a => new Tracked(a.Id, a.Current, a.Max, a.Done))
            .ToList();

        var closest = AchievementProgress.Closest(tracked, Show);

        if (closest.Count == 0)
            return new AchievementView([], NothingInFlight(tracked), false);

        var details = (await client.GetAchievementsAsync(closest.Select(a => a.Id), ct))
            .ToDictionary(a => a.Id);

        var rows = closest
            .Select(a => Describe(a, details.GetValueOrDefault(a.Id)))
            .ToList();

        return new AchievementView(rows, Summarise(tracked, closest.Count), true);
    }

    private static AchievementRow Describe(Tracked tracked, Achievement? detail)
    {
        var points = AchievementProgress.PointsForTier(
            detail?.Tiers?.Select(tier => (tier.Count, tier.Points)), tracked.Max);

        return new AchievementRow(
            tracked.Id,
            detail?.Name ?? $"Achievement {tracked.Id}",
            // The instruction, with this tier's number in it — the API leaves a gap there.
            Prose.Clean(AchievementProgress.Requirement(detail?.Requirement, tracked.Max)),
            AchievementProgress.ProgressText(tracked),
            AchievementProgress.RemainingText(tracked),
            points is > 0 ? $"+{points} AP" : string.Empty,
            tracked.Fraction,
            detail?.Bits is { Count: > 0 });
    }

    /// <summary>
    /// One achievement's checklist: every entry, named, with what is still wanted first.
    ///
    /// The account reports which entries are done as indices into the achievement's own bit
    /// array, so the two have to be read together — see <see cref="Collection"/>. Entries come
    /// as items, skins, miniatures or plain text, and each kind names itself differently.
    /// </summary>
    public async Task<CollectionView?> GetCollectionAsync(int id, CancellationToken ct = default)
    {
        var detail = (await client.GetAchievementsAsync([id], ct)).FirstOrDefault();
        var bits = detail?.Bits;

        if (detail is null || bits is null || bits.Count == 0) return null;

        var account = (await offline.ReadAsync(
                "account/achievements", Gw2JsonContext.Default.ListAccountAchievement,
                client.GetAccountAchievementsAsync, ct))
            .Value.FirstOrDefault(a => a.Id == id);

        var names = await NameBitsAsync(bits, ct);

        var mixed = bits.Select(bit => bit.Type).Distinct().Count() > 1;

        // Blank entries are dropped only after the account's indices have been matched
        // against the full array, since those indices count the blanks too.
        var entries = Collection.Checklist(bits.Count, account?.Bits)
            .Where(entry => !IsBlank(bits[entry.Index]))
            .ToList();

        var rows = entries
            .Select(entry => ShowEntry(bits[entry.Index], names, entry.Done, mixed))
            .ToList();

        return new CollectionView(
            detail.Name,
            Prose.Clean(AchievementProgress.Requirement(
                detail.Requirement, account?.Max ?? entries.Count)),
            Collection.Summarise(entries),
            rows,
            "Still needed first. Tap an item for its page, anything else for the wiki — the " +
            "API names every entry but never says where one comes from.");
    }

    /// <summary>
    /// A placeholder the game does not list: no text and nothing to look up. Maguuma Explorer
    /// has three, which is exactly why its 170 entries only ask for 167.
    /// </summary>
    private static bool IsBlank(AchievementBit bit) =>
        bit.Id is null && string.IsNullOrWhiteSpace(bit.Text);

    private static CollectionRow ShowEntry(
        AchievementBit bit, IReadOnlyDictionary<(string, int), Named> names, bool done,
        bool showKind)
    {
        var kind = bit.Type switch
        {
            "Skin" => "Skin",
            "Item" => "Item",
            "Minipet" => "Miniature",
            _ => "Step"
        };

        var found = bit.Id is { } bitId
            ? names.GetValueOrDefault((bit.Type ?? string.Empty, bitId))
            : null;

        var name = bit.Text is { Length: > 0 } text
            ? text
            : found?.Name ?? (bit.Id is { } id ? $"{kind} {id}" : kind);

        return new CollectionRow(
            name, kind, done, showKind, found?.Icon,
            // Only real items have a page to open; a skin id would open the wrong one.
            bit.Type == "Item" ? bit.Id : null);
    }

    /// <summary>
    /// Resolves each kind of entry from its own endpoint — skins, miniatures and items are
    /// three different id spaces that happen to share an array.
    /// </summary>
    private async Task<Dictionary<(string, int), Named>> NameBitsAsync(
        IReadOnlyList<AchievementBit> bits, CancellationToken ct)
    {
        var names = new Dictionary<(string, int), Named>();

        async Task ResolveAsync(string type, Func<IEnumerable<int>, Task<IEnumerable<Named>>> fetch)
        {
            var ids = bits.Where(b => b.Type == type && b.Id is not null)
                .Select(b => b.Id!.Value).Distinct().ToList();

            if (ids.Count == 0) return;

            try
            {
                foreach (var found in await fetch(ids))
                    names[(type, found.Id)] = found;
            }
            catch (Gw2ApiException)
            {
                // The entry falls back to "Skin 208", which is still a checklist line.
            }
        }

        await ResolveAsync("Skin", async ids => (await client.GetSkinsAsync(ids, ct))
            .Select(skin => new Named(skin.Id, skin.Name, skin.Icon)));

        await ResolveAsync("Minipet", async ids => (await client.GetMinisAsync(ids, ct))
            .Select(mini => new Named(mini.Id, mini.Name, mini.Icon)));

        await ResolveAsync("Item", async ids => (await items.ResolveAsync(ids.ToList(), ct))
            .Select(pair => new Named(pair.Key, pair.Value.Name, pair.Value.Icon)));

        return names;
    }

    private static string Summarise(IReadOnlyList<Tracked> tracked, int shown)
    {
        var started = tracked.Count(a => !a.Done && a.Max > 0 && a.Current > 0 && a.Current < a.Max);
        var done = tracked.Count(a => a.Done);

        return $"{shown} nearest of {started:N0} in progress · {done:N0} finished";
    }

    /// <summary>
    /// Nothing in flight is two different situations, and the difference is worth saying.
    /// </summary>
    private static string NothingInFlight(IReadOnlyList<Tracked> tracked) =>
        tracked.Count == 0
            ? "The API reports no achievement progress for this account."
            : $"Nothing part-finished. All {tracked.Count(a => a.Done):N0} you have touched are done.";
}
