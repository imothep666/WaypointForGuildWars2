using System.Text.Json;
using GuildWarsCompanion.Data;
using GuildWarsCompanion.Gw2;

namespace GuildWarsCompanion.Services;

/// <summary>
/// Holds a build guide against a character: a build code, or a MetaBattle page with its gear.
///
/// The work is in resolving both sides into names the comparison can hold side by side. A code is
/// numbers - palette ids, trait choices by position - and a guide's gear is English names; the
/// character is item and stat ids. So the code goes through /v2/professions' palette table and
/// /v2/specializations, the gear's stat names through /v2/itemstats in English, and its rune,
/// sigils and relic through the Guild Wars 2 Wiki, which knows every item's id by its English
/// name - the API has no search by name.
///
/// Where a piece is on the account comes from <see cref="StorageService"/>, the other equipment
/// templates of every character, and the legendary armory.
/// </summary>
public sealed class GuideService(
    Gw2Client client, CharacterService characters, StorageService storage, ItemLookup items,
    ApiKeySession session, OfflineReader offline, SnapshotStore snapshots)
{
    private static readonly HttpClient Wiki = WikiClient();

    private readonly Dictionary<string, int?> _ids = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Reads what was pasted: a MetaBattle link, or anything holding a build code. Throws
    /// <see cref="ArgumentException"/> with a sentence for the player when it is neither.
    /// </summary>
    public static async Task<Guide> ReadAsync(string text, CancellationToken ct = default)
    {
        if (MetaBattle.TitleFrom(text) is { } title)
        {
            try
            {
                return await MetaBattle.FetchAsync(title, ct);
            }
            catch (HttpRequestException)
            {
                throw new ArgumentException(L.T("MetaBattle could not be reached."));
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new ArgumentException(L.T("MetaBattle could not be reached."));
            }
            catch (InvalidOperationException ex)
            {
                throw new ArgumentException(ex.Message);
            }
        }

        if (BuildCode.Find(text) is { } code) return new Guide(L.T("Build code"), null, null, code, null);

        throw new ArgumentException(L.T("Paste a build code, [&D…], or a link to a MetaBattle build."));
    }

    /// <summary>
    /// The guide last compared with a character, as it was pasted, so the comparison is there again
    /// after a session spent gathering what it said was missing.
    /// </summary>
    public async Task<string?> RecallAsync(string characterName, CancellationToken ct = default) =>
        (await snapshots.LoadAsync(MemoKey(characterName), Gw2JsonContext.Default.ListString, ct))?.Value.FirstOrDefault();

    public Task RememberAsync(string characterName, string text, CancellationToken ct = default) =>
        snapshots.SaveAsync(MemoKey(characterName), [text], Gw2JsonContext.Default.ListString, ct);

    private static string MemoKey(string characterName) => $"guide/last/{characterName}";

    public async Task<GuideReport> CompareAsync(Guide guide, string characterName, CancellationToken ct = default)
    {
        var roster = await characters.AllAsync(ct);
        var character = roster.FirstOrDefault(c => c.Name == characterName)
                        ?? throw new ArgumentException(L.T("That character is no longer on the account."));

        var notes = new List<string>();

        if (guide.Code is not { } code)
            throw new ArgumentException(L.T("The guide has no build code to compare with."));

        if (code.ProfessionName is { } profession &&
            !profession.Equals(character.Profession, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException(L.F("This is a {0} build, and {1} is a {2}.",
                L.T(profession), character.Name, L.T(character.Profession)));

        var revenant = character.Profession.Equals("Revenant", StringComparison.OrdinalIgnoreCase);

        var (guideLines, yourLines) = await LinesAsync(code, character, ct);
        var (guideSkills, yourSkills) = revenant ? (new GuideSkillBar(null, [], null), new GuideSkillBar(null, [], null))
            : await SkillsAsync(code, character, ct);

        if (revenant) notes.Add(L.T("A revenant's skills come with its legends, which are not compared."));
        if (character.Profession.Equals("Ranger", StringComparison.OrdinalIgnoreCase))
            notes.Add(L.T("Pets are not compared."));

        var gear = guide.Gear;
        var bySet = gear is not null && gear.Weapons.Any(weapon => weapon is not null);

        IReadOnlyList<string?> weapons = bySet
            ? gear!.Weapons.Select(weapon => weapon is null ? null : ApiWeaponType(weapon)).ToList()
            : code.Weapons.Select(BuildCode.WeaponType).ToList();

        // What the character wears now: the equipment template in use.
        var tab = Loadouts.Pick(character.EquipmentTabs, character.ActiveEquipmentTab);
        var worn = tab is not null
            ? Loadouts.Worn(tab)
            : (character.Equipment ?? []).Where(piece => piece.IsWorn && piece.Slot is not null).ToList();

        var stats = await StatsAsync(ct);

        var upgradeIds = worn.SelectMany(piece => piece.Upgrades ?? []).Distinct().ToList();
        var details = await DetailsAsync(worn.Select(piece => piece.Id).Concat(upgradeIds), ct);

        var wornPieces = worn.ToDictionary(piece => piece.Slot!, piece => Piece(piece, details, stats.Local));
        var upgradeNames = upgradeIds.ToDictionary(id => id, id => details.GetValueOrDefault(id)?.Name ?? L.F("Item {0}", id));

        var guideStats = new Dictionary<string, (string, StatSet?)>();
        GuideUpgrade? rune = null, relic = null;
        IReadOnlyList<GuideUpgrade?> sigils = [];
        IReadOnlyList<OwnedPiece> owned = [];

        if (gear is { IsPvp: true })
            notes.Add(L.T("PvP gear is chosen in the PvP lobby and is not compared."));

        // The account's holdings, wherever they are, for weapons and for the guide's gear.
        if (bySet || gear is { IsPvp: false } || weapons.Count > 0)
            owned = await OwnedAsync(roster, character, stats.Local, notes, ct);

        if (gear is { IsPvp: false })
        {
            if (gear.Stats is { } every) guideStats[""] = (every, stats.Find(every));

            foreach (var (slot, name) in gear.SlotStats)
                guideStats[slot] = (name, stats.Find(name));

            var held = await ArmoryAsync(ct);

            rune = gear.Rune is { } runeName ? await UpgradeAsync(runeName, held, ct) : null;
            relic = gear.Relic is { } relicName ? await UpgradeAsync(relicName, held, ct) : null;

            var list = new List<GuideUpgrade?>();
            foreach (var sigil in gear.Sigils)
                list.Add(sigil is null ? null : await UpgradeAsync(sigil, held, ct));
            sigils = list;

            var prices = await PriceAsync([rune, relic, .. sigils], ct);
            rune = Priced(rune, prices);
            relic = Priced(relic, prices);
            sigils = sigils.Select(sigil => Priced(sigil, prices)).ToList();

            notes.Add(L.T("Infusions are not compared."));
        }
        else if (gear is null)
        {
            notes.Add(L.T("A build code holds no gear. Paste the guide's MetaBattle link to compare stats, runes and sigils too."));
        }

        var weight = ArmourWeight(character.Profession);

        return GuideComparison.Report(new GuideInput(
            guide, guideLines, yourLines, guideSkills, yourSkills, weapons, bySet, wornPieces, guideStats,
            rune, sigils, relic, upgradeNames, owned, weight, notes));
    }

    // --- specializations and skills ---------------------------------------------

    private async Task<(List<GuideTraitLine> Guide, List<GuideTraitLine> Yours)> LinesAsync(
        BuildCode code, Character character, CancellationToken ct)
    {
        var mine = character.ActiveBuild?.Specializations ?? [];

        var specIds = code.Lines.Select(line => line.Specialization)
            .Concat(mine.Select(line => line.Id ?? 0))
            .Where(id => id > 0)
            .Distinct()
            .ToList();

        var specs = (await client.GetSpecializationsAsync(specIds, ct)).ToDictionary(spec => spec.Id);

        int? TraitAt(int spec, int tier, int choice) =>
            choice is >= 1 and <= 3 && specs.GetValueOrDefault(spec)?.MajorTraits is { Count: 9 } majors
                ? majors[tier * 3 + choice - 1]
                : null;

        var traitIds = code.Lines
            .SelectMany(line => line.Choices.Select((choice, tier) => TraitAt(line.Specialization, tier, choice)))
            .Concat(mine.SelectMany(line => line.Traits ?? []))
            .OfType<int>()
            .Distinct()
            .ToList();

        var traits = (await client.GetTraitsAsync(traitIds, ct)).ToDictionary(trait => trait.Id, trait => trait.Name);

        string? Named(int? id) => id is { } known ? traits.GetValueOrDefault(known, L.F("Trait {0}", known)) : null;
        string SpecName(int id) => specs.GetValueOrDefault(id)?.Name ?? L.F("Specialization {0}", id);

        var guide = code.Lines
            .Where(line => line.Specialization > 0)
            .Select(line => new GuideTraitLine(line.Specialization, SpecName(line.Specialization),
                line.Choices.Select((choice, tier) => Named(TraitAt(line.Specialization, tier, choice))).ToList()))
            .ToList();

        var yours = mine
            .Where(line => line.Id is > 0)
            .Select(line => new GuideTraitLine(line.Id!.Value, SpecName(line.Id.Value),
                Enumerable.Range(0, 3).Select(tier => Named(line.Traits?.ElementAtOrDefault(tier))).ToList()))
            .ToList();

        return (guide, yours);
    }

    private async Task<(GuideSkillBar Guide, GuideSkillBar Yours)> SkillsAsync(
        BuildCode code, Character character, CancellationToken ct)
    {
        var profession = await client.GetProfessionAsync(character.Profession, ct);

        var palette = (profession?.SkillsByPalette ?? [])
            .Where(pair => pair.Count == 2)
            .GroupBy(pair => pair[0])
            .ToDictionary(group => group.Key, group => group.First()[1]);

        int? Skill(int id) => id > 0 && palette.TryGetValue(id, out var skill) ? skill : null;

        var bar = character.ActiveBuild?.Skills;

        var ids = new[] { Skill(code.Heal), Skill(code.Elite), bar?.Heal, bar?.Elite }
            .Concat(code.Utilities.Select(Skill))
            .Concat((bar?.Utilities ?? []).Select(id => (int?)id))
            .OfType<int>()
            .Distinct()
            .ToList();

        var names = (await client.GetSkillsAsync(ids, ct)).ToDictionary(skill => skill.Id, skill => skill.Name);

        string? Named(int? id) => id is { } known ? names.GetValueOrDefault(known, L.F("Skill {0}", known)) : null;

        return (
            new GuideSkillBar(Named(Skill(code.Heal)), code.Utilities.Select(Skill).Select(Named).OfType<string>().ToList(),
                Named(Skill(code.Elite))),
            new GuideSkillBar(Named(bar?.Heal), (bar?.Utilities ?? []).Select(id => Named(id)).OfType<string>().ToList(),
                Named(bar?.Elite)));
    }

    // --- stats ------------------------------------------------------------------

    /// <summary>The stat combinations in the phone's language, and a finder for the guide's English
    /// names, which matches on the English list and answers with the local one.</summary>
    private sealed record Stats(IReadOnlyDictionary<int, StatSet> Local, Func<string, StatSet?> Find);

    private async Task<Stats> StatsAsync(CancellationToken ct)
    {
        var local = (await offline.ReadAsync("guide/itemstats", Gw2JsonContext.Default.ListItemStat,
            token => client.GetAllItemStatsAsync(token), ct)).Value;

        var english = client.Language == Gw2Language.English
            ? local
            : (await offline.ReadAsync("guide/itemstats/en", Gw2JsonContext.Default.ListItemStat,
                token => client.GetAllItemStatsAsync(token, Gw2Language.English), ct)).Value;

        var sets = local
            .Where(stat => stat.Name.Length > 0)
            .ToDictionary(stat => stat.Id, stat => new StatSet(stat.Name,
                (stat.Attributes ?? []).Select(StatSet.Weighted).ToList()));

        var byName = english
            .Where(stat => stat.Name.Length > 0 && sets.ContainsKey(stat.Id))
            .GroupBy(stat => StatSet.Key(stat.Name))
            .ToDictionary(group => group.Key, group => sets[group.First().Id]);

        return new Stats(sets, name => byName.GetValueOrDefault(StatSet.Key(name)));
    }

    private static WornPiece Piece(EquipmentPiece piece, IReadOnlyDictionary<int, WeaponItem> details,
        IReadOnlyDictionary<int, StatSet> stats)
    {
        var item = details.GetValueOrDefault(piece.Id);
        var statId = piece.Stats?.Id ?? item?.Details?.InfixUpgrade?.Id;

        return new WornPiece(piece.Slot!, piece.Id, item?.Name ?? L.F("Item {0}", piece.Id), TypeOf(item),
            statId is { } id ? stats.GetValueOrDefault(id) : null, piece.Upgrades ?? [], item?.Rarity == "Legendary");
    }

    /// <summary>The type a slot is matched on: a weapon's or armour's sub-type, a trinket's kind,
    /// "Back" for a back item, whose details carry no type.</summary>
    private static string? TypeOf(WeaponItem? item) => item?.Type switch
    {
        "Back" => "Back",
        "Relic" => "Relic",
        _ => item?.Details?.Type
    };

    private async Task<Dictionary<int, WeaponItem>> DetailsAsync(IEnumerable<int> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();

        return list.Count == 0 ? [] : (await client.GetWeaponItemsAsync(list, ct)).ToDictionary(item => item.Id);
    }

    // --- what the account holds -------------------------------------------------

    /// <summary>
    /// Weapons, armour, trinkets and backs held anywhere: storage, every character's equipment
    /// templates other than the one being compared, and the legendary armory. Exotic and better
    /// only, which is what a guide asks for and keeps the request for details small.
    /// </summary>
    private async Task<List<OwnedPiece>> OwnedAsync(IReadOnlyList<Character> roster, Character character,
        IReadOnlyDictionary<int, StatSet> stats, List<string> notes, CancellationToken ct)
    {
        var found = new List<(int Id, int? Stat, string Where)>();

        try
        {
            if (!storage.HasSnapshot) await storage.RefreshAsync(ct);

            var held = storage.Holdings.Keys.ToList();
            var known = await items.ResolveAsync(held, ct);

            foreach (var id in held.Where(id => known.GetValueOrDefault(id)?.Rarity is "Exotic" or "Ascended" or "Legendary"))
                found.Add((id, null, storage.WhereIs(id) ?? string.Empty));

            if (storage.Unreadable.Count > 0)
                notes.Add(L.F("Not searched: {0}.", string.Join(", ", storage.Unreadable)));
        }
        catch (Gw2ApiException)
        {
            notes.Add(L.T("The account's storage could not be read, so only worn gear is known."));
        }

        var shown = Loadouts.Pick(character.EquipmentTabs, character.ActiveEquipmentTab)?.Tab;

        foreach (var other in roster)
            foreach (var tab in other.EquipmentTabs ?? [])
            {
                if (other.Name == character.Name && tab.Tab == shown) continue;

                foreach (var piece in tab.Equipment ?? [])
                    found.Add((piece.Id, piece.Stats?.Id, L.F("{0}, equipment {1}", other.Name, Loadouts.Label(tab))));
            }

        if (session.HasPermission(Gw2Permissions.Inventories) && session.HasPermission(Gw2Permissions.Unlocks))
        {
            try
            {
                foreach (var entry in await client.GetLegendaryArmoryAsync(ct))
                    found.Add((entry.Id, null, L.T("Legendary armory")));
            }
            catch (Gw2ApiException)
            {
                // The armory is a bonus; the rest still stands.
            }
        }

        var details = await DetailsAsync(found.Select(piece => piece.Id), ct);

        return found
            .Where(piece => details.ContainsKey(piece.Id))
            .Select(piece =>
            {
                var item = details[piece.Id];
                var statId = piece.Stat ?? item.Details?.InfixUpgrade?.Id;

                return new OwnedPiece(piece.Id, item.Name ?? L.F("Item {0}", piece.Id), TypeOf(item),
                    item.Details?.WeightClass, statId is { } id ? stats.GetValueOrDefault(id) : null,
                    item.Rarity == "Legendary", piece.Where);
            })
            .Where(piece => piece.Type is not null)
            .ToList();
    }

    /// <summary>Upgrades and relics in the legendary armory, by id: there, one is always to hand.</summary>
    private async Task<HashSet<int>> ArmoryAsync(CancellationToken ct)
    {
        if (!session.HasPermission(Gw2Permissions.Inventories) || !session.HasPermission(Gw2Permissions.Unlocks))
            return [];

        try
        {
            return (await client.GetLegendaryArmoryAsync(ct)).Select(entry => entry.Id).ToHashSet();
        }
        catch (Gw2ApiException)
        {
            return [];
        }
    }

    private async Task<GuideUpgrade> UpgradeAsync(string name, HashSet<int> armory, CancellationToken ct)
    {
        var id = await IdOfAsync(name, ct);

        if (id is not { } known) return new GuideUpgrade(name, null, 0, null, null);

        var local = (await items.ResolveAsync([known], ct)).GetValueOrDefault(known)?.Name ?? name;

        // A legendary rune or sigil in the armory is the guide's item, and enough of it.
        if (armory.Contains(known)) return new GuideUpgrade(local, known, int.MaxValue, L.T("Legendary armory"), null);

        return new GuideUpgrade(local, known, storage.HeldCount(known) ?? 0, storage.WhereIs(known), null);
    }

    private async Task<Dictionary<int, long>> PriceAsync(IEnumerable<GuideUpgrade?> upgrades, CancellationToken ct)
    {
        var ids = upgrades.Select(upgrade => upgrade?.ItemId).OfType<int>().Distinct().ToList();
        if (ids.Count == 0) return [];

        try
        {
            return (await client.GetPricesAsync(ids, ct))
                .Where(price => price.Sells is { UnitPrice: > 0 })
                .ToDictionary(price => price.Id, price => (long)price.Sells!.UnitPrice);
        }
        catch (Gw2ApiException)
        {
            return [];
        }
    }

    private static GuideUpgrade? Priced(GuideUpgrade? upgrade, IReadOnlyDictionary<int, long> prices) =>
        upgrade is { ItemId: { } id } && prices.TryGetValue(id, out var price) ? upgrade with { Price = price } : upgrade;

    /// <summary>
    /// An item's id from its English name, asked of the Guild Wars 2 Wiki's semantic data, which
    /// files every item's game id under its name. Kept for the session; null when the wiki has no
    /// such item or cannot be reached.
    /// </summary>
    private async Task<int?> IdOfAsync(string name, CancellationToken ct)
    {
        if (_ids.TryGetValue(name, out var kept)) return kept;

        int? id = null;

        try
        {
            var query = Uri.EscapeDataString($"[[Has canonical name::{name}]]|?Has game id");
            using var answer = JsonDocument.Parse(await Wiki.GetStringAsync(
                $"https://wiki.guildwars2.com/api.php?action=ask&format=json&query={query}", ct));

            // The item's own page; its recipes are filed beside it as "#recipe1", with no id.
            if (answer.RootElement.GetProperty("query").GetProperty("results") is { ValueKind: JsonValueKind.Object } results)
                foreach (var page in results.EnumerateObject().Where(page => !page.Name.Contains('#')))
                    if (page.Value.GetProperty("printouts").GetProperty("Has game id") is { ValueKind: JsonValueKind.Array } ids &&
                        ids.GetArrayLength() > 0)
                    {
                        id = ids[0].GetInt32();
                        break;
                    }
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException
                                       || ex is TaskCanceledException && !ct.IsCancellationRequested)
        {
            return null;
        }

        _ids[name] = id;
        return id;
    }

    /// <summary>A weapon as a guide writes it - "rifle", "Short bow" - in the API's spelling.</summary>
    public static string ApiWeaponType(string weapon) =>
        weapon.Replace(" ", string.Empty).ToLowerInvariant() switch
        {
            "longbow" => "LongBow",
            "shortbow" => "ShortBow",
            "greatsword" => "Greatsword",
            "warhorn" => "Warhorn",
            var other => char.ToUpperInvariant(other[0]) + other[1..]
        };

    public static string? ArmourWeight(string profession) => profession switch
    {
        "Guardian" or "Warrior" or "Revenant" => "Heavy",
        "Engineer" or "Ranger" or "Thief" => "Medium",
        "Elementalist" or "Mesmer" or "Necromancer" => "Light",
        _ => null
    };

    private static HttpClient WikiClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

        http.DefaultRequestHeaders.UserAgent.ParseAdd("GuildWarsCompanion/1.0");

        return http;
    }
}
