using Microsoft.Maui.Graphics;

namespace GuildWarsCompanion.Services;

/// <summary>How one part of a character stands against a guide.</summary>
public enum GuideMark
{
    /// <summary>Exactly what the guide asks for.</summary>
    Same,

    /// <summary>Not the same item or name, but the same effect: Zojja's for Berserker's.</summary>
    Equivalent,

    /// <summary>Something else is in its place.</summary>
    Different,

    /// <summary>Nothing is in its place.</summary>
    Missing,

    /// <summary>The guide or the API does not say enough to tell.</summary>
    Unknown
}

/// <summary>One compared part: what the guide asks for, what the character has, and where the
/// guide's piece can be found when the character does not wear it.</summary>
public sealed record GuideLine(string Label, string Guide, string Yours, GuideMark Mark, string? Note = null)
{
    public string MarkText => Mark switch
    {
        GuideMark.Same => L.T("same"),
        GuideMark.Equivalent => L.T("equivalent"),
        GuideMark.Different => L.T("different"),
        // In context: the plain key is a heading, filed in capitals.
        GuideMark.Missing => L.TIn("guide", "missing"),
        _ => L.T("not known")
    };

    public string Detail => L.F("Guide: {0} · You: {1}", Guide, Yours);

    public bool HasNote => Note is { Length: > 0 };

    /// <summary>The row's spine, as the vault and the material list use it: brass for something
    /// to go and do, dim for what is already right, nothing where it cannot be told.</summary>
    public Color Spine => Mark switch
    {
        GuideMark.Same or GuideMark.Equivalent => Theme.Banked,
        GuideMark.Different or GuideMark.Missing => Theme.Brass,
        _ => Colors.Transparent
    };

    public bool IsToDo => Mark is GuideMark.Different or GuideMark.Missing;
}

public sealed record GuideSection(string Heading, IReadOnlyList<GuideLine> Lines);

/// <summary>A guide held against one character.</summary>
/// <param name="Score">"14 of 19 as the guide has them".</param>
/// <param name="Notes">What was not compared, and why.</param>
public sealed record GuideReport(
    string Title, string? Url, string Score, IReadOnlyList<GuideSection> Sections, IReadOnlyList<string> Notes);

/// <summary>A specialization line with its three chosen traits by name; null for a tier left empty.</summary>
public sealed record GuideTraitLine(int Specialization, string Name, IReadOnlyList<string?> Traits);

/// <summary>A skill bar by name: heal, the three utilities, elite.</summary>
public sealed record GuideSkillBar(string? Heal, IReadOnlyList<string> Utilities, string? Elite);

/// <summary>A stat combination: its name and the attributes it raises, each with its weight. Two with
/// the same are the same stats under another name - Zojja's is Berserker's. The weights matter:
/// Assassin's raises the same three attributes as Berserker's, led by Precision instead of Power.</summary>
public sealed record StatSet(string Name, IReadOnlyList<string> Attributes)
{
    /// <summary>"Power 0.35": an attribute and its weight, as <see cref="Attributes"/> holds them.</summary>
    public static string Weighted(Gw2.ItemStatAttribute attribute) =>
        FormattableString.Invariant($"{attribute.Attribute} {attribute.Multiplier:0.###}");

    public bool SameEffect(StatSet other) =>
        Attributes.Count > 0 &&
        Attributes.Order(StringComparer.Ordinal).SequenceEqual(other.Attributes.Order(StringComparer.Ordinal));

    /// <summary>"Berserker's", "Berserker" and "berserkers" all name Berserker's.</summary>
    public static string Key(string name) =>
        new string(name.ToLowerInvariant().Where(char.IsLetter).ToArray()) is var letters && letters.EndsWith('s')
            ? letters[..^1]
            : letters;
}

/// <summary>A piece the character wears, as the comparison needs it.</summary>
/// <param name="Type">The API's item type in its details: "Rifle", "Coat", "Ring".</param>
public sealed record WornPiece(string Slot, int ItemId, string Name, string? Type, StatSet? Stats,
    IReadOnlyList<int> Upgrades, bool Legendary);

