using System.Globalization;

namespace GuildWarsCompanion.Services;

/// <summary>
/// What the dial is allowed to draw, once the account's own progress has had its say.
///
/// Two different questions, deliberately kept apart:
///
/// <list type="bullet">
/// <item><b>Kinds</b> — "I am not walking to vistas today". A blanket answer, and the same one
/// on every character, because it is about the player rather than the character.</item>
/// <item><b>Dismissals</b> — "this heart is done". Per character, because map completion is per
/// character in the game, and one character finishing a heart says nothing about another.</item>
/// </list>
///
/// This exists because <see cref="CompassFilter"/> cannot answer the second question and never
/// will: the API publishes no completion for hearts, waypoints, landmarks or vistas — the full
/// list of character routes has no map completion in it, and nothing on the account side carries
/// it either. Hiding them by hand is not a workaround for a missing call, it is the only answer
/// there is.
/// </summary>
public sealed record CompassVisibility(
    IReadOnlySet<TargetKind> HiddenKinds,
    IReadOnlySet<string> Dismissed)
{
    /// <summary>Nothing hidden: what a fresh install and every failed read should look like.</summary>
    public static CompassVisibility Everything { get; } =
        new(new HashSet<TargetKind>(), new HashSet<string>(StringComparer.Ordinal));

    public bool Shows(CompassTarget target) =>
        !HiddenKinds.Contains(target.Kind) && !Dismissed.Contains(KeyOf(target));

    public bool IsDismissed(CompassTarget target) => Dismissed.Contains(KeyOf(target));

    public bool ShowsKind(TargetKind kind) => !HiddenKinds.Contains(kind);

    /// <summary>
    /// The name a dismissal is remembered under.
    ///
    /// Coordinates rather than <see cref="CompassTarget.Id"/>, because only hero points and
    /// world bosses have an id at all and the hearts this exists for have none. Map, kind and
    /// position together are unique on a map and stable across game builds, which a name is not:
    /// a map carries several unnamed vistas and they would all collapse into one entry.
    ///
    /// Invariant formatting, and not by habit. A phone set to German writes 49063,6 where an
    /// English one writes 49063.6, so a dismissal made on one locale would be invisible after
    /// the setting changed — and the rounding here is to whole units anyway, which is finer than
    /// any two landmarks are apart.
    /// </summary>
    public static string KeyOf(CompassTarget target) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{target.MapId}:{(int)target.Kind}:{target.X:0}:{target.Y:0}");

    public CompassVisibility ShowingKind(TargetKind kind, bool shown)
    {
        var kinds = new HashSet<TargetKind>(HiddenKinds);

        if (shown) kinds.Remove(kind);
        else kinds.Add(kind);

        return this with { HiddenKinds = kinds };
    }

    public CompassVisibility Dismissing(CompassTarget target, bool dismissed)
    {
        var keys = new HashSet<string>(Dismissed, StringComparer.Ordinal);

        if (dismissed) keys.Add(KeyOf(target));
        else keys.Remove(KeyOf(target));

        return this with { Dismissed = keys };
    }

    /// <summary>
    /// Brings back everything dismissed by hand, leaving the kind switches alone.
    ///
    /// Separate from clearing the kinds because the two go wrong in different ways. A wrongly
    /// dismissed heart is invisible and unfindable — there is nothing on the dial to undo — so
    /// there has to be one button that undoes the lot.
    /// </summary>
    public CompassVisibility RestoringAll() =>
        this with { Dismissed = new HashSet<string>(StringComparer.Ordinal) };

    /// <summary>Every kind, in the order the filter sheet lists them.</summary>
    public static IReadOnlyList<TargetKind> AllKinds { get; } =
        [TargetKind.Heart, TargetKind.Waypoint, TargetKind.Landmark,
         TargetKind.Vista, TargetKind.HeroPoint, TargetKind.WorldBoss,
         TargetKind.Chest, TargetKind.JumpingPuzzle];

    /// <summary>The plural a filter row is labelled with. "Hearts", not "Heart".</summary>
    public static string Plural(TargetKind kind) => kind switch
    {
        TargetKind.Heart => L.T("Hearts"),
        TargetKind.Waypoint => L.T("Waypoints"),
        TargetKind.Landmark => L.T("Landmarks"),
        TargetKind.Vista => L.T("Vistas"),
        TargetKind.HeroPoint => L.T("Hero points"),
        TargetKind.Chest => L.T("Chests"),
        TargetKind.JumpingPuzzle => L.T("Jumping puzzles"),
        _ => L.T("World bosses")
    };
}

/// <summary>
/// Where the choices are kept.
///
/// An interface for the same reason <see cref="IBridgeStore"/> is one: the deciding in
/// <see cref="CompassVisibility"/> is worth testing and the storing is not. Read and write take the character because half of what is stored is that
/// character's business and half is not — see the remarks on <see cref="CompassVisibility"/>.
/// </summary>
public interface ICompassVisibilityStore
{
    CompassVisibility Read(string character);

    void Write(string character, CompassVisibility visibility);
}
