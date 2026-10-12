namespace GuildWarsCompanion.Services;

/// <summary>One entry in a collection, and whether it is done.</summary>
public sealed record CollectionEntry(int Index, bool Done);

/// <summary>
/// Reading a collection's checklist.
///
/// The trap is that the account reports progress as <em>indices into the achievement's bit
/// array</em>, not as item or skin ids. Position is identity: bit 3 of this achievement is
/// done, not "item 3". Treating those indices as ids would produce a checklist that looks
/// entirely plausible and ticks off the wrong things.
/// </summary>
public static class Collection
{
    /// <summary>
    /// Every entry paired with whether the account has it, outstanding ones first.
    ///
    /// What is still needed is the reason to open the screen, so it leads. Within each group
    /// the original order is kept, because that is the order the game lists them in.
    /// </summary>
    public static IReadOnlyList<CollectionEntry> Checklist(int count, IEnumerable<int>? completed)
    {
        var done = completed as IReadOnlySet<int> ?? (completed ?? []).ToHashSet();

        return Enumerable.Range(0, Math.Max(count, 0))
            .Select(index => new CollectionEntry(index, done.Contains(index)))
            .OrderBy(entry => entry.Done)
            .ToList();
    }

    /// <summary>
    /// "3 of 7 collected" over entries that have already been sifted.
    ///
    /// The bit array carries blank placeholders — Maguuma Explorer lists 170 where the
    /// achievement only wants 167, the difference being three empty entries. Those cannot be
    /// dropped before pairing, because the account's indices point into the unsifted array,
    /// so the counting happens here, afterwards.
    /// </summary>
    public static string Summarise(IReadOnlyCollection<CollectionEntry> entries)
    {
        var have = entries.Count(entry => entry.Done);

        return L.F("{0:N0} of {1:N0} collected", have, entries.Count);
    }

    /// <summary>How many are still wanted.</summary>
    public static int Outstanding(int count, IEnumerable<int>? completed) =>
        Checklist(count, completed).Count(entry => !entry.Done);

    /// <summary>
    /// "3 of 7 collected", which is the same shape the rest of the app counts in.
    /// </summary>
    public static string Summarise(int count, IEnumerable<int>? completed)
    {
        var have = count - Outstanding(count, completed);

        return L.F("{0:N0} of {1:N0} collected", have, count);
    }
}
