using GuildWarsCompanion.Data;
using GuildWarsCompanion.Gw2;

namespace GuildWarsCompanion.Services;

/// <summary>One character in the roster.</summary>
public sealed record CharacterRow(
    string Name, string Description, string Playtime, string Profession,
    string? Activity, string? Elite, string? IconUrl, bool IsLive = false)
{
    /// <summary>
    /// Marked only when the game itself named this character, never when the playtime guess did.
    /// The mark is what separates a fact from an inference — both can read "playing now", and
    /// only one of them is worth trusting enough to draw attention to.
    /// </summary>
    public Color LiveColor => IsLive ? Theme.Brass : Colors.Transparent;

    public Color ProfessionColor => Theme.ProfessionColor(Profession);

    /// <summary>
    /// The description, plus the elite specialization and what we can infer about the
    /// character being played. The elite spec is how players actually name a build —
    /// "Reaper", not "Necromancer with three specialization lines".
    /// </summary>
    public string Subtitle
    {
        get
        {
            var parts = new List<string> { Elite is null ? Description : $"{Description} · {Elite}" };
            if (Activity is not null) parts.Add(Activity);
            return string.Join(" · ", parts);
        }
    }
}

/// <summary>One skill on the bar. Empty slots are drawn, because an empty slot is a fact.</summary>
public sealed record SkillTile(string Slot, string Name, string? IconUrl, string? Description)
{
    public bool IsFilled => IconUrl is not null;
    public Color EdgeColor => IsFilled ? Theme.Brass : Theme.EmptySlot;
}

/// <summary>One specialization line: the line itself and the three traits taken in it.</summary>
public sealed record SpecializationRow(string Name, bool IsElite, string Traits);

/// <summary>
/// One trait in the grid.
///
/// Minor traits are not chosen — every specialization grants all three — so they are never
/// dimmed. A major that was not taken is still shown, because the grid is only legible as a
/// whole: three of nine lit is the shape of a build.
/// </summary>
public sealed record TraitTile(
    int Id, string Name, string? IconUrl, string? Description, bool Chosen, bool IsMinor)
{
    public double Fade => Chosen || IsMinor ? 1 : 0.25;

    public Color Edge => Chosen ? Theme.Brass : Theme.EmptySlot;

    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
}

/// <summary>
/// One tier: the minor it grants, and the three majors to choose between.
///
/// Grouped this way because that is how the game lays it out — the minor sits to the left of
/// the tier it belongs to — and because a flat list of twelve would lose which three compete.
/// </summary>
public sealed record TraitTier(TraitTile Minor, IReadOnlyList<TraitTile> Majors);

/// <summary>A specialization as the game draws it: art, three tiers, and what was taken.</summary>
public sealed record SpecializationPanel(
    int Id, string Name, bool IsElite, string? BackgroundUrl, string? IconUrl, string Taken,
    IReadOnlyList<TraitTier> Tiers)
{
    public bool HasTaken => Taken.Length > 0;

    /// <summary>
    /// The cut banner where this build carries one, and the API's own artwork where it does
    /// not — see <see cref="SpecializationArt"/> for why the cut exists at all.
    /// </summary>
    public string? BannerSource => SpecializationArt.Banner(Id, BackgroundUrl);
}

/// <summary>One row of five skill tiles, named.</summary>
/// <param name="IsUtilityRow">The hint belongs on one row only, not repeated under every weapon
/// set. Said outright rather than read off the label, which is in the phone's language.</param>
public sealed record SkillRow(
    string Label, IReadOnlyList<SkillTile> Tiles, string? Note, bool IsUtilityRow = false);

/// <summary>A character's PvE build: both halves of the bar, plus the trait lines.</summary>
/// <param name="EliteName">The elite specialization's English name, for the wiki painting
/// behind the page; see <see cref="CharacterService"/>'s EliteArtNameAsync.</param>
public sealed record BuildView(
    IReadOnlyList<SkillRow> Rows,
    IReadOnlyList<SpecializationRow> Specializations,
    IReadOnlyList<SpecializationPanel> TraitGrid,
    string? EliteName,
    string? Notice,
    string? TemplateName);

/// <summary>The five weapon slots, in bar order.</summary>
internal static class WeaponSlots
{
    public static readonly string[] All =
        ["Weapon_1", "Weapon_2", "Weapon_3", "Weapon_4", "Weapon_5"];
}

/// <summary>
/// One character-creation choice. The journal line is short enough to list; the longer
/// first-person description is there for a tap.
/// </summary>
public sealed record BackstoryRow(string Title, string Journal, string? Description);

/// <summary>One worn item on the detail screen.</summary>
public sealed record EquipmentRow(
    string Slot, string ItemName, string? Rarity, string? IconUrl, int ItemId = 0)
{
    /// <summary>An empty slot has nothing to open.</summary>
    public bool CanOpen => ItemId > 0;

    public Color RarityColor => Theme.RarityColor(Rarity);
}

/// <summary>
/// One tile on the paper doll. <see cref="Bounds"/> is proportional in position and fixed in
/// size, which is what AbsoluteLayout needs to keep tiles tappable at any screen width.
/// </summary>
public sealed record LoadoutSlot(
    string Slot, string Label, string? IconUrl, string? Rarity, Rect Bounds, int ItemId = 0)
{
    /// <summary>An empty slot has nothing to open.</summary>
    public bool CanOpen => ItemId > 0;

    public bool IsFilled => IconUrl is not null;

    /// <summary>Filled tiles take the item's rarity; empty ones stay a quiet outline.</summary>
    public Color EdgeColor => IsFilled ? Theme.RarityColor(Rarity) : Theme.EmptySlot;
}