/// <summary>A piece held anywhere on the account that could fill a slot.</summary>
/// <param name="Where">"In the bank", "On Imosan", "In the legendary armory".</param>
public sealed record OwnedPiece(int ItemId, string Name, string? Type, string? Weight, StatSet? Stats, bool Legendary,
    string Where);

/// <summary>An upgrade or relic the guide names, resolved: its item, how many are held and where,
/// and what one costs to buy.</summary>
public sealed record GuideUpgrade(string Name, int? ItemId, int Held, string? Where, long? Price);

/// <summary>Everything the comparison reads, resolved through the API beforehand.</summary>
/// <param name="GuideWeapons">Weapon types, API spelling: four hands from the guide's page, or the
/// code's list of types when there is no page.</param>
/// <param name="WeaponsBySet">Whether <paramref name="GuideWeapons"/> are the four hands in order.</param>
/// <param name="GuideStats">The guide's stats per slot it names, resolved; a null value is a
/// name the API does not know.</param>
/// <param name="Weight">The armour weight this profession wears: "Medium".</param>
public sealed record GuideInput(
    Guide Guide,
    IReadOnlyList<GuideTraitLine> GuideLines,
    IReadOnlyList<GuideTraitLine> YourLines,
    GuideSkillBar GuideSkills,
    GuideSkillBar YourSkills,
    IReadOnlyList<string?> GuideWeapons,
    bool WeaponsBySet,
    IReadOnlyDictionary<string, WornPiece> Worn,
    IReadOnlyDictionary<string, (string Name, StatSet? Stats)> GuideStats,
    GuideUpgrade? Rune,
    IReadOnlyList<GuideUpgrade?> Sigils,
    GuideUpgrade? Relic,
    IReadOnlyDictionary<int, string> UpgradeNames,
    IReadOnlyList<OwnedPiece> Owned,
    string? Weight,
    IReadOnlyList<string> Notes);

/// <summary>
/// Holds a guide against a character, part by part.
///
/// Nothing here judges a build. The guide is the authority; this only says where the character
/// differs from it, and for gear, whether what the guide asks for is already somewhere on the
/// account or would have to be bought. Stats count as equivalent when they raise the same
/// attributes, which is how the game itself treats Zojja's and Berserker's.
/// </summary>
public static class GuideComparison
{
    public static readonly string[] ArmourSlots = ["Helm", "Shoulders", "Coat", "Gloves", "Leggings", "Boots"];

    public static readonly string[] TrinketSlots =
        ["Backpack", "Amulet", "Ring1", "Ring2", "Accessory1", "Accessory2"];

    private static readonly string[] Hands = ["WeaponA1", "WeaponA2", "WeaponB1", "WeaponB2"];

    public static GuideReport Report(GuideInput input)
    {
        var sections = new List<GuideSection>
        {
            new(L.T("SPECIALIZATIONS"), Lines(input.GuideLines, input.YourLines)),
            new(L.T("SKILLS"), Skills(input.GuideSkills, input.YourSkills)),
            new(L.T("WEAPONS"), Weapons(input))
        };

        if (input.Guide.Gear is { IsPvp: false })
        {
            sections.Add(new GuideSection(L.T("STATS"), Stats(input)));
            sections.Add(new GuideSection(L.T("UPGRADES"), Upgrades(input)));
        }

        sections.RemoveAll(section => section.Lines.Count == 0);

        var lines = sections.SelectMany(section => section.Lines).Where(line => line.Mark != GuideMark.Unknown).ToList();
        var right = lines.Count(line => line.Mark is GuideMark.Same or GuideMark.Equivalent);

        return new GuideReport(
            input.Guide.Title, input.Guide.Url,
            L.F("{0} of {1} as the guide has them", right, lines.Count),
            sections, input.Notes);
    }

    // --- specializations --------------------------------------------------------

