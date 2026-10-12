namespace GuildWarsCompanion.Services;

/// <summary>One recipe that takes this item, as the catalogue reports it.</summary>
public sealed record UsedInRow(
    int OutputItemId, string? OutputName, int Needed, int OutputCount, int MinRating,
    string Disciplines);

/// <summary>
/// What an item turns into.
///
/// The other half of a crafting question, and the half the app could never answer: it could
/// say what makes an item, never what an item makes. The catalogue on disk holds both
/// directions — the ingredient index answers this without a request and without a connection.
/// </summary>
public static class UsedIn
{
    /// <summary>
    /// "2 needed · Tailor 75" — how many of this item the recipe takes, and who can make it.
    ///
    /// The count is the point: an item that goes in one at a time is a different prospect from
    /// one that goes in fifty at a time, and the recipe list alone does not say which.
    /// </summary>
    public static string Describe(UsedInRow row)
    {
        var needed = L.F("{0:N0} needed", row.Needed);
        var disciplines = Names(row.Disciplines);

        if (disciplines.Length == 0) return needed;

        return row.MinRating > 0
            ? $"{needed} · {disciplines} {row.MinRating}"
            : $"{needed} · {disciplines}";
    }

    /// <summary>
    /// The disciplines, unpicked from the pipe-delimited form the cache stores and joined the
    /// way the rest of the app joins a short list.
    /// </summary>
    public static string Names(string? disciplines) =>
        string.Join(" · ", (disciplines ?? string.Empty)
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(L.T));

    /// <summary>
    /// The heading, and what it admits. A list capped at fifteen out of two hundred has to say
    /// so, or it reads as the whole answer.
    /// </summary>
    public static string Summarise(int shown, int total) => total switch
    {
        0 => L.T("Nothing in the catalogue uses this."),
        1 => L.T("Used in one recipe."),
        _ when shown < total => L.F("Used in {0:N0} recipes — the {1} soonest to reach.", total, shown),
        _ => L.F("Used in {0:N0} recipes.", total)
    };
}
