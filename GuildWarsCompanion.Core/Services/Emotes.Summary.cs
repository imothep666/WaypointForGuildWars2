namespace GuildWarsCompanion.Services;

// The phone's half of Emotes: the text it shows. Kept out of Emotes.cs, which the bridge
// compiles as well and which therefore cannot reach L.
public static partial class Emotes
{
    /// <summary>
    /// The header line: what you can use, what you cannot, and what cannot be established.
    ///
    /// The third figure is stated rather than folded into either of the others. Adding the 29
    /// to "you can use" claims tomes that may never have been bought; adding them to "locked"
    /// tells a player they lack an emote they own.
    /// </summary>
    public static string Summarise(IReadOnlySet<string>? unlocked)
    {
        if (unlocked is null)
            return L.F("{0} free to every account · {1} unlockable",
                Catalogue.Count(emote => emote.IsFree), Catalogue.Count(emote => !emote.IsFree));

        var states = Catalogue.Select(emote => StateOf(emote, unlocked)).ToList();

        var usable = states.Count(state => state is EmoteState.Free or EmoteState.Unlocked);
        var locked = states.Count(state => state is EmoteState.Locked);
        var untracked = states.Count(state => state is EmoteState.Untracked);

        var parts = new List<string> { L.F("{0} you can use", usable) };

        if (locked > 0) parts.Add(L.F("{0} locked", locked));
        if (untracked > 0) parts.Add(L.F("{0} the API cannot see", untracked));

        return string.Join(" · ", parts);
    }
}
