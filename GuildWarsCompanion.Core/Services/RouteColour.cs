using System.Globalization;

namespace GuildWarsCompanion.Services;

/// <summary>
/// What colour a pack asks a route to be drawn in.
///
/// <b>Both halves were measured, and the two packs disagree entirely.</b> Metal Marker Myriad puts
/// <c>color</c> on 251 of its trails — nine distinct values, which are the Okabe-Ito colourblind
/// safe palette — and on four categories. Teh's Trails uses none at all and instead sets
/// <c>alpha</c> on seven categories. So a route's colour comes from the trail if it has one, from
/// the nearest category above it if not, and from the app's own brass when neither says anything.
///
/// <b>Read leniently, because one value in the wild is already broken.</b> Metal carries
/// <c>#fffffff</c> — seven hex digits, which is not a colour in any reading. Anything unparseable
/// is treated as "not stated" rather than as an error: the route is still worth drawing in the
/// default, and refusing the whole pack over a typo would be absurd.
/// </summary>
public static class RouteColour
{
    /// <summary>"Not stated", which the drawing turns into the app's own brass.</summary>
    public const int None = 0;

    /// <summary>
    /// A colour as packed ARGB, or null when the text is not one.
    ///
    /// <c>#rrggbb</c> is what both packs write; the hash is optional because packs are written by
    /// hand, and eight digits are accepted as <c>#aarrggbb</c> because that is what a tool writing
    /// .NET colours produces. Six digits mean fully opaque.
    /// </summary>
    public static int? Parse(string? text)
    {
        if (text is null) return null;

        var hex = text.Trim().TrimStart('#');

        if (hex.Length is not (6 or 8)) return null;

        if (!uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
            return null;

        return hex.Length == 6 ? unchecked((int)(0xFF000000u | value)) : unchecked((int)value);
    }

    /// <summary>
    /// The alpha a pack asks for, as a fraction, or null when it says nothing.
    ///
    /// A fraction rather than a byte: packs write <c>1</c> and <c>0.7</c>. Anything outside 0 to 1
    /// is a value this cannot honour, and is treated as unstated rather than clamped — a pack
    /// writing 255 means something this reader does not understand.
    /// </summary>
    public static double? Alpha(string? text) =>
        double.TryParse(text?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
        && value is >= 0 and <= 1
            ? value
            : null;

    /// <summary>
    /// The colour to store for one trail: its own, else the nearest category above it that states
    /// one, with the nearest stated alpha applied.
    ///
    /// <b>"Nearest" is the last segment of the dotted path that has an answer</b>, walked from the
    /// most specific outwards. A pack's attributes inherit down its category tree, and a type path
    /// carries its own ancestry — so this recovers the inheritance without keeping the tree, the
    /// same trick <see cref="RouteNames"/> uses for the display names.
    /// </summary>
    public static int Resolve(
        string? trailColour,
        string type,
        IReadOnlyDictionary<string, string> categoryColours,
        IReadOnlyDictionary<string, string> categoryAlphas)
    {
        var segments = type.Split('.', StringSplitOptions.RemoveEmptyEntries);

        var colour = Parse(trailColour) ?? Nearest(segments, categoryColours, Parse);

        var alpha = Nearest(segments, categoryAlphas, Alpha);

        if (colour is null && alpha is null) return None;

        // A stated alpha with no stated colour still means something: Teh's Trails fades a
        // category to 0.7 without saying what colour it is. The drawing applies it to its own.
        var argb = colour ?? unchecked((int)0xFFC9A227);

        if (alpha is { } fraction)
            argb = unchecked((int)(((uint)(fraction * 255) << 24) | ((uint)argb & 0x00FFFFFF)));

        return argb;
    }

    private static T? Nearest<T>(
        string[] segments, IReadOnlyDictionary<string, string> stated, Func<string?, T?> read)
        where T : struct
    {
        for (var i = segments.Length - 1; i >= 0; i--)
            if (stated.TryGetValue(segments[i], out var text) && read(text) is { } value)
                return value;

        return null;
    }
}
