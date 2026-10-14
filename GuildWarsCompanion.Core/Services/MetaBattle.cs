using System.Text.Json;
using System.Text.RegularExpressions;

namespace GuildWarsCompanion.Services;

/// <summary>
/// The gear a guide asks for, as its page writes it: English names, the way MetaBattle's editors
/// type them. Stats are prefixes without the "'s" ("Berserker"); the rest are item names.
/// </summary>
/// <param name="Stats">The stats for every slot not named in <paramref name="SlotStats"/>.</param>
/// <param name="SlotStats">Stats for single slots, keyed by the API's slot name ("Helm", "Ring1").</param>
/// <param name="Weapons">Weapon set 1 main and off hand, then set 2; null for an empty hand.</param>
/// <param name="Sigils">Sigils in the same four places as the weapons.</param>
/// <param name="IsPvp">Structured PvP gear: amulets and PvP runes, nothing the account holds.</param>
public sealed record GuideGear(
    string? Stats,
    IReadOnlyDictionary<string, string> SlotStats,
    string? Rune,
    IReadOnlyList<string?> Weapons,
    IReadOnlyList<string?> Sigils,
    string? Relic,
    bool IsPvp);

/// <summary>A build guide: what it is called, where it lives, its code and its gear.</summary>
public sealed record Guide(string Title, string? Url, string? Profession, BuildCode? Code, GuideGear? Gear);

/// <summary>
/// Reads build guides from MetaBattle (metabattle.com/wiki), which the player uses.
///
/// MetaBattle is a MediaWiki whose build pages are filled in from templates - <c>{{Skill bar}}</c>,
/// <c>{{TemplateCode}}</c>, <c>{{PvE equipment}}</c> - so the page's wikitext, asked of the wiki's
/// own API, carries the gear as named fields rather than as prose. That is what this reads. The
/// prose around it - variants, "or", footnotes - is for the player and is not parsed.
///
/// A fan site, not a data source anyone promised to keep stable: a page edited into another shape
/// yields a guide with what could still be read, and a missing equipment template simply means no
/// gear comparison.
/// </summary>
public static class MetaBattle
{
    public const string Wiki = "https://metabattle.com/wiki/";

    private static readonly HttpClient Http = Client();

    /// <summary>
    /// The page title in a MetaBattle link: "https://metabattle.com/wiki/Build:Deadeye_-_Power_DPS_Rifle"
    /// is "Build:Deadeye - Power DPS Rifle". Null for text that holds no such link.
    /// </summary>
    public static string? TitleFrom(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var match = Regex.Match(text, @"metabattle\.com/wiki/(?:index\.php\?title=)?([^\s?#&]+)", RegexOptions.IgnoreCase);
        if (!match.Success) return null;

        var title = Uri.UnescapeDataString(match.Groups[1].Value).Replace('_', ' ').Trim();

        return title.Length == 0 ? null : title;
    }

    public static string UrlFor(string title) => Wiki + Uri.EscapeDataString(title.Replace(' ', '_')).Replace("%3A", ":");

    /// <summary>
    /// Fetches a page's wikitext and reads it. Throws <see cref="HttpRequestException"/> when the
    /// wiki cannot be reached and <see cref="InvalidOperationException"/> when it has no such page.
    /// </summary>
    public static async Task<Guide> FetchAsync(string title, CancellationToken ct = default)
    {
        using var leash = CancellationTokenSource.CreateLinkedTokenSource(ct);
        leash.CancelAfter(TimeSpan.FromSeconds(20));

        var address = $"{Wiki}api.php?action=parse&format=json&redirects=1&prop=wikitext&page={Uri.EscapeDataString(title)}";

        using var answer = JsonDocument.Parse(await Http.GetStringAsync(address, leash.Token));

        if (!answer.RootElement.TryGetProperty("parse", out var parse))
            throw new InvalidOperationException(L.F("MetaBattle has no page called \"{0}\".", title));

        var found = parse.GetProperty("title").GetString() ?? title;
        var text = parse.GetProperty("wikitext").GetProperty("*").GetString() ?? string.Empty;

        return Parse(found, text) with { Url = UrlFor(found) };
    }

    /// <summary>Reads a build page's wikitext.</summary>
    public static Guide Parse(string title, string wikitext)
    {
        var text = Regex.Replace(wikitext, "<!--.*?-->", string.Empty, RegexOptions.Singleline);

        var build = Fields(text, "Build");
        var code = Fields(text, "TemplateCode") is { } template && template.GetValueOrDefault("code") is { } raw
            ? BuildCode.Find(raw)
            : BuildCode.Find(text);

        var name = title.StartsWith("Build:", StringComparison.OrdinalIgnoreCase) ? title[6..].Trim() : title;

        return new Guide(name, null, build?.GetValueOrDefault("profession"), code, Gear(text));
    }

    private static readonly (string Field, string Slot)[] Slots =
    [
        ("head", "Helm"), ("shoulders", "Shoulders"), ("chest", "Coat"), ("hands", "Gloves"),
        ("legs", "Leggings"), ("feet", "Boots"), ("backpiece", "Backpack"), ("amulet", "Amulet"),
        ("ring1", "Ring1"), ("ring2", "Ring2"), ("accessory1", "Accessory1"), ("accessory2", "Accessory2")
    ];