    private static List<GuideLine> Lines(IReadOnlyList<GuideTraitLine> guide, IReadOnlyList<GuideTraitLine> yours)
    {
        var result = new List<GuideLine>();

        foreach (var line in guide)
        {
            var guideTraits = Traits(line);

            if (yours.FirstOrDefault(mine => mine.Specialization == line.Specialization) is not { } mine)
            {
                result.Add(new GuideLine(line.Name, guideTraits, L.T("not taken"), GuideMark.Missing));
                continue;
            }

            var differ = Enumerable.Range(0, 3)
                .Where(tier => line.Traits.ElementAtOrDefault(tier) is { } wanted &&
                               wanted != mine.Traits.ElementAtOrDefault(tier))
                .Select(tier => mine.Traits.ElementAtOrDefault(tier) is { } instead
                    ? L.F("Tier {0}: {1} instead of {2}", tier + 1, line.Traits[tier], instead)
                    : L.F("Tier {0}: {1}", tier + 1, line.Traits[tier]))
                .ToList();

            result.Add(new GuideLine(line.Name, guideTraits, Traits(mine),
                differ.Count == 0 ? GuideMark.Same : GuideMark.Different,
                differ.Count == 0 ? null : string.Join(" · ", differ)));
        }

        return result;
    }

    private static string Traits(GuideTraitLine line) =>
        string.Join(" · ", line.Traits.Select(trait => trait ?? "—"));

    // --- skills -----------------------------------------------------------------

    private static List<GuideLine> Skills(GuideSkillBar guide, GuideSkillBar yours)
    {
        var result = new List<GuideLine>();

        if (guide.Heal is { } heal)
            result.Add(Skill(L.T("Heal"), heal, yours.Heal));

        // Utilities in any order: the slot a skill sits in changes its key, not the build.
        var spare = yours.Utilities.Where(mine => !guide.Utilities.Contains(mine)).ToList();

        foreach (var utility in guide.Utilities)
        {
            if (yours.Utilities.Contains(utility))
            {
                result.Add(new GuideLine(L.TIn("guide", "Utility"), utility, utility, GuideMark.Same));
                continue;
            }

            var instead = spare.FirstOrDefault();
            if (instead is not null) spare.RemoveAt(0);

            result.Add(new GuideLine(L.TIn("guide", "Utility"), utility, instead ?? "—",
                instead is null ? GuideMark.Missing : GuideMark.Different));
        }

        if (guide.Elite is { } elite)
            result.Add(Skill(L.T("Elite"), elite, yours.Elite));

        return result;
    }

    private static GuideLine Skill(string label, string wanted, string? mine) =>
        new(label, wanted, mine ?? "—",
            mine is null ? GuideMark.Missing : mine == wanted ? GuideMark.Same : GuideMark.Different);

    // --- weapons ----------------------------------------------------------------