/// <summary>A single character, opened from the roster.</summary>
public sealed record CharacterDetail(
    string Name,
    string Subtitle,
    string Playtime,
    string Deaths,
    string Created,
    string? HeroChallenges,
    string? MasteryNote,
    IReadOnlyList<EquipmentRow> Equipment,
    string? EquipmentNotice,
    BuildView Build,
    string BodyImage,
    IReadOnlyList<LoadoutSlot> Doll,
    IReadOnlyList<LoadoutSlot> Trinkets,
    BuildCheck Check,
    ShoppingList Shopping,
    string? StatPrefix,
    IReadOnlyList<BackstoryRow> Backstory,
    IReadOnlyList<LoadoutOption> Loadouts,
    IReadOnlyList<EquipmentRow> PvpGear,
    string? PvpNotice,
    IReadOnlyList<DyeSwatch> Dyes,
    string? Profession = null);

/// <summary>One dye in a character's palette, ready to show.</summary>
public sealed record DyeSwatch(DyeUse Use)
{
    public string Name => Use.Name;

    public string UsesText => Use.UsesText;

    public bool IsRepeated => Use.IsRepeated;

    /// <summary>The colour as it comes out on this character's armour weight.</summary>
    public Color Paint => Color.FromArgb(Use.Hex);
}

/// <summary>One equipment template to switch to.</summary>
public sealed record LoadoutOption(int Tab, string Label, bool IsShown, bool IsWorn)
{
    /// <summary>Brass for the one on screen; the worn one is named instead of coloured, since
    /// looking at a tab you are not wearing is the whole point of having them.</summary>
    public Color EdgeColor => IsShown ? Theme.Brass : Theme.Banked;

    public string Caption => IsWorn ? L.T("in use") : string.Empty;

    public bool HasCaption => IsWorn;
}

