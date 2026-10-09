namespace GuildWarsCompanion.Services;

/// <summary>How far through a reset tracker an account is, and what is left of it.</summary>
public sealed record ResetCount(int Done, int Total, IReadOnlyList<string> Remaining);

/// <summary>
/// Comparing "everything that counts" against "what this account has done".
///
/// The subtlety worth isolating: the completed list is not always a subset of the tracked
/// one. /v2/account/raids reports checkpoints alongside encounters, and only encounters are
/// counted here — so anything reported done that is not being tracked has to be ignored
/// rather than added. Counting the completed list directly would report 30 of 26 bosses
/// killed, which is both impossible and invisible until someone reads it closely.
/// </summary>
public static class ResetProgress
{
    /// <summary>
    /// <paramref name="everything"/> defines the denominator; nothing outside it can move
    /// the numerator. Remaining names are in the order the API listed them, which for these
    /// endpoints is the order the game presents them in.
    /// </summary>
    public static ResetCount Of(
        IEnumerable<string> everything,
        IEnumerable<string> completed,
        Func<string, string>? name = null)
    {
        var done = completed as ISet<string> ?? completed.ToHashSet();
        var tracked = everything.Distinct().ToList();
        var label = name ?? Ids.ToDisplayName;

        var remaining = tracked.Where(id => !done.Contains(id)).Select(label).ToList();

        return new ResetCount(tracked.Count - remaining.Count, tracked.Count, remaining);
    }
}
