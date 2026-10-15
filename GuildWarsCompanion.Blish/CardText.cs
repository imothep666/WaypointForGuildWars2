using System;
using System.Globalization;

namespace GuildWarsCompanion.Blish;

/// <summary>
/// The numbers on the objective card, as the card writes them. Kept apart from Blish HUD so the
/// tests can compile it: what a player reads at a glance should not be left to an untested
/// rounding.
/// </summary>
public static class CardText
{
    /// <summary>
    /// Within this many metres up or down the objective counts as level with the character. A
    /// slope underfoot, a step, a crouch: less than this is noise, and the arrow would flicker
    /// between up and down while the character walks.
    /// </summary>
    public const double Level = 3;

    /// <summary>"240 m", and from a kilometre on "1.2 km", in the PC's own way of writing numbers.</summary>
    public static string Distance(double metres, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;

        return metres < 999.5
            ? Math.Round(Math.Max(0, metres)).ToString("0", culture) + " m"
            : (metres / 1000).ToString("0.0", culture) + " km";
    }

    /// <summary>
    /// How far above the character the objective is, in whole metres, negative below; null when
    /// it is about level, or when nobody knows how high it is.
    /// </summary>
    public static int? Climb(double up, bool heightKnown) =>
        !heightKnown || Math.Abs(up) < Level ? null : (int)Math.Round(up);

    /// <summary>"12 m", for beside the arrow that says which way.</summary>
    public static string ClimbText(int climb, CultureInfo? culture = null) =>
        Math.Abs(climb).ToString("0", culture ?? CultureInfo.CurrentCulture) + " m";
}