    private static List<GuideLine> Weapons(GuideInput input)
    {
        var worn = Hands.Select(hand => input.Worn.GetValueOrDefault(hand)?.Type).ToArray();
        var result = new List<GuideLine>();

        if (input.WeaponsBySet)
        {
            // Either of the character's sets may carry either of the guide's.
            var sets = new List<(WornPiece? Main, WornPiece? Off)>
            {
                (input.Worn.GetValueOrDefault(Hands[0]), input.Worn.GetValueOrDefault(Hands[1])),
                (input.Worn.GetValueOrDefault(Hands[2]), input.Worn.GetValueOrDefault(Hands[3]))
            };

            for (var set = 0; set < 2; set++)
            {
                var main = input.GuideWeapons.ElementAtOrDefault(set * 2);
                var off = input.GuideWeapons.ElementAtOrDefault(set * 2 + 1);

                if (main is null && off is null) continue;

                // The weapons' stats are part of the set's line, not lines of their own: compared
                // hand by hand they were held against whatever the character had in that hand,
                // which said a shortbow had the guide's stats for a dagger.
                var wantMain = main is null ? null : WantedFor(input, Hands[set * 2]);
                var wantOff = off is null ? null : WantedFor(input, Hands[set * 2 + 1]);
                var wanted = WithStats(Pair(main, off), wantMain?.Name, wantOff?.Name);

                var label = L.F("Weapon set {0}", set + 1);
                var match = sets.FindIndex(mine => Same(mine.Main?.Type, main) && Same(mine.Off?.Type, off));

                if (match >= 0)
                {
                    var (mineMain, mineOff) = sets[match];
                    sets.RemoveAt(match);

                    var yours = WithStats(Pair(main, off), mineMain?.Stats?.Name, mineOff?.Stats?.Name);
                    var marks = new[] { StatMark(mineMain, wantMain), StatMark(mineOff, wantOff) };

                    var mark = marks.Contains(GuideMark.Different) ? GuideMark.Different
                        : marks.Contains(GuideMark.Equivalent) ? GuideMark.Equivalent
                        : GuideMark.Same;

                    string? note = null;

                    if (mark == GuideMark.Different)
                    {
                        var notes = new[] { (mineMain, wantMain, Hands[set * 2]), (mineOff, wantOff, Hands[set * 2 + 1]) }
                            .Where(hand => StatMark(hand.Item1, hand.Item2) == GuideMark.Different)
                            .Select(hand => hand.Item1!.Legendary
                                ? L.T("Legendary: its stats can be changed out of combat.")
                                : OwnedFor(input, hand.Item3, hand.Item1, hand.Item2!))
                            .OfType<string>()
                            .Distinct();

                        note = string.Join(" · ", notes);
                    }

                    result.Add(new GuideLine(label, wanted, yours, mark, note));
                    continue;
                }

                var closest = sets.FirstOrDefault();
                if (sets.Count > 0) sets.RemoveAt(0);

                var missing = new[] { main, off }.OfType<string>()
                    .Where(type => !worn.Any(mine => Same(mine, type)))
                    .ToList();

                result.Add(new GuideLine(label, wanted,
                    WithStats(Pair(closest.Main?.Type, closest.Off?.Type), closest.Main?.Stats?.Name, closest.Off?.Stats?.Name),
                    closest.Main is null && closest.Off is null ? GuideMark.Missing : GuideMark.Different,
                    OwnedWeapons(input, missing)));
            }

            return result;
        }

        foreach (var type in input.GuideWeapons.OfType<string>())
        {
            var wearing = worn.Any(mine => Same(mine, type));

            result.Add(new GuideLine(WeaponName(type), WeaponName(type), wearing ? WeaponName(type) : "—",
                wearing ? GuideMark.Same : GuideMark.Missing,
                wearing ? null : OwnedWeapons(input, [type])));
        }

        return result;
    }

    /// <summary>The stats a hand is asked for: its own, or the guide's for everything.</summary>
    private static StatSet? WantedFor(GuideInput input, string hand) =>
        (input.GuideStats.TryGetValue(hand, out var own) ? own : input.GuideStats.GetValueOrDefault("")).Stats;

    /// <summary>How a weapon's stats stand against those asked for. Nothing asked, or a
    /// legendary, which takes any, is as the guide has it.</summary>
    private static GuideMark StatMark(WornPiece? piece, StatSet? wanted) =>
        wanted is null || piece is null ? GuideMark.Same
        : piece.Stats is not { } have ? GuideMark.Same
        : have.Name == wanted.Name ? GuideMark.Same
        : have.SameEffect(wanted) ? GuideMark.Equivalent
        : GuideMark.Different;

    /// <summary>"Rifle · Berserker's", or "Dagger / Dagger · Berserker's / Marauder's" where the hands differ.</summary>
    private static string WithStats(string weapons, string? main, string? off)
    {
        var stats = main == off || off is null ? main
            : main is null ? off
            : $"{main} / {off}";

        return stats is null ? weapons : $"{weapons} · {stats}";
    }

    private static string Pair(string? main, string? off) =>
        main is null && off is null ? "—"
        : off is null ? WeaponName(main!)
        : main is null ? $"— / {WeaponName(off)}"
        : $"{WeaponName(main)} / {WeaponName(off)}";

    private static bool Same(string? mine, string? wanted) =>
        string.Equals(mine ?? string.Empty, wanted ?? string.Empty, StringComparison.OrdinalIgnoreCase);

    /// <summary>Where on the account a weapon of each missing type is, preferring the guide's stats.</summary>
    private static string? OwnedWeapons(GuideInput input, IReadOnlyList<string> types)
    {
        var wanted = input.GuideStats.GetValueOrDefault("WeaponA1").Stats ?? Default(input);

        var found = types
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(type => Best(input.Owned.Where(piece => Same(piece.Type, type)), wanted) is { } piece
                ? Holding(piece)
                : L.F("No {0} on the account", WeaponName(type)))
            .ToList();

        return found.Count == 0 ? null : string.Join(" · ", found);
    }

