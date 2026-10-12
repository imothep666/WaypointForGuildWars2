namespace GuildWarsCompanion.Services;

/// <summary>
/// What is left on the map the character is standing in, as far as the app can tell.
/// </summary>
/// <param name="ChestLooted">This map's bonus chest: looted today, waiting, or null when the map has
/// none or it could not be read.</param>
/// <param name="HeroPoints">How many hero points the map has.</param>
/// <param name="HeroPointsLeft">How many this character has not taken; null when not read.</param>
/// <param name="Bosses">World bosses fought on this map that are not done today and are due soon,
/// soonest first; empty when there are none or they could not be read.</param>
/// <param name="Meta">The map's meta event on now, or the next one; null when the map has none
/// in the wiki's timetable or it could not be read.</param>
public sealed record HereFacts(
    bool? ChestLooted, int HeroPoints, int? HeroPointsLeft,
    IReadOnlyList<(string Name, TimeSpan Wait)> Bosses,
    HereMeta? Meta = null);

/// <summary>A map's meta event as the "Here:" line says it.</summary>
/// <param name="Then">While one is on, the one after it and how soon. Seen on the phone: "Challenges
/// on now" in Auric Basin said nothing about the Octovine twelve minutes later, which is the thing
/// anyone standing there wants to know.</param>
public sealed record HereMeta(string Name, bool IsOn, TimeSpan Wait, (string Name, TimeSpan Wait)? Then = null);

/// <summary>
/// The line under "who is playing" on Today: what is still worth doing right here.
///
/// Today's trackers answer "what is left anywhere", which is the question before logging in. Once
/// a character is standing somewhere, the question is narrower — is there anything on this map —
/// and the answer used to need three screens.
///
/// Every part is said only when it was actually read. A part that could not be read is left out
/// rather than guessed, and "nothing left" is said only when something was read and all of it is
/// done; otherwise there is no line at all.
/// </summary>
public static class HereLine
{
    /// <summary>
    /// The bonus chest for a map, from its name.
    ///
    /// The API publishes the chests as ids and nothing else — no map, no name. The ids are the map's
    /// name with "_heros_choice_chest" on the end ("auric_basin_heros_choice_chest", "dragons_stand_…"),
    /// so the map is matched by its own name rather than by a table typed in here. An id that names
    /// no map ("convergence_…") simply never matches.
    /// </summary>
    public static string? ChestFor(string mapName, IEnumerable<string> chestIds)
    {
        var wanted = $"{Slug(mapName)}_heros_choice_chest";

        return chestIds.FirstOrDefault(id => string.Equals(id, wanted, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>"Dragon's Stand" → "dragons_stand": apostrophes dropped, anything else between words
    /// an underscore.</summary>
    private static string Slug(string name)
    {
        var letters = name.ToLowerInvariant()
            .Where(c => c != '\'' && c != '’')
            .Select(c => char.IsLetterOrDigit(c) ? c : '_');

        return string.Join('_', new string(letters.ToArray()).Split('_', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>"Here: map chest waiting · 2 of 13 hero points left · Shadow Behemoth in 25 min",
    /// "Here: nothing left", or null when nothing could be said.</summary>
    public static string? Describe(HereFacts facts)
    {
        var parts = new List<string>();

        if (facts.ChestLooted == false) parts.Add(L.T("map chest waiting"));

        if (facts is { HeroPoints: > 0, HeroPointsLeft: > 0 } && facts.HeroPointsLeft is { } left)
            parts.Add(L.F("{0} of {1} hero points left", left, facts.HeroPoints));

        if (facts.Meta is { } meta)
        {
            parts.Add(meta.IsOn ? L.F("{0} on now", meta.Name) : $"{meta.Name} {When(meta.Wait)}");

            if (meta is { IsOn: true, Then: var (then, wait) }) parts.Add($"{then} {When(wait)}");
        }

        foreach (var (name, wait) in facts.Bosses) parts.Add($"{name} {When(wait)}");

        if (parts.Count > 0) return Prefix + string.Join(" · ", parts);

        // Only a claim when something was read and found done. Nothing read is no line at all.
        var anythingRead = facts.ChestLooted is true || facts is { HeroPoints: > 0, HeroPointsLeft: 0 };

        return anythingRead ? Prefix + L.T("nothing left") : null;
    }

    /// <summary>
    /// "Here: " in the phone's language. Its own key rather than part of a sentence, because the
    /// Here widget takes it off again to list the facts one a line; see <see cref="HereWidget.Facts"/>.
    /// </summary>
    public static string Prefix => L.T("Here:") + " ";

    private static string When(TimeSpan wait) =>
        wait <= TimeSpan.Zero
            ? L.T("up now")
            : wait.TotalMinutes < 1
                ? L.T("in under a minute")
                : wait.TotalMinutes < 60
                    ? L.F("in {0:0} min", wait.TotalMinutes)
                    : L.F("in {0}h {1:00}m", (int)wait.TotalHours, wait.Minutes);
}
