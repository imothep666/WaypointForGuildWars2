using GuildWarsCompanion.Gw2;

namespace GuildWarsCompanion.Services;

/// <summary>Which pile of cosmetics a category counts.</summary>
public enum UnlockKind
{
    Dye,
    Skin,
    Mini,
    Outfit,
    MountSkin,
    Glider,
    Title,
    Mailcarrier,
    Novelty,
    Finisher,
    Jadebot,
    Skiff
}

/// <summary>
/// One pile of cosmetics: where the account's half comes from, and where the full list does.
/// </summary>
/// <param name="Account">The authenticated endpoint, without the /v2/ prefix.</param>
/// <param name="Catalogue">The public endpoint that knows how many there are in total.</param>
/// <param name="Browsable">
/// Whether the entries can be listed by name.
///
/// False only for skins, and only because of size: /v2/skins is around fourteen thousand ids,
/// so naming them is a bulk download with its own cache and progress, not a page load. The
/// count is honest and cheap — one request either way — so the category still appears with a
/// real number rather than being hidden until the rest is built.
/// </param>
public sealed record UnlockCategory(
    UnlockKind Kind, string Name, string Account, string Catalogue, bool Browsable = true);

/// <summary>One thing in a category, and whether the account has it.</summary>
/// <param name="Rarity">
/// "Exclusive", "Rare", "Uncommon", "Common", "Starter" — the API's own words, for dyes only.
/// Null everywhere else, because no other cosmetic catalogue publishes a rarity at all.
/// </param>
public sealed record UnlockEntry(
    int Id, string Name, bool Owned, string? Swatch = null, string? Rarity = null);

/// <summary>
/// One heading in a browsed category, and what sits under it.
/// </summary>
/// <param name="Rarity">
/// The API's own word — "Exclusive", "Rare" — or <see cref="Unlocks.Locked"/> for the one group
/// that is not a rarity at all. Null rarity is possible in principle and lands in "Other"; in
/// practice only Dye Remover has none.
/// </param>
public sealed record UnlockGroup(string Rarity, IReadOnlyList<UnlockEntry> Entries)
{
    /// <summary>"EXCLUSIVE", as a heading is written on this screen.</summary>
    public string Heading => Rarity.ToUpperInvariant();

    public string Line => $"{Entries.Count:N0}";
}

/// <summary>How much of a category the account has.</summary>
public sealed record UnlockTally(UnlockCategory Category, int Owned, int Total)
{
    /// <summary>"412 of 682", grouped, because four figures without a separator is a smear.</summary>
    public string Line => $"{Owned:N0} of {Total:N0}";

    /// <summary>
    /// For a progress bar, in 0..1.
    ///
    /// Zero rather than a division when the total is unknown. A category whose catalogue has
    /// not been read yet is not a full bar, and it is not an exception either.
    /// </summary>
    public double Fraction => Total <= 0 ? 0 : Math.Clamp(Owned / (double)Total, 0, 1);
}

/// <summary>
/// What the account has unlocked, and how much of it there is.
///
/// The table is the point of this file. Every one of these categories is the same two
/// questions asked of two endpoints — what do I have, and how many are there — and writing
/// that once means a new category is one row rather than a new screen.
///
/// Pure, so the counting and the ordering can be tested without a key.
/// </summary>
public static class Unlocks
{
    /// <summary>
    /// Every category, in the order the screen lists them: the big visible ones first, the
    /// ones somebody goes looking for on purpose after.
    /// </summary>
    public static IReadOnlyList<UnlockCategory> All { get; } =
    [
        new(UnlockKind.Dye, "Dyes", "account/dyes", "colors"),
        new(UnlockKind.Skin, "Skins", "account/skins", "skins", Browsable: false),
        new(UnlockKind.Mini, "Minis", "account/minis", "minis"),
        new(UnlockKind.Outfit, "Outfits", "account/outfits", "outfits"),
        new(UnlockKind.MountSkin, "Mount skins", "account/mounts/skins", "mounts/skins"),
        new(UnlockKind.Glider, "Gliders", "account/gliders", "gliders"),
        new(UnlockKind.Title, "Titles", "account/titles", "titles"),
        new(UnlockKind.Mailcarrier, "Mailcarriers", "account/mailcarriers", "mailcarriers"),
        new(UnlockKind.Novelty, "Novelties", "account/novelties", "novelties"),
        new(UnlockKind.Finisher, "Finishers", "account/finishers", "finishers"),
        new(UnlockKind.Jadebot, "Jade bot skins", "account/jadebots", "jadebots"),
        new(UnlockKind.Skiff, "Skiff skins", "account/skiffs", "skiffs")
    ];