    /// <summary>A weapon type in words: the API's "LongBow" as "Longbow".</summary>
    public static string WeaponName(string type) =>
        BuildChecker.WeaponTypeName(type.ToLowerInvariant() switch
        {
            "longbow" => "Longbow",
            "shortbow" => "Shortbow",
            _ => type
        });

    // --- stats ------------------------------------------------------------------

    private static StatSet? Default(GuideInput input) =>
        input.GuideStats.GetValueOrDefault("").Stats;

    private static List<GuideLine> Stats(GuideInput input)
    {
        var result = new List<GuideLine>();
        // Weapons' stats are on their sets' lines, under WEAPONS.
        foreach (var slot in ArmourSlots.Concat(TrinketSlots))
        {
            if (!input.GuideStats.TryGetValue(slot, out var asked) && !input.GuideStats.TryGetValue("", out asked))
                continue;

            var label = SlotName(slot);
            var mine = input.Worn.GetValueOrDefault(slot);

            if (asked.Stats is not { } wanted)
            {
                result.Add(new GuideLine(label, asked.Name, mine?.Stats?.Name ?? "—", GuideMark.Unknown,
                    L.F("\"{0}\" is not a stat combination the API knows.", asked.Name)));
                continue;
            }

            if (mine is null)
            {
                result.Add(new GuideLine(label, wanted.Name, "—", GuideMark.Missing, OwnedFor(input, slot, mine, wanted)));
                continue;
            }

            var mark = mine.Stats is not { } have ? GuideMark.Unknown
                : have.Name == wanted.Name ? GuideMark.Same
                : have.SameEffect(wanted) ? GuideMark.Equivalent
                : GuideMark.Different;

            var note = mark switch
            {
                GuideMark.Different when mine.Legendary => L.T("Legendary: its stats can be changed out of combat."),
                GuideMark.Different => OwnedFor(input, slot, mine, wanted),
                _ => null
            };

            result.Add(new GuideLine(label, wanted.Name, mine.Stats?.Name ?? mine.Name, mark, note));
        }

        return result;
    }

    /// <summary>A piece on the account that would fill a slot with the guide's stats.</summary>
    private static string? OwnedFor(GuideInput input, string slot, WornPiece? worn, StatSet wanted)
    {
        var type = SlotType(slot, worn, input);
        if (type is null) return null;

        var armour = Array.IndexOf(ArmourSlots, slot) >= 0;

        var candidates = input.Owned.Where(piece =>
            Same(piece.Type, type) && (!armour || input.Weight is null || Same(piece.Weight, input.Weight)));

        return Best(candidates, wanted) is { } found && (found.Legendary || found.Stats?.SameEffect(wanted) == true)
            ? Holding(found)
            : L.T("Not on the account");
    }

    /// <summary>The API's item type a slot takes. A weapon hand takes what the guide puts in it.</summary>
    private static string? SlotType(string slot, WornPiece? worn, GuideInput input) => slot switch
    {
        "Ring1" or "Ring2" => "Ring",
        "Accessory1" or "Accessory2" => "Accessory",
        "Amulet" => "Amulet",
        "Backpack" => "Back",
        _ when Array.IndexOf(Hands, slot) is var hand and >= 0 => input.GuideWeapons.ElementAtOrDefault(hand),
        _ => slot
    };

    /// <summary>The best of some pieces for wanted stats: the same stats, then a legendary, which
    /// can take any.</summary>
    private static OwnedPiece? Best(IEnumerable<OwnedPiece> pieces, StatSet? wanted)
    {
        var list = pieces.ToList();

        return list.FirstOrDefault(piece => wanted is not null && piece.Stats?.SameEffect(wanted) == true)
               ?? list.FirstOrDefault(piece => piece.Legendary)
               ?? list.FirstOrDefault();
    }

    private static string Holding(OwnedPiece piece) =>
        piece.Legendary
            ? L.F("{0} · {1}, any stats", piece.Where, piece.Name)
            : $"{piece.Where}: {piece.Name}";

    // --- upgrades ---------------------------------------------------------------

