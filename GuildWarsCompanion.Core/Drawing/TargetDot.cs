using GuildWarsCompanion.Services;

namespace GuildWarsCompanion.Drawing;

/// <summary>
/// The colour a target is drawn in when there is no marker art for it.
///
/// <b>Why some kinds have no art.</b> <see cref="MapIcons"/> stamps the game's own markers, which
/// a player already reads without learning anything — but /v2/files publishes 116 assets and none
/// of them is a chest or a jumping puzzle. The closest candidate is <c>map_adventure</c>, and
/// borrowing it would be worse than a dot: an adventure is a different game feature with its own
/// marker, so the icon would be telling the player something untrue. A world boss borrows
/// <c>map_special_event</c> because a boss genuinely *is* an event; there is no such honest
/// substitute here.
///
/// So the two imported kinds are dots, told apart by colour, and the row in the list carries the
/// word. Shared between the dial and the map for the reason everything else in this app is:
/// two screens drawing the same thing differently is a bug waiting to be reported as one.
/// </summary>
internal static class TargetDot
{
    private static readonly Color Brass = Color.FromArgb("#C9A227");
    private static readonly Color Mist = Color.FromArgb("#9BA8B8");
    private static readonly Color Ember = Color.FromArgb("#C2603C");

    /// <summary>What to fill a dot with. Mist is the fallback, which is also what a kind whose
    /// icon simply failed to download gets.</summary>
    public static Color For(TargetKind kind) => kind switch
    {
        TargetKind.WorldBoss => Ember,
        TargetKind.Chest => Brass,
        TargetKind.JumpingPuzzle => Mist,
        _ => Mist
    };
}