    /// <summary>
    /// Pairs a catalogue with the account's ids.
    ///
    /// Unlocked first, and that is a decision about what the screen is for. This answers "what
    /// have I got" — the question somebody opens it with — and the locked remainder is context
    /// underneath. Within each half the catalogue's own order is kept: the API returns dyes in
    /// the order the game groups them, which is more use than alphabetical.
    /// </summary>
    public static IReadOnlyList<UnlockEntry> Pair(
        IEnumerable<UnlockEntry> catalogue, IEnumerable<int>? owned)
    {
        var has = owned as IReadOnlySet<int> ?? (owned ?? []).ToHashSet();

        return catalogue
            .Select(entry => entry with { Owned = has.Contains(entry.Id) })
            .OrderByDescending(entry => entry.Owned)
            .ThenByDescending(entry => Rank(entry.Rarity))
            .ToList();
    }

    /// <summary>The group holding everything not unlocked. Not a rarity, and deliberately not
    /// one of the API's words, so it cannot collide with a rarity added later.</summary>
    public const string Locked = "Locked";

    /// <summary>Where an unlocked entry goes when it has no rarity to sort it by.</summary>
    public const string Other = "Other";

    /// <summary>
    /// Splits a paired category into the headings the screen draws.
    ///
    /// Unlocked first, one group per rarity, rarest group first — then everything still locked
    /// in a single group at the end, whatever its rarity. That last part is the point of
    /// grouping at all: a rarity heading answers "what have I got, and how good is it", and
    /// repeating those same five headings again for things nobody owns would answer nothing
    /// while doubling the headings to scroll past.
    ///
    /// Order within every group is the order it arrived in, which <see cref="Pair"/> has already
    /// settled.
    /// </summary>
    public static IReadOnlyList<UnlockGroup> Group(IEnumerable<UnlockEntry> entries)
    {
        var all = entries as IReadOnlyList<UnlockEntry> ?? entries.ToList();

        var groups = all
            .Where(entry => entry.Owned)
            .GroupBy(entry => entry.Rarity is { Length: > 0 } rarity ? rarity : Other)
            .OrderByDescending(group => Rank(group.Key))
            .Select(group => new UnlockGroup(group.Key, group.ToList()))
            .ToList();

        var locked = all.Where(entry => !entry.Owned).ToList();

        if (locked.Count > 0) groups.Add(new UnlockGroup(Locked, locked));

        return groups;
    }

    /// <summary>
    /// Where a dye's rarity sits, rarest highest.
    ///
    /// Everything without a rarity ranks zero, which is every category but dyes — LINQ's sort is
    /// stable, so those keep the catalogue's own order exactly as they did before this existed.
    /// </summary>
    public static int Rank(string? rarity) => rarity switch
    {
        "Exclusive" => 5,
        "Rare" => 4,
        "Uncommon" => 3,
        "Common" => 2,
        "Starter" => 1,
        _ => 0
    };

    /// <summary>
    /// A dye's rarity, which is the third of its three categories.
    ///
    /// The API publishes them as a bare array — ["Blue", "Vibrant", "Rare"] — with position as
    /// the only thing saying which is which. Reading it by index is therefore correct and also
    /// fragile, so it is done once, here, and the length is checked rather than assumed: Dye
    /// Remover carries an empty array and would otherwise throw on a screen full of colour.
    /// </summary>
    public static string? RarityOf(DyeColor dye) =>
        dye.Categories is { Count: 3 } categories ? categories[2] : null;

    /// <summary>
    /// The count line for a browsed category, from the paired list rather than the tally.
    ///
    /// Separate from <see cref="UnlockTally.Line"/> on purpose: the tally counts the account's
    /// ids, and a category can carry an id the public catalogue no longer lists — a dye pulled
    /// from the game does not leave the accounts that own it. Counting the drawn rows instead
    /// means the number and the list below it can never disagree.
    /// </summary>
    public static string Counted(IReadOnlyCollection<UnlockEntry> entries) =>
        $"{entries.Count(entry => entry.Owned):N0} of {entries.Count:N0} unlocked";

    /// <summary>
    /// "#8A2F2F" from three channels, or null when there are none.
    ///
    /// Its own function because a dye is the one category with something better than a name to
    /// show, and a swatch drawn from the wrong numbers is worse than no swatch — it is a
    /// confident wrong colour. Which is exactly what happened: see <see cref="Of"/>.
    /// </summary>
    public static string? Swatch(IReadOnlyList<int>? rgb) =>
        rgb is [var r, var g, var b, ..]
            ? $"#{Clamp(r):X2}{Clamp(g):X2}{Clamp(b):X2}"
            : null;

    /// <summary>
    /// What one dye actually looks like.
    ///
    /// NOT <c>base_rgb</c>, which is the trap here and reads like the obvious field. It is
    /// [128, 26, 26] for every dye in the game — the neutral base the material shifts are
    /// applied to, not the colour — so a list built from it is six hundred identical red
    /// squares that look deliberate. The colour lives in the material blocks.
    ///
    /// Cloth first because it is the truest to how a dye reads in the game's own preview, with
    /// the other two behind it for the handful of dyes that publish only some materials.
    /// </summary>
    public static string? Of(DyeColor dye) =>
        Swatch(dye.Cloth?.Rgb) ?? Swatch(dye.Leather?.Rgb) ?? Swatch(dye.Metal?.Rgb);

    private static int Clamp(int channel) => Math.Clamp(channel, 0, 255);
}
