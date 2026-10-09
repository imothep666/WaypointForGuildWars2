using System.Text.Json.Serialization;

namespace GuildWarsCompanion.Bridge.Protocol;

/// <summary>
/// The game's own UI state bitmask, as MumbleLink reports it.
///
/// <see cref="GameHasFocus"/> and <see cref="InCombat"/> are the two worth having: between them
/// they answer "is this person playing right now", which the companion app currently guesses at
/// with a clock.
/// </summary>
[Flags]
public enum UiState
{
    None = 0,
    MapOpen = 1,
    CompassTopRight = 2,
    CompassRotating = 4,
    GameHasFocus = 8,
    Competitive = 16,
    TextboxFocused = 32,
    InCombat = 64
}

/// <summary>
/// What the game is doing right now, as read from MumbleLink on the PC.
///
/// Shared with the phone app rather than duplicated: the bridge reads it, the phone consumes it
/// as JSON, and one definition keeps the two from disagreeing about what a field means. Only the
/// bridge knows how to get it out of shared memory — MumbleLink.cs is deliberately not shared,
/// because nothing on Android can read a Windows memory mapping.
///
/// Everything here is a fact the Guild Wars 2 HTTP API cannot give at all. Two of them —
/// which character is being played, and which map — are written down in ROADMAP.md as settled
/// impossibilities, and were, right up until something ran on the same PC as the game.
/// </summary>
public sealed record GameState(
    uint Tick,
    string CharacterName,
    int ProfessionId,
    int SpecializationId,
    int RaceId,
    bool IsCommander,
    int MapId,
    long WorldId,
    int MapType,
    int MountId,
    UiState Ui,
    float PlayerX,
    float PlayerY,
    float FrontX = 0,
    float FrontZ = 0,
    // The game client's own process id. Bridge-side only: it is what tells one game session
    // from the next when the tick merely stalls, and the phone has no use for it.
    [property: JsonIgnore] uint ProcessId = 0)
{
    /// <summary>
    /// Which way the character faces, in radians, clockwise from map north.
    ///
    /// The ground-plane half of fAvatarFront turned into a bearing. MumbleLink's world is
    /// Y-up metres with X growing east and Z growing <em>north</em>, while the continent
    /// coordinates beside it grow east and south — so map north is +Z, and the bearing is the
    /// angle from +Z towards +X.
    ///
    /// The Z sign was the other way round until it was checked in Tyria, and the check is worth
    /// writing down because the wrong version looked entirely reasonable. Assuming north was -Z
    /// mirrors the heading about the east-west axis: it reports 180 - the true bearing. Near
    /// east or west that is a small error which reads as a miscalibration rather than a mirror —
    /// a character running east at 73 degrees was drawn as facing 102 — and the dial stays
    /// self-consistent throughout, because every mark and the whole trail turn with it. It shows
    /// up as the objective and the walked trail sitting at the same wrong angle while the marks
    /// themselves are in exactly the right places.
    ///
    /// Measured from a compass screenshot rather than by eye: the marks were matched against
    /// /v2/continents for the map, which pins the character's position and the dial's rotation,
    /// and the rotation came out as 180 minus the direction the character was actually walking.
    ///
    /// Zero when the bridge is older than this field, which is a real case while a phone and a
    /// PC are on different versions. A needle stuck at north is the cost, not a crash.
    /// </summary>
    [JsonIgnore]
    public double HeadingRadians => Math.Atan2(FrontX, FrontZ);

    /// <summary>Whether a heading was actually reported, as opposed to defaulted. A zero vector
    /// is what an older bridge sends and is not a direction.</summary>
    [JsonIgnore]
    public bool HasHeading => FrontX != 0 || FrontZ != 0;

    [JsonIgnore] public bool InCombat => Ui.HasFlag(UiState.InCombat);

    /// <summary>
    /// The game is the window being typed into. Not the same as the game merely running, which
    /// is the distinction that makes this worth reading at all.
    /// </summary>
    [JsonIgnore] public bool HasFocus => Ui.HasFlag(UiState.GameHasFocus);

    [JsonIgnore] public bool IsMounted => MountId > 0;

    /// <summary>Whether the position and the facing are numbers. JSON has no NaN or infinity, so a
    /// state with one cannot be sent at all; see GameWatch.Poll.</summary>
    [JsonIgnore]
    public bool IsFinite =>
        float.IsFinite(PlayerX) && float.IsFinite(PlayerY) && float.IsFinite(FrontX) && float.IsFinite(FrontZ);

    [JsonIgnore] public string Profession => Named(Professions, ProfessionId - 1);

    [JsonIgnore] public string Race => Named(Races, RaceId);

    [JsonIgnore] public string Mount => Named(Mounts, MountId);

    /// <summary>"Imothep · Ranger" — what a phone screen would lead with.</summary>
    [JsonIgnore]
    public string Line =>
        CharacterName.Length == 0 ? Profession : $"{CharacterName} · {Profession}";

    /// <summary>
    /// An id outside the documented range gives "Unknown" rather than throwing or picking the
    /// nearest. A new mount ships with every expansion and this table will be behind at least
    /// once; being behind should read as ignorance, not as a wrong answer.
    /// </summary>
    private static string Named(string[] names, int index) =>
        index >= 0 && index < names.Length ? names[index] : "Unknown";

    // Documented on the wiki's API:MumbleLink page, and checked against it rather than guessed:
    // professions are 1-based, races and mounts 0-based, which is exactly the sort of thing that
    // silently shows every character as the profession next door.
    private static readonly string[] Professions =
        ["Guardian", "Warrior", "Engineer", "Ranger", "Thief",
         "Elementalist", "Mesmer", "Necromancer", "Revenant"];

    private static readonly string[] Races = ["Asura", "Charr", "Human", "Norn", "Sylvari"];

    private static readonly string[] Mounts =
        ["None", "Jackal", "Griffon", "Springer", "Skimmer", "Raptor",
         "Roller Beetle", "Warclaw", "Skyscale", "Skiff", "Siege Turtle"];
}

