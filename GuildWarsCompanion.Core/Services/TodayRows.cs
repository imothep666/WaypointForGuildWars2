namespace GuildWarsCompanion.Services;

/// <summary>
/// One thing that resets, and how far through it you are.
///
/// Deliberately free of MAUI: the row says what it is and the view decides how to colour it.
/// That is what lets the reset trackers — including the "could not read" state, which is the
/// easiest thing here to break silently — be tested without a UI framework.
/// </summary>
public sealed record TodayRow(
    string Name, int Done, int Total, string? Remaining, bool IsStale = false,
    string? Route = null, bool Unread = false)
{
    public bool IsComplete => !Unread && Total > 0 && Done >= Total;

    /// <summary>Whether tapping the row leads anywhere.</summary>
    public bool CanOpen => !string.IsNullOrEmpty(Route);

    /// <summary>
    /// Nothing to chase: either finished, or not known. Both are quiet; only outstanding
    /// work is worth brass.
    /// </summary>
    public bool IsQuiet => Unread || IsComplete;

    public string Progress => Unread ? "—" : $"{Done} / {Total}";

    public double Fraction => Unread || Total <= 0 ? 0 : Math.Clamp((double)Done / Total, 0, 1);

    /// <summary>
    /// What is shown under the row.
    ///
    /// A tracker that could not be read says so and stays. Dropping the row instead — which is
    /// what happened until a rate limit made Map bonus chests vanish from a five-row list —
    /// leaves a shorter list and no way to tell anything is missing.
    /// </summary>
    public string? Caption => Unread
        ? L.T("Could not be read just now — pull down to try again.")
        : IsStale
            ? L.T("Last cycle — the vault only updates once you log into the game.")
            : Remaining;
}

/// <summary>Daily and weekly, kept apart: they expire on different clocks.</summary>
/// <param name="key">What code knows the section by, since <paramref name="name"/> is in the
/// phone's language; the name itself when not given.</param>
public sealed class TodaySection(string name, string summary, IEnumerable<TodayRow> rows, string? key = null)
    : List<TodayRow>(rows)
{
    public string Name { get; } = name;
    public string Key { get; } = key ?? name;
    public string Summary { get; } = summary;
}

/// <summary>How a section describes itself in one line.</summary>
public static class TodaySummary
{
    /// <summary>
    /// Counts only what could actually be read.
    ///
    /// An unreadable tracker is not "unfinished" — nobody knows what it is. Counting it as
    /// outstanding would be a guess dressed up as a total, and counting it as done would be
    /// worse.
    /// </summary>
    public static string Of(IReadOnlyList<TodayRow> rows)
    {
        var readable = rows.Where(r => !r.Unread).ToList();
        var unread = rows.Count - readable.Count;

        var outstanding = readable.Count(r => !r.IsComplete);

        var summary = readable.Count == 0
            ? L.T("Nothing could be read")
            : outstanding == 0
                ? L.T("All clear")
                : L.F("{0} of {1} unfinished", outstanding, readable.Count);

        return unread == 0 ? summary : L.F("{0} · {1} unread", summary, unread);
    }

    /// <summary>Rows worth showing: anything with a target, plus anything that failed to load.</summary>
    public static IReadOnlyList<TodayRow> Worth(IEnumerable<TodayRow> rows) =>
        rows.Where(r => r.Total > 0 || r.Unread).ToList();
}
