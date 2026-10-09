namespace GuildWarsCompanion.Services;

/// <summary>What kind of thing a target is, which is what the list groups and icons by.</summary>
public enum TargetKind
{
    Heart,
    Waypoint,
    Landmark,
    Vista,
    HeroPoint,
    WorldBoss,

    /// <summary>From a marker pack, not the API: there is no chest endpoint.</summary>
    Chest,

    /// <summary>From a marker pack, for the same reason.</summary>
    JumpingPuzzle
}

/// <summary>
/// Something the compass can point at.
/// </summary>
/// <param name="X">Continent coordinates, the same space the character's position arrives in.</param>
/// <param name="Precise">
/// Whether <paramref name="X"/> is the thing itself or only the middle of the map it is on.
///
/// True for everything the floor endpoint publishes a coordinate for. False for a world boss:
/// the API has no arena coordinate, so the needle points at the map and the screen has to say
/// so rather than imply a precision it does not have.
/// </param>
/// <param name="Id">
/// What the account's progress is keyed by, where there is such a key: a hero point's
/// "0-7", a world boss's "shadow_behemoth". Null for everything the API tracks no completion
/// for, which is most of the map — see <see cref="CompassFilter"/>.
/// </param>
/// <param name="Up">
/// How high it is in the game's metres, where something knows: a marker pack's chest does, the
/// API's points of interest do not. What lets the game draw it at its own height.
/// </param>
public sealed record CompassTarget(
    string Name, TargetKind Kind, int MapId, string MapName,
    double X, double Y, bool Precise = true, string? Note = null, string? Id = null, double? Up = null)
{
    /// <summary>
    /// Whether this is worth drawing a direction to from a given map.
    ///
    /// False only for the awkward case the map-centre fallback creates: a world boss on the map
    /// the character is already standing in. The arithmetic still works — the middle of
    /// Queensdale is a real point and a character in Queensdale is some distance from it — but
    /// the answer is noise. It says "walk towards the middle of the map you are in", which is
    /// not where the boss is and not information.
    ///
    /// Found by looking at the dial rather than by reasoning: Shadow Behemoth sat four hundred
    /// units away among the local waypoints, looking exactly like a real nearby target.
    /// </summary>
    public bool CanPointFrom(int currentMapId) => Precise || MapId != currentMapId;

    public string KindLabel => Kind switch
    {
        TargetKind.Heart => "Heart",
        TargetKind.Waypoint => "Waypoint",
        TargetKind.Landmark => "Landmark",
        TargetKind.Vista => "Vista",
        TargetKind.HeroPoint => "Hero point",
        TargetKind.Chest => "Chest",
        TargetKind.JumpingPuzzle => "Jumping puzzle",
        _ => "World boss"
    };
}

/// <summary>
/// Which map each scheduled world boss appears on.
///
/// Separate from the service that fetches, so the test project can compile it without dragging
/// in an HTTP client — the same split the rest of Services/ uses between logic and plumbing.
/// </summary>
public static class BossMaps
{
/// <summary>
/// Map ids for the places <see cref="EventSchedule"/> names, read from /v2/maps rather than
/// remembered.
///
/// A name-to-id table exists because the schedule is hardcoded prose — it says "Queensdale",
/// not 15 — and looking every name up at runtime would mean pulling the whole map list to
/// resolve eleven strings. <c>CompassTargetTests</c> asserts every scheduled location has an
/// entry, so editing the schedule cannot quietly drop a boss off this screen.
/// </summary>
private static readonly Dictionary<string, int> Ids = new(StringComparer.OrdinalIgnoreCase)
{
    ["Queensdale"] = 15,
    ["Harathi Hinterlands"] = 17,
    ["Blazeridge Steppes"] = 20,
    ["Wayfarer Foothills"] = 28,
    ["Frostgorge Sound"] = 30,
    ["Caledon Forest"] = 34,
    ["Metrica Province"] = 35,
    ["Mount Maelstrom"] = 39,
    ["Sparkfly Fen"] = 53,
    ["Bloodtide Coast"] = 73,
    ["Southsun Cove"] = 873
};

    /// <summary>The map id for a location the timetable names, or null.</summary>
    public static int? For(string location) =>
        Ids.TryGetValue(location, out var id) ? id : null;

    /// <summary>Every location this knows, for the test that keeps it in step with the schedule.</summary>
    public static IReadOnlyDictionary<string, int> All => Ids;
}

/// <summary>
/// Hiding what the player has already done, so the dial empties as they work through a map.
///
/// Only two kinds of target can be filtered, and that is a limit of the API rather than a choice:
///
/// <list type="bullet">
/// <item><b>Hero points</b> — <c>/v2/characters/{name}/heropoints</c>, keyed by the same ids the
/// floor endpoint uses. Permanent per character.</item>
/// <item><b>World bosses</b> — <c>/v2/account/worldbosses</c>, which resets at the daily reset,
/// so these come back every day by design.</item>
/// </list>
///
/// <b>Renown hearts cannot be filtered.</b> There is no endpoint for them. The full list of
/// character routes is backstory, buildtabs, core, crafting, dungeons, equipment, equipmenttabs,
/// heropoints, inventory, quests, recipes, sab, skills, specializations and training — map
/// completion is not among them, and nothing on the account side carries it either. Waypoints,
/// landmarks and vistas are in the same position.
/// </summary>
public sealed record CompassFilter(IReadOnlySet<string> Done)
{
    public static CompassFilter Nothing { get; } = new(new HashSet<string>());

    /// <summary>
    /// Whether a target should still be drawn.
    ///
    /// Anything with no <see cref="CompassTarget.Id"/> always survives: an unknown state is not
    /// a completed one, and quietly hiding a heart because its progress cannot be read would be
    /// the worst of both.
    /// </summary>
    public bool Keep(CompassTarget target) =>
        target.Id is not { Length: > 0 } id || !Done.Contains(id);

    /// <summary>Whether this kind can be filtered at all, for a screen that has to explain
    /// why the hearts never go away.</summary>
    public static bool Filterable(TargetKind kind) =>
        kind is TargetKind.HeroPoint or TargetKind.WorldBoss;
}
