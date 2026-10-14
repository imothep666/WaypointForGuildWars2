using System.Text.RegularExpressions;

namespace GuildWarsCompanion.Services;

/// <summary>One specialization line in a build code: which line, and the trait taken in each
/// tier, 1 for the top, 2 the middle, 3 the bottom and 0 for none.</summary>
public sealed record BuildCodeLine(int Specialization, IReadOnlyList<int> Choices);

/// <summary>
/// A build template chat code, <c>[&amp;DQUj…]</c>, as the game copies it and as guide sites
/// print it.
///
/// The layout is the wiki's "Chat link format": a header of 13, the profession, three
/// specialization lines of two bytes each, ten skill palette ids of two bytes (terrestrial and
/// aquatic in turn: heal, three utilities, elite), sixteen bytes only rangers and revenants
/// use, and since Secrets of the Obscure the weapon types and any weapon skill overrides. A code
/// from before that ends after the sixteen bytes, and guide sites still carry many of them, so the
/// tail is optional.
///
/// What a code does not hold is anything about gear: no stats, runes, sigils or relic. Those come
/// from the guide's own page, if at all.
/// </summary>
/// <param name="Profession">1 Guardian to 9 Revenant, the same numbering as MumbleLink's.</param>
/// <param name="Palette">The ten palette ids, terrestrial and aquatic interleaved.</param>
/// <param name="Weapons">Weapon type ids, each once even when wielded twice.</param>
public sealed record BuildCode(
    int Profession,
    IReadOnlyList<BuildCodeLine> Lines,
    IReadOnlyList<int> Palette,
    IReadOnlyList<int> Weapons,
    IReadOnlyList<int> Overrides)
{
    private const byte Header = 0x0D;
    private const int FixedLength = 44;

    /// <summary>The land heal, three utilities and elite, by palette id; 0 for an empty slot.</summary>
    public int Heal => Palette[0];

    public IReadOnlyList<int> Utilities => [Palette[2], Palette[4], Palette[6]];

    public int Elite => Palette[8];

    /// <summary>The API's profession id, as <see cref="Callings"/> knows it.</summary>
    public string? ProfessionName => Callings.ProfessionFor(Profession);

    private static readonly Regex Link = new(@"\[&([A-Za-z0-9+/=]+)\]", RegexOptions.Compiled);

    /// <summary>
    /// The first build code in some text, or null. Takes a whole paste - a guide's page, a chat
    /// line - so the code does not have to be cut out by hand.
    /// </summary>
    public static BuildCode? Find(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        foreach (Match match in Link.Matches(text))
            if (Parse(match.Groups[1].Value) is { } code)
                return code;

        return null;
    }

    /// <summary>The code's base64 body, without <c>[&amp;</c> and <c>]</c>; null for anything
    /// that is not a build code.</summary>
    public static BuildCode? Parse(string base64)
    {
        byte[] bytes;

        try
        {
            bytes = Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            return null;
        }

        if (bytes.Length < FixedLength || bytes[0] != Header) return null;

        var lines = new List<BuildCodeLine>(3);

        for (var i = 0; i < 3; i++)
        {
            var traits = bytes[3 + 2 * i];
            lines.Add(new BuildCodeLine(bytes[2 + 2 * i], [traits & 3, (traits >> 2) & 3, (traits >> 4) & 3]));
        }

        var palette = new int[10];
        for (var i = 0; i < palette.Length; i++)
            palette[i] = bytes[8 + 2 * i] | (bytes[9 + 2 * i] << 8);

        var weapons = new List<int>();
        var overrides = new List<int>();
        var at = FixedLength;

        if (at < bytes.Length)
        {
            var count = bytes[at++];

            for (var i = 0; i < count && at + 1 < bytes.Length; i++, at += 2)
                weapons.Add(bytes[at] | (bytes[at + 1] << 8));

            if (at < bytes.Length)
            {
                var skills = bytes[at++];

                for (var i = 0; i < skills && at + 3 < bytes.Length; i++, at += 4)
                    overrides.Add(BitConverter.ToInt32(bytes, at));
            }
        }

        return new BuildCode(bytes[1], lines, palette, weapons, overrides);
    }

    /// <summary>
    /// A weapon type id as the API spells the type on an item ("LongBow", not "Longbow"), or null
    /// for one this does not know. From the wiki's list.
    /// </summary>
    public static string? WeaponType(int id) => id switch
    {
        5 => "Axe",
        35 => "LongBow",
        47 => "Dagger",
        49 => "Focus",
        50 => "Greatsword",
        51 => "Hammer",
        53 => "Mace",
        54 => "Pistol",
        85 => "Rifle",
        86 => "Scepter",
        87 => "Shield",
        89 => "Staff",
        90 => "Sword",
        102 => "Torch",
        103 => "Warhorn",
        107 => "ShortBow",
        265 => "Spear",
        _ => null
    };
}