    private static List<GuideLine> Upgrades(GuideInput input)
    {
        var result = new List<GuideLine>();

        if (input.Rune is { } rune)
        {
            var armour = ArmourSlots.Select(slot => input.Worn.GetValueOrDefault(slot)).OfType<WornPiece>().ToList();
            var slotted = armour.Count(piece => rune.ItemId is { } id && piece.Upgrades.Contains(id));

            var mine = armour.SelectMany(piece => piece.Upgrades)
                .Select(id => input.UpgradeNames.GetValueOrDefault(id))
                .OfType<string>()
                .GroupBy(name => name)
                .OrderByDescending(group => group.Count())
                .FirstOrDefault()?.Key;

            result.Add(new GuideLine(L.T("Rune"), $"{rune.Name} × 6",
                slotted > 0 ? $"{rune.Name} × {slotted}" : mine ?? "—",
                rune.ItemId is null ? GuideMark.Unknown : slotted >= 6 ? GuideMark.Same : slotted > 0 || mine is null ? GuideMark.Missing : GuideMark.Different,
                slotted >= 6 ? null : Supply(rune, 6 - slotted)));
        }

        for (var hand = 0; hand < 4; hand++)
        {
            if (input.Sigils.ElementAtOrDefault(hand) is not { } sigil) continue;

            // A two-handed weapon carries both of its set's sigils, and names the second too.
            var set = hand / 2;
            var weapon = input.GuideWeapons.ElementAtOrDefault(hand) ?? input.GuideWeapons.ElementAtOrDefault(set * 2);
            var weapons = Hands.Skip(set * 2).Take(2).Select(slot => input.Worn.GetValueOrDefault(slot)).OfType<WornPiece>();
            var slotted = weapons.Any(piece => sigil.ItemId is { } id && piece.Upgrades.Contains(id));

            result.Add(new GuideLine(weapon is null ? L.T("Sigil") : L.F("Sigil, {0}", WeaponName(weapon)),
                sigil.Name, slotted ? sigil.Name : "—",
                sigil.ItemId is null ? GuideMark.Unknown : slotted ? GuideMark.Same : GuideMark.Missing,
                slotted ? null : Supply(sigil, 1)));
        }

        if (input.Relic is { } relic)
        {
            var mine = input.Worn.GetValueOrDefault("Relic");

            result.Add(new GuideLine(L.T("Relic"), relic.Name, mine?.Name ?? L.T("not shown by the API"),
                mine is null ? GuideMark.Unknown : mine.ItemId == relic.ItemId ? GuideMark.Same : GuideMark.Different,
                // Unseen, a relic is not called missing: the price would be a guess at a purchase.
                mine is null ? (relic.Held > 0 ? relic.Where : null)
                : mine.ItemId == relic.ItemId ? null : Supply(relic, 1)));
        }

        return result;
    }

    /// <summary>"6 in the bank", or what the rest would cost, or that the name is not known.</summary>
    private static string Supply(GuideUpgrade upgrade, int needed)
    {
        if (upgrade.ItemId is null) return L.F("\"{0}\" is not an item the app could find.", upgrade.Name);

        if (upgrade.Held >= needed && upgrade.Where is { } where) return where;

        var buy = needed - upgrade.Held;
        var held = upgrade.Held > 0 && upgrade.Where is { } some ? some + " · " : string.Empty;

        return upgrade.Price is { } price
            ? held + L.F("{0} to buy, {1}", buy, Coin.Format(price * buy))
            : held + L.F("{0} more needed, not on the trading post", buy);
    }

    public static string SlotName(string slot) => slot switch
    {
        "Helm" => L.T("Head"),
        "Shoulders" => L.T("Shoulders"),
        "Coat" => L.TIn("slot", "Chest"),
        "Gloves" => L.T("Hands"),
        "Leggings" => L.T("Legs"),
        "Boots" => L.T("Feet"),
        "Backpack" => L.T("Back"),
        "Amulet" => L.T("Amulet"),
        "Ring1" => L.T("Ring 1"),
        "Ring2" => L.T("Ring 2"),
        "Accessory1" => L.T("Accessory 1"),
        "Accessory2" => L.T("Accessory 2"),
        "WeaponA1" => L.T("Main hand"),
        "WeaponA2" => L.T("Off hand"),
        "WeaponB1" => L.T("Main hand (swap)"),
        "WeaponB2" => L.T("Off hand (swap)"),
        _ => slot
    };
}