/// <summary>
/// Whether the block is still being written to.
///
/// MumbleLink keeps its last values after Guild Wars 2 closes, so the presence of a state says
/// nothing about whether anyone is playing — the character who logged out last night is still
/// sitting there, in the map they left, apparently in combat. The only signal is uiTick, which
/// the game advances every frame and a closed game does not.
///
/// That the tick is the way to tell is not in the wiki; it is what every tool does, and it
/// follows from "all values are updated every frame" being the one documented guarantee.
/// </summary>
public readonly record struct Liveness(uint Tick, DateTimeOffset ChangedAt)
{
    /// <summary>
    /// Generous on purpose. A frame is milliseconds, but the poll is seconds and a loading
    /// screen or an alt-tabbed client can stall the tick briefly; calling that "closed" would
    /// flicker the answer for someone who is very much still playing.
    /// </summary>
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(6);

    public Liveness Advance(uint tick, DateTimeOffset now) =>
        tick == Tick ? this : new Liveness(tick, now);

    /// <summary>Never live before anything has been seen — a zero tick is absence, not stillness.</summary>
    public bool IsLiveAt(DateTimeOffset now) =>
        Tick != 0 && now - ChangedAt <= Window;
}

/// <summary>
/// What the bridge answers when asked about the game.
/// </summary>
/// <param name="State">Null when the game is not running, or has not been since the bridge started.</param>
/// <param name="IsLive">
/// Whether the game is updating the block <em>now</em>. Separate from <paramref name="State"/>
/// being present because the two are genuinely different: MumbleLink keeps its last values after
/// the game closes, so a state that exists is not a game that is running.
/// </param>
/// <param name="MapName">
/// What <see cref="GameState.MapId"/> is called, resolved against the public Guild Wars 2 API
/// by the bridge. Not part of the state proper because it is not in the block: MumbleLink gives
/// the number and only the API knows the name. Null until the answer arrives — the first poll
/// after a map change has the id and not yet the name — and null for good if this PC has no
/// network, which is why <see cref="MapLabel"/> exists rather than every screen inventing its
/// own fallback.
/// </param>
/// <param name="Arrived">
/// The objective the character last reached, as the Blish HUD module saw it: set when the module
/// says so, gone again as soon as the phone sends a trail or a clear. The phone clears its
/// objective when this names the one it is guiding to. Null from a bridge older than this.
/// </param>
public sealed record GameReport(
    GameState? State, bool IsLive, string Note, string? MapName = null, TrailArrival? Arrived = null)
{
    public static GameReport Nothing(string note) => new(null, false, note);

    /// <summary>"Wayfarer Foothills", or "Map 28" until it is known. Empty with no state at
    /// all, because there is no map to name.</summary>
    [JsonIgnore]
    public string MapLabel =>
        MapName ?? (State is null ? string.Empty : $"Map {State.MapId}");
}

/// <summary>
/// An objective reached: its map, and where it stands in MumbleLink metres, x and z — the
/// <see cref="GuidedTrail.Target"/> of the trail that led there, less its height.
/// </summary>
public sealed record TrailArrival(int MapId, double X, double Z);