/// <summary>
/// The roster and what each character is wearing.
///
/// One request covers both: /v2/characters?ids=all returns every character fully expanded,
/// so opening a character costs nothing beyond resolving its item ids against the cache.
/// </summary>
public sealed class CharacterService(
    Gw2Client client, ItemLookup lookup, ActivityStore activity,
    ApiKeySession session, BuildChecker checker, GearAdvisor advisor, OfflineReader offline)
{
    /// <summary>Slots in the order the hero panel shows them; anything unlisted sorts last.</summary>
    private static readonly string[] SlotOrder =
    [
        "Helm", "Shoulders", "Coat", "Gloves", "Leggings", "Boots",
        "WeaponA1", "WeaponA2", "WeaponB1", "WeaponB2",
        "Backpack", "Amulet", "Ring1", "Ring2", "Accessory1", "Accessory2",
        "Sickle", "Axe", "Pick", "FishingRod", "FishingLure"
    ];

    private static readonly Dictionary<string, string> SlotNames = new()
    {
        ["Helm"] = L.T("Head"),
        ["Shoulders"] = L.T("Shoulders"),
        // In context: elsewhere in the app "Chest" is a treasure chest.
        ["Coat"] = L.TIn("slot", "Chest"),
        ["Gloves"] = L.T("Hands"),
        ["Leggings"] = L.T("Legs"),
        ["Boots"] = L.T("Feet"),
        ["WeaponA1"] = L.T("Main hand"),
        ["WeaponA2"] = L.T("Off hand"),
        ["WeaponB1"] = L.T("Main hand (swap)"),
        ["WeaponB2"] = L.T("Off hand (swap)"),
        ["Backpack"] = L.T("Back"),
        ["Amulet"] = L.T("Amulet"),
        ["Ring1"] = L.T("Ring 1"),
        ["Ring2"] = L.T("Ring 2"),
        ["Accessory1"] = L.T("Accessory 1"),
        ["Accessory2"] = L.T("Accessory 2"),
        ["HelmAquatic"] = L.T("Breathing apparatus"),
        ["WeaponAquaticA"] = L.T("Aquatic weapon"),
        ["WeaponAquaticB"] = L.T("Aquatic weapon (swap)"),
        ["Sickle"] = L.T("Harvesting sickle"),
        ["Axe"] = L.T("Logging axe"),
        ["Pick"] = L.T("Mining pick"),
        ["FishingRod"] = L.T("Fishing rod"),
        ["FishingLure"] = L.T("Fishing lure")
    };

    /// <summary>The roster from the last fetch, so the detail page needs no second request.</summary>
    private List<Character> _characters = [];

    /// <summary>
    /// A character's playtime only moves while it is being played, so comparing it with the
    /// last snapshot names the character in use. That is as close as the <em>API</em> allows —
    /// there is no "logged in" flag and no position — and it stays the answer for anyone with no
    /// PC bridge. Where there is one, MumbleLink names the character outright and outranks this;
    /// <see cref="PlayedNow"/> holds the precedence.
    /// </summary>
    /// <param name="live">
    /// What the PC bridge says the game is doing. Passed in rather than fetched here: this
    /// service reads the Guild Wars 2 API and knows nothing about a bridge on somebody's desk.
    /// Left alone it is <see cref="LiveReading.Unknown"/>, which is what anyone without a
    /// bridge has, and the playtime comparison below is then the only answer there is.
    /// </param>
    /// <param name="playing">
    /// The bridge's answer, still on its way: asked alongside the roster rather than before it,
    /// because a PC that is off can take the bridge seconds to give up on, and the roster's own
    /// request is the slow part of the screen.
    /// </param>
    public async Task<Fetched<IReadOnlyList<CharacterRow>>> GetRosterAsync(
        Task<LiveReading>? playing = null, CancellationToken ct = default)
    {
        // The whole roster in one payload, which is also what the detail screen reads from —
        // so an offline copy covers both the list and tapping into a character.
        var roster = await offline.ReadAsync(
            "characters", Gw2JsonContext.Default.ListCharacter, client.GetCharactersAsync, ct);

        var live = playing is null ? default : await playing;

        _characters = roster.Value;

        var previous = await activity.LoadPlaytimeAsync(ct);

        // Largest gain wins: playing one character cannot add time to another.
        var grown = _characters
            .Where(c => previous.TryGetValue(c.Name, out var was) && c.AgeSeconds > was)
            .OrderByDescending(c => c.AgeSeconds - previous[c.Name])
            .FirstOrDefault();

        if (grown is not null)
            await activity.SaveActiveAsync(grown.Name, grown.Level, ct);

        await activity.SavePlaytimeAsync(
            _characters.Select(c => (c.Name, c.AgeSeconds)).ToList(), ct);

        var rows = await RowsAsync(_characters, live, grown?.Name, stored: false, ct);

        return new Fetched<IReadOnlyList<CharacterRow>>(rows, roster.FetchedAt);
    }

    /// <summary>
    /// The roster as last read, without the network: drawn the moment the screen opens, while
    /// <see cref="GetRosterAsync"/> asks for a fresh one. Null when nothing has been stored yet.
    /// Nothing is recorded from it - the playtime it carries is old, and comparing it would
    /// name the wrong character as the one being played.
    /// </summary>
    public async Task<IReadOnlyList<CharacterRow>?> GetStoredRosterAsync(CancellationToken ct = default)
    {
        if (await offline.StoredAsync("characters", Gw2JsonContext.Default.ListCharacter, ct) is not { Count: > 0 } kept)
            return null;

        if (_characters.Count == 0) _characters = kept;

        return await RowsAsync(kept, default, null, stored: true, ct);
    }

    /// <param name="stored">Read only what is kept, for the rows drawn before the fresh roster.</param>
    private async Task<List<CharacterRow>> RowsAsync(
        List<Character> roster, LiveReading live, string? grown, bool stored, CancellationToken ct)
    {
        var elites = await ReadElitesAsync(roster, stored, ct);
        var mastery = await ReadMasteryPointsAsync(ct, stored);

        // One request covers every profession on the roster. Professions only change with a
        // game build, so the last set answers perfectly well with no connection.
        var professions = stored
            ? await offline.StoredAsync("roster/professions", Gw2JsonContext.Default.ListProfession, ct) ?? []
            : (await offline.ReadAsync(
                "roster/professions", Gw2JsonContext.Default.ListProfession,
                token => client.GetProfessionsAsync(roster.Select(c => c.Profession), token), ct)).Value;

        var icons = professions
            .Where(p => p.Icon is not null)
            .ToDictionary(p => p.Id, p => p.Icon!);
        var active = await activity.LoadActiveAsync(ct);
        var detected = active is null ? default((string, DateTimeOffset)?) : (active.Name, active.DetectedAt);
        var now = DateTimeOffset.UtcNow;

        return roster
            .OrderByDescending(c => c.AgeSeconds)
            .Select(c => new CharacterRow(
                c.Name,
                Describe(c, mastery),
                FormatPlaytime(c.AgeSeconds),
                c.Profession,
                PlayedNow.Describe(c.Name, live, grown, detected, now),
                elites.TryGetValue(c.Name, out var elite) ? elite.Name : null,
                // The elite emblem when there is one, the profession badge otherwise.
                (elites.TryGetValue(c.Name, out var badge) ? badge.Icon : null)
                    ?? icons.GetValueOrDefault(c.Profession),
                PlayedNow.Matches(c.Name, live.Name)))
            .ToList();
    }

    /// <summary>
    /// Elite specialization per character — its name and its own icon. Every chosen
    /// specialization id across the whole roster resolves in a single request, and only the
    /// elite one is kept: an elite spec is what a build is called, and it has an emblem of
    /// its own that says more than the generic profession badge.
    /// </summary>
    private async Task<Dictionary<string, (string Name, string? Icon)>> ReadElitesAsync(
        List<Character> roster, bool stored, CancellationToken ct)
    {
        var chosen = roster
            .SelectMany(c => ChosenSpecializations(c).Select(spec => spec.Id))
            .OfType<int>()
            .Distinct()
            .ToList();

        if (chosen.Count == 0) return [];

        var specs = (stored
                ? await offline.StoredAsync("roster/specializations", Gw2JsonContext.Default.ListSpecialization, ct) ?? []
                : (await offline.ReadAsync(
                    "roster/specializations", Gw2JsonContext.Default.ListSpecialization,
                    token => client.GetSpecializationsAsync(chosen, token), ct)).Value)
            .ToDictionary(s => s.Id);

        return roster
            .Select(c => (c.Name, Elite: ChosenSpecializations(c)
                .Select(choice => choice.Id is { } id ? specs.GetValueOrDefault(id) : null)
                .FirstOrDefault(spec => spec?.Elite == true)))
            .Where(pair => pair.Elite is not null)
            .ToDictionary(pair => pair.Name, pair => (pair.Elite!.Name, pair.Elite.Icon));
    }

    /// <summary>
    /// Every character as the API gives them, from the roster already held when there is one: for
    /// a comparison that needs the raw build and every equipment template, not the screen's rows.
    /// </summary>
    public async Task<IReadOnlyList<Character>> AllAsync(CancellationToken ct = default)
    {
        if (_characters.Count == 0)
            _characters = (await offline.ReadAsync(
                "characters", Gw2JsonContext.Default.ListCharacter,
                client.GetCharactersAsync, ct)).Value;

        return _characters;
    }

    private static IReadOnlyList<SpecializationChoice> ChosenSpecializations(Character character) =>
        character.ActiveBuild?.Specializations ?? [];

    public async Task<CharacterDetail?> GetCharacterAsync(
        string name, int? loadout = null, CancellationToken ct = default)
    {
        // Deep-linked or returned to after the roster was dropped from memory.
        if (_characters.Count == 0)
            _characters = (await offline.ReadAsync(
                "characters", Gw2JsonContext.Default.ListCharacter,
                client.GetCharactersAsync, ct)).Value;

        var character = _characters.FirstOrDefault(c => c.Name == name);
        if (character is null) return null;


        // A character keeps several loadouts and wears one. The top-level equipment array is
        // all of them run together, so the gear on screen comes from a single tab instead.
        var tabs = character.EquipmentTabs;
        var shown = Loadouts.Pick(tabs, loadout ?? character.ActiveEquipmentTab);

        var worn = shown is not null
            ? Loadouts.Worn(shown)
            : (character.Equipment ?? []).Where(e => e.IsWorn && e.Slot is not null).ToList();

        var options = (tabs ?? [])
            .OrderBy(tab => tab.Tab)
            .Select(tab => new LoadoutOption(
                tab.Tab, Loadouts.Label(tab), tab.Tab == shown?.Tab, tab.IsActive))
            .ToList();

        var items = await lookup.ResolveAsync(worn.Select(e => e.Id).ToList(), ct);

        var equipment = worn
            .OrderBy(e => Array.IndexOf(SlotOrder, e.Slot) is var i && i >= 0 ? i : int.MaxValue)
            .ThenBy(e => e.Slot)
            .Select(e => new EquipmentRow(
                SlotNames.GetValueOrDefault(e.Slot!, e.Slot!),
                items.GetValueOrDefault(e.Id)?.Name ?? L.F("Item {0}", e.Id),
                items.GetValueOrDefault(e.Id)?.Rarity,
                items.GetValueOrDefault(e.Id)?.Icon,
                e.Id))
            .ToList();

        // The doll is keyed by the API's own slot names, not the friendly labels.
        var bySlot = worn.ToDictionary(e => e.Slot!, e => e);

        var doll = LoadoutLayout.PlacedSlots
            .Select(slot => BuildSlot(character.Race, slot, bySlot, items))
            .OfType<LoadoutSlot>()
            .ToList();

        var trinkets = LoadoutLayout.StripSlots
            .Select((slot, index) => BuildStripSlot(slot, index, bySlot, items))
            .ToList();

        var (pvpGear, pvpNotice) = await PvpFor(shown, ct);
        var dyes = await DyesFor(worn, character.Profession, ct);

        var build = await BuildFor(character, ct);
        var mastery = await ReadMasteryPointsAsync(ct);

        // One request covers every equipped item and every rune and sigil in them: the
        // checker needs levels and sub-types, which the item cache does not keep.
        var detailIds = worn.Select(e => e.Id)
            .Concat(worn.SelectMany(e => e.Upgrades ?? []))
            .Distinct()
            .ToList();

        var details = detailIds.Count > 0
            ? (await client.GetWeaponItemsAsync(detailIds, ct)).ToDictionary(w => w.Id)
            : [];

        var check = checker.Run(
            character,
            details,
            ChosenSpecializations(character),
            character.ActiveBuild?.Skills,
            character.Profession.Equals("Revenant", StringComparison.OrdinalIgnoreCase));

        var shopping = await advisor.BuildAsync(character, details, ct);
        var backstory = await ReadBackstoryAsync(character, ct);
        var statPrefix = await DescribeStatPrefixAsync(character, details, ct);

        return new CharacterDetail(
            character.Name,
            Describe(character, mastery),
            FormatPlaytime(character.AgeSeconds),
            character.Deaths.ToString("N0"),
            character.Created.ToString("d MMM yyyy"),
            await ReadHeroChallengesAsync(character.Name, ct),
            character.Level >= MaxLevel && mastery is not null
                ? L.T("Mastery points are account-wide, not per character.")
                : null,
            equipment,
            Loadouts.EmptyNotice(equipment.Count, options.Count),
            build,
            LoadoutLayout.ImageFor(character.Race),
            doll,
            trinkets,
            check,
            shopping,
            statPrefix,
            backstory,
            options,
            pvpGear,
            pvpNotice,
            dyes,
            character.Profession);
    }

    /// <summary>
    /// The colours the character is actually wearing.
    ///
    /// Decoration, so it degrades quietly: a character with a palette and no connection still
    /// has gear worth looking at, and an empty strip is better than an error over swatches.
    /// </summary>
    private async Task<IReadOnlyList<DyeSwatch>> DyesFor(
        IReadOnlyList<EquipmentPiece> worn, string profession, CancellationToken ct)
    {
        var ids = Dyes.Channels(worn).Distinct().ToList();

        if (ids.Count == 0) return [];

        try
        {
            var colors = (await client.GetColorsAsync(ids, ct)).ToDictionary(color => color.Id);

            return Dyes.Palette(worn, colors, profession)
                .Select(use => new DyeSwatch(use))
                .ToList();
        }
        catch (Gw2ApiException)
        {
            return [];
        }
    }

    /// <summary>
    /// The PvP half of a template: an amulet, a rune and four sigils.
    ///
    /// Amulets are their own id space — /v2/pvp/amulets, not /v2/items — which is why this
    /// could not be shown from the equipment array alone. An untouched template reports all
    /// six as null, and six empty rows would be a screen full of nothing, so it says so in a
    /// line instead.
    /// </summary>
    private async Task<(IReadOnlyList<EquipmentRow> Rows, string? Notice)> PvpFor(
        EquipmentTab? tab, CancellationToken ct)
    {
        if (!Loadouts.HasPvpGear(tab?.Pvp))
            return ([], L.T("No PvP gear set on this template."));

        var pvp = tab!.Pvp!;
        var rows = new List<EquipmentRow>();

        if (pvp.Amulet is { } amuletId)
        {
            try
            {
                var amulet = (await client.GetAmuletsAsync([amuletId], ct)).FirstOrDefault();
                rows.Add(new EquipmentRow(
                    L.T("Amulet"), amulet?.Name ?? L.F("Amulet {0}", amuletId), null, amulet?.Icon));
            }
            catch (Gw2ApiException)
            {
                rows.Add(new EquipmentRow(L.T("Amulet"), L.F("Amulet {0}", amuletId), null, null));
            }
        }

        // Runes and sigils are ordinary items, so the item cache already knows them.
        var itemIds = new[] { pvp.Rune }
            .Concat(pvp.Sigils ?? [])
            .OfType<int>()
            .ToList();

        var items = itemIds.Count == 0
            ? []
            : await lookup.ResolveAsync(itemIds, ct);

        EquipmentRow Row(string slot, int id)
        {
            var item = items.GetValueOrDefault(id);
            return new EquipmentRow(slot, item?.Name ?? L.F("Item {0}", id), item?.Rarity, item?.Icon, id);
        }

        if (pvp.Rune is { } runeId) rows.Add(Row(L.T("Rune"), runeId));

        var sigils = (pvp.Sigils ?? []).Where(sigil => sigil is not null).Select(s => s!.Value);
        rows.AddRange(sigils.Select((id, index) => Row(L.F("Sigil {0}", index + 1), id)));

        return (rows, null);
    }

    /// <summary>A tile pinned to the figure. Empty slots are still drawn: a missing helmet
    /// is information, and a doll with holes in it reads better than one with gaps.</summary>
    private LoadoutSlot? BuildSlot(
        string race, string slot,
        Dictionary<string, EquipmentPiece> worn,
        Dictionary<int, Item> items)
    {
        var position = LoadoutLayout.PositionFor(race, slot);
        if (position is null) return null;

        var piece = worn.GetValueOrDefault(slot);
        var item = piece is null ? null : items.GetValueOrDefault(piece.Id);

        // Positions name the tile's centre; AbsoluteLayout wants its top-left.
        var bounds = new Rect(position.Value.X, position.Value.Y,
            LoadoutLayout.TileSize, LoadoutLayout.TileSize);

        return new LoadoutSlot(slot, SlotNames.GetValueOrDefault(slot, slot),
            item?.Icon, item?.Rarity, bounds, piece?.Id ?? 0);
    }

    private LoadoutSlot BuildStripSlot(
        string slot, int index,
        Dictionary<string, EquipmentPiece> worn,
        Dictionary<int, Item> items)
    {
        var piece = worn.GetValueOrDefault(slot);
        var item = piece is null ? null : items.GetValueOrDefault(piece.Id);

        return new LoadoutSlot(slot, SlotNames.GetValueOrDefault(slot, slot),
            item?.Icon, item?.Rarity,
            new Rect(0, 0, LoadoutLayout.TileSize, LoadoutLayout.TileSize), piece?.Id ?? 0);
    }

    /// <summary>
    /// The build, both halves.
    ///
    /// The right half (heal, utilities, elite) is stored on the character. The left half —
    /// weapon slots 1 to 5 — is not: it follows from the profession and the weapon in hand,
    /// so it has to be derived from /v2/professions. That is the other half of the build, and
    /// the half a player actually presses.
    /// </summary>
    private async Task<BuildView> BuildFor(Character character, CancellationToken ct)
    {
        var build = character.ActiveBuild;
        var choices = ChosenSpecializations(character);
        var bar = build?.Skills;

        // A revenant's utility bar comes from its legend, so the template leaves it empty.
        var legendIds = (build?.Legends ?? []).OfType<string>().Where(l => l.Length > 0).ToList();
        var legend = legendIds.Count > 0
            ? (await client.GetLegendsAsync(legendIds, ct)).FirstOrDefault()
            : null;

        var utilityIds = new List<int?> { bar?.Heal ?? legend?.Heal };
        utilityIds.AddRange(bar?.Utilities ?? legend?.Utilities ?? [null, null, null]);
        utilityIds.Add(bar?.Elite ?? legend?.Elite);

        var weaponSets = await ResolveWeaponSetsAsync(character, ct);

        // One request covers every skill on the screen, weapon and utility alike.
        var skillIds = utilityIds
            .Concat(weaponSets.SelectMany(set => set.SkillIds))
            .OfType<int>()
            .ToList();

        var skills = skillIds.Count > 0
            ? (await client.GetSkillsAsync(skillIds, ct)).ToDictionary(s => s.Id)
            : [];

        var rows = weaponSets
            .Select(set => new SkillRow(
                set.Label,
                set.SkillIds.Select((id, i) => Tile($"Slot {i + 1}", id, skills)).ToList(),
                set.Note))
            .ToList();

        rows.Add(new SkillRow(
            L.T("Utility"),
            utilityIds
                .Select((id, i) => Tile(i == 0 ? "Heal" : i == 4 ? "Elite" : $"Utility {i}", id, skills))
                .ToList(),
            legend is not null && bar?.Heal is null ? L.T("From the equipped legend.") : null,
            IsUtilityRow: true));

        var (lines, grid) = await SpecializationsAsync(choices, character.Name, ct);

        return new BuildView(
            rows,
            lines,
            grid,
            await EliteArtNameAsync(grid.FirstOrDefault(panel => panel.IsElite), ct),
            lines.Count == 0 ? await DiagnoseAsync(ct) : null,
            string.IsNullOrWhiteSpace(build?.Name) ? null : build.Name);
    }

    /// <summary>English names of elite specializations already asked for, by id.</summary>
    private readonly Dictionary<int, string?> _eliteArtNames = [];

    /// <summary>
    /// The elite specialization as the wiki names it, for the painting behind the page. The
    /// name the page shows is the API's, in the phone's language; the wiki's files are English.
    /// Null when there is no elite, or no connection to ask with; the page then shows the
    /// profession's painting instead, as it does for a core build.
    /// </summary>
    private async Task<string?> EliteArtNameAsync(SpecializationPanel? elite, CancellationToken ct)
    {
        if (elite is null) return null;
        if (client.Language == Gw2Language.English) return elite.Name;
        if (_eliteArtNames.TryGetValue(elite.Id, out var known)) return known;

        try
        {
            return _eliteArtNames[elite.Id] = await client.GetEnglishSpecializationNameAsync(elite.Id, ct);
        }
        catch (Gw2ApiException)
        {
            return null;
        }
    }

    /// <summary>
    /// The trait lines and the grid behind them, from one pass over the same data.
    ///
    /// The grid needs every trait in the specialization rather than only the three taken —
    /// nine majors and three minors each — which is one bulk request for all of them instead
    /// of one for the nine names. Three of nine lit is what a build looks like; the three
    /// names alone do not show it.
    /// </summary>
    private async Task<(List<SpecializationRow> Lines, List<SpecializationPanel> Grid)>
        SpecializationsAsync(
            IReadOnlyList<SpecializationChoice> choices, string key, CancellationToken ct)
    {
        var specIds = choices.Select(c => c.Id).OfType<int>().ToList();

        // Reference data: a specialization changes only with a game build, so it is kept and
        // answers with no connection — the same treatment professions and stories get. The key
        // carries the character's own name because two characters rarely share a build.
        var specs = specIds.Count == 0
            ? []
            : (await offline.ReadAsync(
                    $"build/{key}/specializations", Gw2JsonContext.Default.ListSpecialization,
                    token => client.GetSpecializationsAsync(specIds, token), ct))
                .Value.ToDictionary(s => s.Id);

        var traitIds = specs.Values
            .SelectMany(spec => (spec.MinorTraits ?? []).Concat(spec.MajorTraits ?? []))
            .Concat(choices.SelectMany(c => c.Traits ?? []).OfType<int>())
            .Distinct()
            .ToList();

        var traits = traitIds.Count == 0
            ? []
            : (await offline.ReadAsync(
                    $"build/{key}/traits", Gw2JsonContext.Default.ListTrait,
                    token => client.GetTraitsAsync(traitIds, token), ct))
                .Value.ToDictionary(t => t.Id);

        var lines = new List<SpecializationRow>();
        var grid = new List<SpecializationPanel>();

        foreach (var choice in choices)
        {
            var spec = choice.Id is { } id ? specs.GetValueOrDefault(id) : null;
            if (spec is null) continue;

            var chosen = (choice.Traits ?? []).OfType<int>().ToHashSet();

            var taken = string.Join(" · ", (choice.Traits ?? [])
                .Select(t => t is { } traitId ? traits.GetValueOrDefault(traitId)?.Name : null)
                .Where(name => name is not null));

            lines.Add(new SpecializationRow(spec.Name, spec.Elite, taken));

            // Pairing the two flat lists back into tiers, guarded: see TraitGrid.
            var split = TraitGrid.Split(spec.MinorTraits, spec.MajorTraits);
            if (split.Count == 0) continue;

            var tiers = split
                .Select(tier => new TraitTier(
                    Tile(tier.Minor, traits, chosen, minor: true),
                    tier.Majors.Select(major => Tile(major, traits, chosen, minor: false)).ToList()))
                .ToList();

            // The line's own emblem, which the specialization response already carries.
            grid.Add(new SpecializationPanel(
                spec.Id, spec.Name, spec.Elite, spec.Background, spec.Icon, taken, tiers));
        }

        return (lines, grid);
    }

    private static TraitTile Tile(
        int id, IReadOnlyDictionary<int, Trait> traits, IReadOnlySet<int> chosen, bool minor)
    {
        var trait = traits.GetValueOrDefault(id);

        return new TraitTile(
            id,
            trait?.Name ?? L.F("Trait {0}", id),
            trait?.Icon,
            Prose.Clean(trait?.Description),
            chosen.Contains(id),
            minor);
    }

    private sealed record WeaponSet(string Label, IReadOnlyList<int?> SkillIds, string? Note);

    /// <summary>
    /// Slots 1 to 5 for each equipped weapon set. A two-handed weapon fills all five; a main
    /// hand fills 1 to 3 and an off hand fills 4 to 5, which is exactly how the profession
    /// data is laid out.
    /// </summary>
    private async Task<List<WeaponSet>> ResolveWeaponSetsAsync(Character character, CancellationToken ct)
    {
        var worn = (character.Equipment ?? [])
            .Where(e => e.IsWorn && e.Slot is not null)
            .ToDictionary(e => e.Slot!, e => e.Id);

        var pairs = new[]
        {
            (Label: L.T("Weapon"), Main: worn.GetValueOrDefault("WeaponA1"), Off: worn.GetValueOrDefault("WeaponA2")),
            (Label: L.T("Weapon swap"), Main: worn.GetValueOrDefault("WeaponB1"), Off: worn.GetValueOrDefault("WeaponB2"))
        }.Where(p => p.Main != 0 || p.Off != 0).ToList();

        if (pairs.Count == 0) return [];

        var profession = await client.GetProfessionAsync(character.Profession, ct);
        if (profession?.Weapons is null) return [];

        var weaponIds = pairs.SelectMany(p => new[] { p.Main, p.Off }).Where(id => id != 0).ToList();
        var types = (await client.GetWeaponItemsAsync(weaponIds, ct))
            .ToDictionary(w => w.Id, w => w.Details?.Type);

        var sets = new List<WeaponSet>();

        foreach (var pair in pairs)
        {
            var mainType = pair.Main != 0 ? types.GetValueOrDefault(pair.Main) : null;
            var offType = pair.Off != 0 ? types.GetValueOrDefault(pair.Off) : null;

            var main = mainType is not null ? profession.Weapons.GetValueOrDefault(mainType) : null;
            var off = offType is not null ? profession.Weapons.GetValueOrDefault(offType) : null;

            var twoHanded = main?.Flags?.Contains("TwoHand") == true;
            string? attunement = null;

            var ids = new List<int?>();
            for (var index = 0; index < WeaponSlots.All.Length; index++)
            {
                // Slots 1-3 come from the main hand (or the two-hander), 4-5 from the off hand.
                var source = twoHanded || index < 3 ? main : off;
                var candidates = (source?.Skills ?? [])
                    .Where(s => s.Slot == WeaponSlots.All[index])
                    .ToList();

                if (candidates.Count == 0)
                {
                    ids.Add(null);
                    continue;
                }

                // Elementalists list one entry per attunement; show one set and say which.
                if (candidates.Any(c => c.Attunement is not null))
                {
                    attunement ??= candidates[0].Attunement;
                    candidates = candidates.Where(c => c.Attunement == attunement).ToList();
                }

                // Thief slot 3 depends on the other hand: match it, else the unpaired variant.
                if (candidates.Count > 1 && candidates.Any(c => c.Offhand is not null))
                {
                    var dual = candidates.FirstOrDefault(c => c.Offhand == (offType ?? "Nothing"))
                               ?? candidates.FirstOrDefault(c => c.Offhand == "Nothing")
                               ?? candidates[0];
                    candidates = [dual];
                }

                ids.Add(candidates.Count > 0 ? candidates[0].Id : null);
            }

            var note = attunement is not null ? L.F("{0} attunement", AttunementName(attunement)) : null;
            if (ids.Any(id => id is not null))
                sets.Add(new WeaponSet(pair.Label, ids, note));
        }

        return sets;
    }

    /// <summary>
    /// Only runs when a build came back empty: reports the shape the API actually sent, so a
    /// renamed field is a one-look diagnosis instead of a guessing game.
    /// </summary>
    private async Task<string> DiagnoseAsync(CancellationToken ct)
    {
        try
        {
            var raw = await client.GetRawAsync("characters?ids=all", ct);
            using var document = System.Text.Json.JsonDocument.Parse(raw);
            var first = document.RootElement[0];

            if (!first.TryGetProperty("build_tabs", out var tabs) || tabs.GetArrayLength() == 0)
                return L.T("No build tabs returned. The API key needs the 'builds' permission.");

#if DEBUG
            var tabKeys = string.Join(", ", tabs[0].EnumerateObject().Select(x => x.Name));
            var buildKeys = tabs[0].TryGetProperty("build", out var b)
                ? string.Join(", ", b.EnumerateObject().Select(x => x.Name))
                : "(no build node)";

            return $"Unexpected build format. tab: {tabKeys} | build: {buildKeys}";
#else
            return L.T("The build came back in a format this version does not understand.");
#endif
        }
        catch (Exception ex)
        {
            return L.F("Could not read the build: {0}", ex.GetType().Name);
        }
    }

    /// <summary>
    /// The stat combination the character mostly wears, by name. Derived from the pieces
    /// themselves: fixed items name their set through `infix_upgrade`, and ascended pieces —
    /// which carry no stats of their own — name it through the `stats` chosen on the
    /// equipment entry. Reporting the majority and the odd ones out is a fact about the
    /// character, not advice about what it ought to wear.
    /// </summary>
    private async Task<string?> DescribeStatPrefixAsync(
        Character character,
        Dictionary<int, WeaponItem> details,
        CancellationToken ct)
    {
        var gearSlots = new[]
        {
            "Helm", "Shoulders", "Coat", "Gloves", "Leggings", "Boots",
            "Backpack", "Amulet", "Ring1", "Ring2", "Accessory1", "Accessory2",
            "WeaponA1", "WeaponA2"
        };

        var pieces = (character.Equipment ?? [])
            .Where(e => e.IsWorn && e.Slot is not null && gearSlots.Contains(e.Slot))
            .ToList();

        var statIds = pieces
            .Select(piece => piece.Stats?.Id ?? details.GetValueOrDefault(piece.Id)?.Details?.InfixUpgrade?.Id ?? 0)
            .Where(id => id != 0)
            .ToList();

        if (statIds.Count == 0) return null;

        var names = (await client.GetItemStatsAsync(statIds, ct)).ToDictionary(s => s.Id, s => s.Name);

        var grouped = statIds
            .Select(id => names.GetValueOrDefault(id))
            .OfType<string>()
            .GroupBy(name => name)
            .OrderByDescending(g => g.Count())
            .ToList();

        if (grouped.Count == 0) return null;

        var majority = grouped[0];
        var others = statIds.Count - majority.Count();

        return others == 0
            ? L.F("All {0} stat-bearing pieces are {1}.", majority.Count(), majority.Key)
            : L.F("{0} of {1} pieces are {2}; {3} differ.", majority.Count(), statIds.Count, majority.Key, others);
    }

    /// <summary>
    /// The answers chosen when the character was made. The character carries only answer
    /// ids; the text is static data, resolved in one request and kept in the order the
    /// character lists them so the story reads as it was authored.
    /// </summary>
    private async Task<List<BackstoryRow>> ReadBackstoryAsync(Character character, CancellationToken ct)
    {
        var ids = character.Backstory ?? [];
        if (ids.Count == 0) return [];

        var answers = (await client.GetBackstoryAnswersAsync(ids, ct)).ToDictionary(a => a.Id);

        return ids
            .Select(id => answers.GetValueOrDefault(id))
            .OfType<BackstoryAnswer>()
            .Where(a => !string.IsNullOrWhiteSpace(a.Title))
            // The API returns markup in some of these; it has to be stripped before display.
            .Select(a => new BackstoryRow(
                a.Title!,
                Prose.Clean(a.Journal) ?? string.Empty,
                Prose.Clean(a.Description)))
            .ToList();
    }

    private static SkillTile Tile(string slot, int? id, Dictionary<int, Skill> skills)
    {
        var skill = id is { } value ? skills.GetValueOrDefault(value) : null;
        return new SkillTile(slot, skill?.Name ?? L.T("empty"), skill?.Icon, Prose.Clean(skill?.Description));
    }

    /// <summary>
    /// Below 80 the level is the progress metric. At 80 there is no level left to report, so
    /// mastery points take its place — with the caveat that they are account-wide, which the
    /// detail screen spells out.
    /// </summary>
    /// <remarks>
    /// Gender, race and profession are the API's English ids, so they are named here in the
    /// phone's language. Each shape is one sentence rather than a "who" glued to a level,
    /// because German puts the profession first and the rest after it.
    /// </remarks>
    private static string Describe(Character c, int? masteryPoints)
    {
        var race = RaceName(c.Race);
        var profession = ProfessionName(c.Profession);
        var mastery = c.Level >= MaxLevel && masteryPoints is not null;

        if (string.IsNullOrEmpty(c.Gender))
            return mastery
                ? L.F("{0} {1} · {2:N0} mastery points", race, profession, masteryPoints)
                : L.F("Level {0} {1} {2}", c.Level, race, profession);

        var gender = GenderName(c.Gender);

        return mastery
            ? L.F("{0} {1} {2} · {3:N0} mastery points", gender, race, profession, masteryPoints)
            : L.F("Level {0} {1} {2} {3}", c.Level, gender, race, profession);
    }

    /// <summary>A profession id from the API, as the game names it in the phone's language.</summary>
    public static string ProfessionName(string id) => id switch
    {
        "Guardian" => L.T("Guardian"),
        "Warrior" => L.T("Warrior"),
        "Engineer" => L.T("Engineer"),
        "Ranger" => L.T("Ranger"),
        "Thief" => L.T("Thief"),
        "Elementalist" => L.T("Elementalist"),
        "Mesmer" => L.T("Mesmer"),
        "Necromancer" => L.T("Necromancer"),
        "Revenant" => L.T("Revenant"),
        _ => id
    };

    private static string RaceName(string id) => id switch
    {
        "Asura" => L.T("Asura"),
        "Charr" => L.T("Charr"),
        "Human" => L.T("Human"),
        "Norn" => L.T("Norn"),
        "Sylvari" => L.T("Sylvari"),
        _ => id
    };

    private static string GenderName(string id) => id switch
    {
        "Male" => L.T("Male"),
        "Female" => L.T("Female"),
        _ => id
    };

    /// <summary>An elementalist attunement id, named for the line under the weapon skills.</summary>
    private static string AttunementName(string id) => id switch
    {
        "Fire" => L.T("Fire"),
        "Water" => L.T("Water"),
        "Air" => L.T("Air"),
        "Earth" => L.T("Earth"),
        _ => id
    };

    private const int MaxLevel = 80;

    /// <summary>
    /// Hero challenges completed by this character.
    ///
    /// Both sources for this are broken upstream: the character's `training` array and the
    /// heropoints endpoint each return empty for accounts whose characters demonstrably have
    /// completed challenges — verified with and without a pinned schema version, so it is
    /// server-side rather than a request shape.
    ///
    /// An empty list is therefore treated as "unknown", not zero. Reporting a confident 0
    /// for a level 80 is worse than reporting nothing: the tile disappears instead.
    /// </summary>
    private async Task<string?> ReadHeroChallengesAsync(string character, CancellationToken ct)
    {
        var completed = await client.GetHeroPointsAsync(character, ct);
        return completed is null || completed.Count == 0
            ? null
            : completed.Count.ToString("N0");
    }

    /// <summary>
    /// Total mastery points earned across every region, or null if unreadable.
    ///
    /// Cached and optional. For a level 80 this is what the roster shows in place of a level,
    /// so an offline roster without it would read as though the characters had lost their
    /// progress — but a roster is still a roster without it, so it never fails the screen.
    /// </summary>
    private async Task<int?> ReadMasteryPointsAsync(CancellationToken ct, bool stored = false)
    {
        if (!session.HasPermission(Gw2Permissions.Progression)) return null;

        if (stored)
            return (await offline.StoredAsync("account/mastery/points", Gw2JsonContext.Default.MasteryPoints, ct))
                ?.Totals?.Sum(t => t.Earned);

        var points = await offline.ReadOptionalAsync(
            "account/mastery/points", Gw2JsonContext.Default.MasteryPoints,
            client.GetMasteryPointsAsync, ct);

        return points.Value?.Totals?.Sum(t => t.Earned);
    }

    /// <summary>Hours is how players talk about time on a character; days once it gets silly.</summary>
    private static string FormatPlaytime(long seconds)
    {
        var hours = seconds / 3600d;
        return hours >= 1000
            ? L.F("{0:N0} d", hours / 24)
            : L.F("{0:N0} h", hours);
    }

}
