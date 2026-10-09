namespace GuildWarsCompanion.Services;

/// <summary>Where a world boss is actually fought, in its map's own coordinates.</summary>
/// <param name="MapId">The map the arena is on, which has to match the schedule's: several bosses
/// share a name with an event on a later map.</param>
public sealed record BossArena(int MapId, double MapX, double MapY);

/// <summary>
/// The arena each scheduled world boss is fought in, from ArenaNet's own data.
///
/// <b>This entry retires "the API publishes no spawn coordinate".</b> That was true of everything
/// under /v2 and it is still true there — but <c>/v1/event_details.json</c> is alive, answers
/// without a key, and gives every event a <c>location.center</c> in map coordinates. It is the
/// same API family the rest of the app uses and it is ArenaNet's, so there is nothing here that
/// was recalled, guessed, or taken from somebody else's marker pack.
///
/// Fetched once on 2026-09-18 — 1.38 MB, 5417 events — and baked, because v1 is frozen: a
/// request per session would spend a megabyte and a half re-reading numbers that cannot change.
/// The event id is recorded against every row so any of them can be checked against the same
/// endpoint later:
/// <code>
/// https://api.guildwars2.com/v1/event_details.json
/// </code>
///
/// <b>Checked against something independent rather than against the arithmetic that produced
/// it.</b> Shadow Behemoth converts to (44826, 30045), which lands 207 units from Deathroot Shack
/// and 422 from the Temple of the Ages — the Godslost Swamp, which is where it spawns. A mirrored
/// y would have put it in the fields north of Beetletun instead, and looked just as plausible as
/// a pair of numbers.
///
/// See <see cref="MapSpace"/> for the conversion and <see cref="CompassTarget.Precise"/> for what
/// having one of these changes: a filled needle instead of a hollow one, a distance that means the
/// arena rather than the middle of the map, and port-then-walk advice that can be given at all.
/// </summary>
public static class BossArenas
{
    /// <summary>
    /// Keyed by the schedule's <c>ApiId</c>, the same key <c>/v2/account/worldbosses</c> uses.
    ///
    /// Every boss whose fight is one named event with one centre. The map id is carried so a row
    /// can be rejected when it does not belong to the map the schedule names — Claw of Jormag has
    /// three events by that name, two of them in Bjora Marches, and the schedule means Frostgorge
    /// Sound.
    /// </summary>
    private static readonly Dictionary<string, BossArena> Arenas = new(StringComparer.OrdinalIgnoreCase)
    {
        // Kill Admiral Taidha Covington. [242BD241-E360-48F1-A8D9-57180E146789]
        ["admiral_taidha_covington"] = new(73, -6750, 8950),
        // Slay the Shatterer [03BF176A-D59F-49CA-A311-39FC6F533F2F]
        ["the_shatterer"] = new(20, -10914, 36746),
        // Defeat the Inquest's golem Mark II. [9AA133DC-F630-4A0E-BB5D-EE34A2B306C2]
        ["inquest_golem_mark_ii"] = new(39, 36212.2, 2776.12),
        // Kill the Svanir shaman chief… [F7D9D427-5E54-4F12-977A-9809B23FBA99]
        ["svanir_shaman_chief"] = new(28, 15793.7, 13549),
        // Defeat the great jungle wurm. [C5972F64-B894-45B4-BC31-2DEEA6B7C033]
        ["great_jungle_wurm"] = new(34, -15390.1, 33189),
        // Kill the megadestroyer before it blows everyone up. [C876757A-EF3E-4FBE-A484-07FF790D9B05]
        ["megadestroyer"] = new(39, -13042.1, -8359.21),
        // Defeat Ulgoth the Modniir and his minions. [E6872A86-E434-4FC1-B803-89921FF0F6D6]
        ["modniir_ulgoth"] = new(17, 27736.2, 24605.7),
        // Defeat the Claw of Jormag. [0464CB9E-1848-4AAA-BA31-4779A959DD71] — the Frostgorge one.
        ["claw_of_jormag"] = new(30, 20098.6, 16717.5),
        // Destroy the fire elemental… [33F76E9E-0BB6-46D0-A3A9-BE4CDFC4A3A4]
        ["fire_elemental"] = new(35, -14644.5, 34932.1),
        // Defeat the shadow behemoth. [31CEBA08-E44D-472F-81B0-7143D73797F5]
        ["shadow_behemoth"] = new(15, 9835, -17597),
        // Defeat Tequatl the Sunless. [568A30CF-8512-462F-9D67-647D69BEFAED]
        ["tequatl_the_sunless"] = new(53, -20150.7, -34501.4),
        // Triple Trouble [F06787F5-CFC0-499A-954E-EC125FA71C9D]
        ["triple_trouble_wurm"] = new(73, 12991.1, -23555.1)

        // Karka Queen is deliberately absent. v1 publishes four events with her exact name, all on
        // Southsun Cove and all with different centres — she is fought at whichever settlement she
        // attacks, and which one is not known until she does. Picking one would be a precise
        // answer to a question that has four, so she keeps the map-centre fallback and the screen
        // keeps saying so. This is the same rule the hollow needle was built for.
    };

    /// <summary>
    /// The arena for a boss on the map the schedule names, or null.
    ///
    /// Null is an ordinary answer: the caller falls back to the middle of the map and says so.
    /// </summary>
    public static BossArena? For(string apiId, int mapId) =>
        Arenas.TryGetValue(apiId, out var arena) && arena.MapId == mapId ? arena : null;

    /// <summary>Every arena, for the test that keeps this table in step with the schedule.</summary>
    public static IReadOnlyDictionary<string, BossArena> All => Arenas;
}