    private static GuideGear? Gear(string text)
    {
        var pvp = false;

        if (Fields(text, "PvE equipment") is not { } fields)
        {
            if (Fields(text, "PvP equipment") is not { } inPvp) return null;

            fields = inPvp;
            pvp = true;
        }

        var slots = new Dictionary<string, string>();

        foreach (var (field, slot) in Slots)
            if (fields.GetValueOrDefault(field) is { } stats)
                slots[slot] = stats;

        string? At(string key) => fields.GetValueOrDefault(key);

        // The equipment template's four weapon fields are not filled in one way: a pistol and
        // dagger set is "weapon1 = Pistol, weapon4 = Dagger" on one page and weapon1 and weapon2 on
        // another. The skill bar's are consistent - 1 and 2 the first set, 3 and 4 the second - so
        // the hands come from there, and each of the equipment's sigils and weapon stats goes to the
        // hand holding the same weapon.
        string?[] listed = [At("weapon1"), At("weapon2"), At("weapon3"), At("weapon4")];
        var bar = Fields(text, "Skill bar");
        string?[] hands = bar is null
            ? listed
            : [bar.GetValueOrDefault("weapon1"), bar.GetValueOrDefault("weapon2"),
                bar.GetValueOrDefault("weapon3"), bar.GetValueOrDefault("weapon4")];

        if (hands.All(hand => hand is null)) hands = (string?[])listed.Clone();

        // Two two-handers are "weapon1" and "weapon2" on a skill bar: a staff and a rifle are two
        // sets, not one with a rifle in the off hand.
        if (TwoHanded(hands[0]) && hands[1] is not null && hands[2] is null && hands[3] is null)
            (hands[1], hands[2]) = (null, hands[1]);

        // Each weapon the equipment lists takes its sigil, and a two-hander both of its set's, to
        // whichever hand holds that weapon.
        var sigils = new string?[4];
        var taken = new bool[4];

        for (var i = 0; i < 4; i++)
        {
            if (listed[i] is not { } type) continue;

            var hand = -1;

            for (var at = 0; at < 4 && hand < 0; at++)
                if (!taken[at] && string.Equals(hands[at], type, StringComparison.OrdinalIgnoreCase))
                    hand = at;

            if (hand < 0) hand = i;

            taken[hand] = true;
            sigils[hand] = At($"sigil{i + 1}");

            if (TwoHanded(type) && hand % 2 == 0 && i % 2 == 0)
                sigils[hand + 1] = At($"sigil{i + 2}");

            if (At($"weapon{i + 1}-stats") is { } own) slots[HandSlots[hand]] = own;
        }

        return new GuideGear(At("stats"), slots, At("rune"), hands, sigils, At("relic"), pvp);
    }

    private static readonly string[] HandSlots = ["WeaponA1", "WeaponA2", "WeaponB1", "WeaponB2"];

    private static readonly HashSet<string> TwoHanders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Greatsword", "Hammer", "Longbow", "Long bow", "Rifle", "Shortbow", "Short bow", "Staff", "Spear"
    };

    private static bool TwoHanded(string? weapon) => weapon is not null && TwoHanders.Contains(weapon);

    /// <summary>
    /// A template's named fields - <c>| key = value</c> - from its first use on the page, with
    /// empty values left out. Null when the page does not use the template.
    /// </summary>
    private static Dictionary<string, string>? Fields(string text, string template)
    {
        var start = Regex.Match(text, @"\{\{\s*" + Regex.Escape(template) + @"\s*(\||\n|\}\})", RegexOptions.IgnoreCase);
        if (!start.Success) return null;

        // To the template's own closing braces, stepping over any template inside a value.
        var depth = 0;
        var end = start.Index;

        for (; end < text.Length - 1; end++)
        {
            if (text[end] == '{' && text[end + 1] == '{') { depth++; end++; }
            else if (text[end] == '}' && text[end + 1] == '}' && --depth == 0) { end++; break; }
            else if (text[end] == '}' && text[end + 1] == '}') end++;
        }

        var body = text[(start.Index + 2)..Math.Min(end, text.Length)];
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var part in body.Split('|').Skip(1))
        {
            var equals = part.IndexOf('=');
            if (equals <= 0) continue;

            var key = part[..equals].Trim();
            var value = Plain(part[(equals + 1)..].TrimEnd('}').Trim());

            if (key.Length > 0 && value.Length > 0) fields[key] = value;
        }

        return fields;
    }

    /// <summary>Wiki markup in a value reduced to its words: "[[Rune|Superior Rune]]" is "Superior Rune".</summary>
    private static string Plain(string value)
    {
        value = Regex.Replace(value, @"\[\[(?:[^|\]]*\|)?([^\]]*)\]\]", "$1");
        value = Regex.Replace(value, @"'{2,}", string.Empty);

        return value.Trim();
    }

    private static HttpClient Client()
    {
        var http = new HttpClient();

        http.DefaultRequestHeaders.UserAgent.ParseAdd("GuildWarsCompanion/1.0");

        return http;
    }
}
