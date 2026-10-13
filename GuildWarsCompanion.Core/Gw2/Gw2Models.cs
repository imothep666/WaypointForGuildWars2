using System.Text.Json.Serialization;

namespace GuildWarsCompanion.Gw2;

/// <summary>
/// The API's error envelope. Guild Wars 2 answers a bad key with 400 and this body rather
/// than a 401, so the text is the only way to tell "your key is wrong" from "your request
/// was wrong".
/// </summary>
public sealed record ApiError([property: JsonPropertyName("text")] string? Text);

/// <summary>Response of GET /v2/tokeninfo — use it to validate a key and learn its scopes.</summary>
public sealed record TokenInfo(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("permissions")] IReadOnlyList<string> Permissions);

/// <summary>Response of GET /v2/account — requires the "account" scope.</summary>
public sealed record Account(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("world")] int World,
    [property: JsonPropertyName("created")] DateTimeOffset Created,
    [property: JsonPropertyName("access")] IReadOnlyList<string> Access,
    [property: JsonPropertyName("fractal_level")] int FractalLevel,
    [property: JsonPropertyName("wvw_rank")] int WvwRank);

/// <summary>
/// One equipped item. With ?ids=all the array also carries every armory and build-template
/// piece, so <see cref="Location"/> is what separates "worn right now" from "stored".
/// </summary>
public sealed record EquipmentPiece(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("slot")] string? Slot,
    [property: JsonPropertyName("location")] string? Location,
    [property: JsonPropertyName("upgrades")] IReadOnlyList<int>? Upgrades,
    [property: JsonPropertyName("stats")] SelectedStats? Stats,
    [property: JsonPropertyName("dyes")] IReadOnlyList<int?>? Dyes = null,
    [property: JsonPropertyName("skin")] int? Skin = null)
{
    /// <summary>Legendary gear reports as equipped out of the armory rather than directly.</summary>
    public bool IsWorn => Location is "Equipped" or "EquippedFromLegendaryArmory";
}

/// <summary>
/// One equipment template: a whole loadout the character can swap to in the field.
///
/// The character's top-level `equipment` array is every tab's contents run together, which is
/// why it needs filtering to find what is actually worn. A tab, by contrast, is one coherent
/// set — its pieces are the loadout.
/// </summary>
public sealed record EquipmentTab(
    [property: JsonPropertyName("tab")] int Tab,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("is_active")] bool IsActive,
    [property: JsonPropertyName("equipment")] IReadOnlyList<EquipmentPiece>? Equipment,
    [property: JsonPropertyName("equipment_pvp")] PvpLoadout? Pvp);

/// <summary>
/// The PvP half of a template: one amulet, one rune, four sigils.
///
/// PvP gear is a separate id space from everything else — amulets come from /v2/pvp/amulets,
/// not /v2/items — which is why it could not be shown before the tabs were read.
/// </summary>
public sealed record PvpLoadout(
    [property: JsonPropertyName("amulet")] int? Amulet,
    [property: JsonPropertyName("rune")] int? Rune,
    [property: JsonPropertyName("sigils")] IReadOnlyList<int?>? Sigils);

/// <summary>
/// GET /v2/colors — a dye, which is not one colour but several.
///
/// The same dye reads differently on cloth, leather and metal, and the API gives each its own
/// rgb. Which one applies depends on the wearer's armour weight, so the profession decides
/// what a dye actually looks like.
/// </summary>
public sealed record DyeColor(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("base_rgb")] IReadOnlyList<int>? BaseRgb,
    [property: JsonPropertyName("cloth")] DyeMaterial? Cloth,
    [property: JsonPropertyName("leather")] DyeMaterial? Leather,
    [property: JsonPropertyName("metal")] DyeMaterial? Metal,
    /// <summary>
    /// Three strings, always in the same order: hue, material and rarity — ["Blue", "Vibrant",
    /// "Rare"]. Position is the only thing that says which is which; there are no keys. Every
    /// dye in the game carries all three except Dye Remover, which carries none.
    /// </summary>
    [property: JsonPropertyName("categories")] IReadOnlyList<string>? Categories = null);

/// <summary>How a dye comes out on one kind of armour.</summary>
public sealed record DyeMaterial(
    [property: JsonPropertyName("rgb")] IReadOnlyList<int>? Rgb);

/// <summary>
/// GET /v2/home/cats — a home instance cat.
///
/// There is no name here and never has been: the API gives only the hint, which is the food
/// that lures it. The hint is the answer, not a clue to one.
/// </summary>
public sealed record HomeCat(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("hint")] string? Hint);

/// <summary>
/// One side of the gem exchange.
///
/// The endpoint answers a specific trade rather than publishing a rate: ask what 7 gold buys
/// and it prices that trade. <see cref="CoinsPerGem"/> is what that particular trade worked out
/// at, not a rate to multiply by — the exchange is not linear, and a small conversion gets a
/// worse one.
///
/// A trade too small to make is answered with <see cref="Text"/> and no quantity, on a 200
/// rather than an error, so an absent quantity is the thing to check.
/// </summary>
public sealed record GemQuote(
    [property: JsonPropertyName("quantity")] int Quantity,
    [property: JsonPropertyName("coins_per_gem")] long CoinsPerGem,
    [property: JsonPropertyName("text")] string? Text);

/// <summary>GET /v2/pvp/amulets — the one piece of PvP gear items cannot name.</summary>
public sealed record PvpAmulet(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("icon")] string? Icon);

/// <summary>One chosen specialization and the three traits taken in it.</summary>
public sealed record SpecializationChoice(
    [property: JsonPropertyName("id")] int? Id,
    [property: JsonPropertyName("traits")] IReadOnlyList<int?>? Traits);

/// <summary>
/// A skill bar. Every slot is nullable: a character below level 80, or one that simply has
/// not slotted an elite, reports null rather than omitting the field.
/// </summary>
public sealed record SkillBar(
    [property: JsonPropertyName("heal")] int? Heal,
    [property: JsonPropertyName("utilities")] IReadOnlyList<int?>? Utilities,
    [property: JsonPropertyName("elite")] int? Elite,
    [property: JsonPropertyName("legends")] IReadOnlyList<string?>? Legends);

/// <summary>
/// The contents of one build template: what modern characters carry instead of the old
/// top-level specializations and skills members.
/// </summary>
public sealed record BuildTemplate(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("specializations")] IReadOnlyList<SpecializationChoice>? Specializations,
    [property: JsonPropertyName("skills")] SkillBar? Skills,
    [property: JsonPropertyName("legends")] IReadOnlyList<string?>? Legends);

/// <summary>One of the character's build tabs. Requires the "builds" scope.</summary>
public sealed record BuildTab(
    [property: JsonPropertyName("tab")] int Tab,
    [property: JsonPropertyName("is_active")] bool IsActive,
    [property: JsonPropertyName("build")] BuildTemplate? Build);

/// <summary>
/// The stat set chosen on an ascended or legendary piece, which carries no fixed stats of
/// its own. Without this, exactly the best gear reads as having no stats at all.
/// </summary>
public sealed record SelectedStats([property: JsonPropertyName("id")] int Id);

/// <summary>
/// Subset of GET /v2/characters?ids=all — the full object also carries inventory, training
/// and crafting. Unmapped members are ignored.
/// </summary>
/// <summary>GET /v2/characters/:id/core — a character's level and playtime and nothing heavy.</summary>
public sealed record CharacterCore(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("level")] int Level,
    [property: JsonPropertyName("age")] long AgeSeconds);

public sealed record Character(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("race")] string Race,
    [property: JsonPropertyName("gender")] string? Gender,
    [property: JsonPropertyName("profession")] string Profession,
    [property: JsonPropertyName("level")] int Level,
    [property: JsonPropertyName("age")] long AgeSeconds,
    [property: JsonPropertyName("deaths")] int Deaths,
    [property: JsonPropertyName("created")] DateTimeOffset Created,
    [property: JsonPropertyName("equipment")] IReadOnlyList<EquipmentPiece>? Equipment,
    [property: JsonPropertyName("active_build_tab")] int? ActiveBuildTab,
    [property: JsonPropertyName("build_tabs")] IReadOnlyList<BuildTab>? BuildTabs,
    [property: JsonPropertyName("training")] IReadOnlyList<TrainingLine>? Training,
    [property: JsonPropertyName("backstory")] IReadOnlyList<string>? Backstory,
    [property: JsonPropertyName("bags")] IReadOnlyList<CharacterBag?>? Bags = null,
    [property: JsonPropertyName("crafting")] IReadOnlyList<CraftingDiscipline>? Crafting = null,
    [property: JsonPropertyName("active_equipment_tab")] int? ActiveEquipmentTab = null,
    [property: JsonPropertyName("equipment_tabs")] IReadOnlyList<EquipmentTab>? EquipmentTabs = null)
{
    /// <summary>
    /// The build the character is actually wearing. `is_active` is the authority; the tab
    /// number and then the first tab are fallbacks for older responses.
    /// </summary>
    public BuildTemplate? ActiveBuild =>
        BuildTabs?.FirstOrDefault(t => t.IsActive)?.Build
        ?? BuildTabs?.FirstOrDefault(t => t.Tab == ActiveBuildTab)?.Build
        ?? BuildTabs?.FirstOrDefault()?.Build;
}

// --- build reference data (static, like items) ------------------------------

/// <summary>GET /v2/specializations — "elite" is what gives a build its common name.</summary>
public sealed record Specialization(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("elite")] bool Elite,
    [property: JsonPropertyName("icon")] string? Icon,
    // Three, one per tier, and always active: they are granted rather than chosen.
    [property: JsonPropertyName("minor_traits")] IReadOnlyList<int>? MinorTraits = null,
    // Nine, in tier order: three for tier one, then tier two, then tier three.
    [property: JsonPropertyName("major_traits")] IReadOnlyList<int>? MajorTraits = null,
    [property: JsonPropertyName("background")] string? Background = null);

/// <summary>
/// GET /v2/backstory/answers — a character creation choice. `journal` is the one-line
/// version the personal story journal shows; `description` is the longer first-person text.
/// </summary>
public sealed record BackstoryAnswer(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("journal")] string? Journal);

/// <summary>GET /v2/traits.</summary>
public sealed record Trait(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("icon")] string? Icon,
    [property: JsonPropertyName("description")] string? Description = null,
    [property: JsonPropertyName("tier")] int Tier = 0,
    [property: JsonPropertyName("slot")] string? Slot = null);

/// <summary>
/// One weapon skill a profession gets from a weapon type. <c>Attunement</c> appears only for
/// elementalists (four sets per slot) and <c>Offhand</c> only for thief dual-wield skills,
/// where slot 3 depends on what is in the other hand.
/// </summary>
public sealed record ProfessionWeaponSkill(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("slot")] string? Slot,
    [property: JsonPropertyName("offhand")] string? Offhand,
    [property: JsonPropertyName("attunement")] string? Attunement);

/// <summary>What a profession can do with one weapon type. Flags: TwoHand, Mainhand, Offhand.</summary>
public sealed record ProfessionWeapon(
    [property: JsonPropertyName("flags")] IReadOnlyList<string>? Flags,
    [property: JsonPropertyName("skills")] IReadOnlyList<ProfessionWeaponSkill>? Skills);

/// <summary>
/// GET /v2/professions — the weapon-to-skill map. Slots 1-5 are not stored on the character:
/// they follow from the profession and the weapon in hand, so they have to be derived.
/// </summary>
public sealed record Profession(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("icon")] string? Icon,
    [property: JsonPropertyName("weapons")] Dictionary<string, ProfessionWeapon>? Weapons);

/// <summary>One skill line and the hero points put into it.</summary>
public sealed record TrainingLine(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("spent")] int Spent,
    [property: JsonPropertyName("done")] bool Done);

/// <summary>Mastery points earned and spent in one region.</summary>
public sealed record MasteryTotal(
    [property: JsonPropertyName("region")] string Region,
    [property: JsonPropertyName("spent")] int Spent,
    [property: JsonPropertyName("earned")] int Earned);

/// <summary>
/// GET /v2/account/mastery/points — needs "progression". Account-wide: masteries belong to
/// the account, not to a character, however much they behave like a post-80 level.
/// </summary>
public sealed record MasteryPoints(
    [property: JsonPropertyName("totals")] IReadOnlyList<MasteryTotal>? Totals);

/// <summary>
/// GET /v2/masteries — one track, and every level it has.
///
/// Tracks belong to a region, and so do the points that buy them: Central Tyria points cannot
/// train a Maguuma track. A single account-wide total would say nothing about what can actually
/// be spent.
/// </summary>
public sealed record Mastery(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("requirement")] string? Requirement,
    [property: JsonPropertyName("order")] int Order,
    [property: JsonPropertyName("region")] string? Region,
    [property: JsonPropertyName("levels")] IReadOnlyList<MasteryLevel>? Levels);

/// <summary>One step of a track, and what it costs to train.</summary>
public sealed record MasteryLevel(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("point_cost")] int PointCost,
    [property: JsonPropertyName("exp_cost")] long ExpCost);

/// <summary>
/// GET /v2/account/masteries — how far the account is along a track.
///
/// <see cref="Level"/> is a <em>0-indexed</em> reference into the track's levels, so a track
/// showing 4 has five levels done. Reading it as a count is off by one on every trained track.
/// </summary>
public sealed record AccountMastery(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("level")] int Level);

/// <summary>Points at the stat set an item carries, e.g. 161 for Berserker's.</summary>
public sealed record InfixUpgrade([property: JsonPropertyName("id")] int Id);

/// <summary>The weapon type of an equipped item, and the stat set it was made with.</summary>
public sealed record WeaponDetails(
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("infix_upgrade")] InfixUpgrade? InfixUpgrade);

/// <summary>
/// GET /v2/itemstats — the names players use for stat combinations. This is what lets the app
/// say "Berserker's" instead of "Power, Precision, CritDamage".
/// </summary>
public sealed record ItemStat(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name);

/// <summary>
/// An equipped item with the details the cache does not keep: its level, and the sub-type
/// that says whether a weapon is two-handed or an upgrade is a rune or a sigil.
/// </summary>
public sealed record WeaponItem(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("level")] int Level,
    [property: JsonPropertyName("details")] WeaponDetails? Details);

/// <summary>
/// GET /v2/legends — a revenant's utility bar comes from the equipped legend rather than
/// from chosen skills, so the build template leaves those slots empty.
/// </summary>
public sealed record Legend(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("heal")] int? Heal,
    [property: JsonPropertyName("elite")] int? Elite,
    [property: JsonPropertyName("utilities")] IReadOnlyList<int?>? Utilities);

/// <summary>
/// GET /v2/skills. The description is the only place a drawback appears: the structured
/// `facts` carry no polarity and no target, so "gain fury" and "you take more damage" are
/// indistinguishable to a machine. The prose is therefore worth showing verbatim.
/// </summary>
public sealed record Skill(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("icon")] string? Icon,
    [property: JsonPropertyName("description")] string? Description);

/// <summary>GET /v2/build — the current game build. Changes when ArenaNet patches.</summary>
public sealed record GameBuild([property: JsonPropertyName("id")] int Id);

/// <summary>GET /v2/currencies — static, cacheable. `Order` is ANet's own display order.</summary>
public sealed record Currency(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("order")] int Order,
    [property: JsonPropertyName("icon")] string Icon);

/// <summary>GET /v2/account/wallet — needs the "wallet" scope. Coin (id 1) is in copper.</summary>
public sealed record WalletEntry(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("value")] long Value);

/// <summary>GET /v2/materials — the material storage tabs and which item ids live in each.</summary>
public sealed record MaterialCategory(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("order")] int Order,
    [property: JsonPropertyName("items")] IReadOnlyList<int> Items);

/// <summary>GET /v2/account/materials — needs "inventories". Returns bare item ids plus counts.</summary>
public sealed record AccountMaterial(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("category")] int Category,
    [property: JsonPropertyName("count")] int Count);

/// <summary>
/// One occupied slot — in the bank, a shared inventory slot, or a character's bag.
///
/// All three endpoints return the same shape, and all three return <c>null</c> for an empty
/// slot, which is why every list of these is a list of nullables. A slot is one stack, so an
/// item held in several slots appears several times and has to be summed.
/// </summary>
public sealed record StorageSlot(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("count")] int Count,
    [property: JsonPropertyName("binding")] string? Binding);

/// <summary>
/// A bag equipped on a character, with what is inside it. GET /v2/characters returns this
/// under "bags"; empty bag slots are null, as are empty slots within a bag.
/// </summary>
public sealed record CharacterBag(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("size")] int Size,
    [property: JsonPropertyName("inventory")] IReadOnlyList<StorageSlot?>? Inventory);

/// <summary>
/// Subset of GET /v2/items — unauthenticated and immutable until the next game build,
/// which is exactly why it belongs in the local cache rather than in a request per view.
/// </summary>
public sealed record Item(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("icon")] string? Icon,
    [property: JsonPropertyName("rarity")] string? Rarity);

/// <summary>
/// The full GET /v2/items record, for the one item a player has tapped.
///
/// <see cref="Item"/> keeps only what a list row needs, because it is cached 74,000 times
/// over. This is fetched on demand for a single item and not stored.
/// </summary>
public sealed record ItemDetail(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("icon")] string? Icon,
    [property: JsonPropertyName("rarity")] string? Rarity,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("level")] int Level,
    [property: JsonPropertyName("vendor_value")] int VendorValue,
    [property: JsonPropertyName("flags")] IReadOnlyList<string>? Flags,
    [property: JsonPropertyName("chat_link")] string? ChatLink,
    [property: JsonPropertyName("details")] ItemSubtype? Details);

/// <summary>
/// The shared head of the "details" object. Its shape changes per item type — a weapon
/// carries damage, armour carries defence — but every variant names its own subtype, which is
/// the part worth showing.
/// </summary>
public sealed record ItemSubtype([property: JsonPropertyName("type")] string? Type);

/// <summary>
/// GET /v2/account/luck — the total essence of luck this account has consumed.
///
/// Shaped like the wallet: a list with one entry. The value is raw luck, not a magic find
/// percentage; the conversion between them is a 300-row table the API does not carry.
/// </summary>
public sealed record AccountLuck(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("value")] long Value);

// --- achievements ----------------------------------------------------------

/// <summary>
/// GET /v2/account/achievements — one line per achievement the account has touched.
///
/// <see cref="Max"/> is the current tier's target, not the final one: a slayer achievement
/// with tiers at 10/100/500 reports max 100 while you are working through the second.
/// </summary>
public sealed record AccountAchievement(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("current")] int Current,
    [property: JsonPropertyName("max")] int Max,
    [property: JsonPropertyName("done")] bool Done,
    [property: JsonPropertyName("repeated")] int Repeated,
    [property: JsonPropertyName("bits")] IReadOnlyList<int>? Bits = null);

/// <summary>
/// GET /v2/achievements — the static half. There are over eight thousand, so these are only
/// ever fetched for the handful being shown.
/// </summary>
public sealed record Achievement(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("requirement")] string? Requirement,
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("flags")] IReadOnlyList<string>? Flags,
    [property: JsonPropertyName("tiers")] IReadOnlyList<AchievementTier>? Tiers,
    [property: JsonPropertyName("bits")] IReadOnlyList<AchievementBit>? Bits = null);

/// <summary>
/// GET /v2/achievements/categories/88, the day's fractal dailies.
///
/// A daily category names today's achievements and, with the schema this client pins, tomorrow's
/// too. Each entry is an object rather than a bare id since the 2022-03-23 schema.
/// </summary>
public sealed record DailyCategory(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("achievements")] IReadOnlyList<CategoryEntry> Achievements,
    [property: JsonPropertyName("tomorrow")] IReadOnlyList<CategoryEntry>? Tomorrow = null);

public sealed record CategoryEntry([property: JsonPropertyName("id")] int Id);

/// <summary>
/// One entry in a collection.
///
/// The account reports which of these are done as a list of <em>indices into this array</em>,
/// not as ids — so position is identity here, and the two must be read together.
/// </summary>
public sealed record AchievementBit(
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("id")] int? Id,
    [property: JsonPropertyName("text")] string? Text);

/// <summary>GET /v2/skins — collections are mostly skins, which items cannot name.</summary>
public sealed record Skin(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("icon")] string? Icon,
    [property: JsonPropertyName("rarity")] string? Rarity);

/// <summary>
/// One entry in any of the cosmetic catalogues: /v2/outfits, /v2/gliders, /v2/titles,
/// /v2/mailcarriers, /v2/novelties, /v2/finishers, /v2/jadebots, /v2/skiffs, /v2/mounts/skins.
///
/// One record for nine endpoints because they agree on the three fields the unlock screen
/// draws. Each publishes more than this — a glider knows its order, a title its achievement —
/// and none of it is read here. Registering nine near-identical records would cost nine
/// entries in the source-generated context to draw the same row.
///
/// Titles carry no icon, which is why that is nullable rather than an oversight.
/// </summary>
public sealed record UnlockItem(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("icon")] string? Icon);

/// <summary>
/// GET /v2/account/finishers — the one unlock endpoint that answers with objects.
/// </summary>
/// <param name="Quantity">
/// Uses left on a consumable one. Not shown and not counted: owning a finisher with no charges
/// is still owning it, and a list that hid them would disagree with the game's own wardrobe.
/// </param>
public sealed record AccountFinisher(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("permanent")] bool Permanent,
    [property: JsonPropertyName("quantity")] int Quantity);

/// <summary>GET /v2/minis — the other thing collections are made of.</summary>
public sealed record Mini(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("icon")] string? Icon);

/// <summary>What a tier costs and what it pays.</summary>
public sealed record AchievementTier(
    [property: JsonPropertyName("count")] int Count,
    [property: JsonPropertyName("points")] int Points);

// --- crafting --------------------------------------------------------------

/// <summary>
/// One discipline a character has taken up. <see cref="Active"/> marks the two that are
/// currently switched on — a character may hold more but only use two at a time.
/// </summary>
public sealed record CraftingDiscipline(
    [property: JsonPropertyName("discipline")] string Name,
    [property: JsonPropertyName("rating")] int Rating,
    [property: JsonPropertyName("active")] bool Active);


/// <summary>
/// GET /v2/recipes — what makes an item, and out of what.
///
/// The API has no "can I craft this" answer; it has ingredients and counts, and the rest is
/// arithmetic against what the account is holding.
/// </summary>
public sealed record Recipe(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("output_item_id")] int OutputItemId,
    [property: JsonPropertyName("output_item_count")] int OutputItemCount,
    [property: JsonPropertyName("disciplines")] IReadOnlyList<string>? Disciplines,
    [property: JsonPropertyName("min_rating")] int MinRating,
    [property: JsonPropertyName("flags")] IReadOnlyList<string>? Flags,
    [property: JsonPropertyName("ingredients")] IReadOnlyList<RecipeIngredient>? Ingredients);

/// <summary>
/// One line of a recipe. <see cref="Type"/> is usually "Item", but currencies and guild
/// upgrades appear too — and those cannot be counted against storage.
/// </summary>
public sealed record RecipeIngredient(
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("count")] int Count)
{
    public bool IsItem => Type is null or "Item";
}

// --- personal story --------------------------------------------------------

/// <summary>
/// One step of a story. <see cref="Goals"/> carries the interesting part: each goal has a
/// first-person account of what happened, written as the character would tell it.
/// </summary>
public sealed record Quest(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("level")] int Level,
    [property: JsonPropertyName("story")] int Story,
    [property: JsonPropertyName("goals")] IReadOnlyList<QuestGoal>? Goals);

/// <summary>
/// <see cref="Complete"/> is the past-tense account — "I defeated the Inquest agents" — and is
/// the only place the API records what a character actually did rather than what they could.
/// </summary>
public sealed record QuestGoal(
    [property: JsonPropertyName("active")] string? Active,
    [property: JsonPropertyName("complete")] string? Complete);

/// <summary>
/// GET /v2/stories — an arc within a season. Note that a story's quests cover every branch:
/// the personal story lists 44 quests where any one character completes a fraction, because
/// the branches are the decisions.
/// </summary>
public sealed record Story(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("season")] string? Season,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("timeline")] string? Timeline,
    [property: JsonPropertyName("order")] int Order,
    [property: JsonPropertyName("races")] IReadOnlyList<string>? Races);

/// <summary>GET /v2/stories/seasons — "My Story", "Heart of Thorns", and the rest, in order.</summary>
public sealed record StorySeason(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("order")] int Order);

// --- raids and dungeons ----------------------------------------------------
// These two do NOT follow the pattern the other reset trackers share. /v2/worldbosses and
// /v2/account/worldbosses talk about the same ids; /v2/raids lists six raids while
// /v2/account/raids reports individual encounters, and /v2/dungeons lists eight dungeons
// while /v2/account/dungeons reports individual paths. The static side has to be expanded
// before the two can be compared at all.

/// <summary>GET /v2/raids?ids=all — one request expands every wing and encounter.</summary>
public sealed record Raid(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("wings")] IReadOnlyList<RaidWing>? Wings);

public sealed record RaidWing(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("events")] IReadOnlyList<RaidEvent>? Events);

/// <summary>
/// One thing inside a wing. Four of the thirty are checkpoints rather than fights — Spirit
/// Woods, Twisted Castle, Gate, Camp — and counting those as encounters would inflate every
/// denominator on the screen.
/// </summary>
public sealed record RaidEvent(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("type")] string? Type)
{
    public bool IsBoss => Type == "Boss";
}

/// <summary>GET /v2/dungeons?ids=all — eight dungeons, thirty-three paths between them.</summary>
public sealed record Dungeon(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("paths")] IReadOnlyList<DungeonPath>? Paths);

/// <summary>A single run. "Story" or "Explorable"; both reset daily and both count.</summary>
public sealed record DungeonPath(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("type")] string? Type);

// --- Wizard's Vault --------------------------------------------------------

/// <summary>GET /v2/wizardsvault — the running season. Unauthenticated.</summary>
public sealed record VaultSeason(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("start")] DateTimeOffset Start,
    [property: JsonPropertyName("end")] DateTimeOffset End);

/// <summary>
/// GET /v2/account/wizardsvault/listings — the vault's shop as this account sees it. Needs
/// "account". <c>purchased</c> and <c>purchase_limit</c> are absent for an unlimited reward; the
/// wiki records that <c>purchased</c> has been reported as 0 even after buying, so it is read as a
/// claim to be checked rather than trusted.
/// </summary>
public sealed record VaultListing(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("item_id")] int ItemId,
    [property: JsonPropertyName("item_count")] int ItemCount,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("cost")] int Cost,
    [property: JsonPropertyName("purchased")] int? Purchased = null,
    [property: JsonPropertyName("purchase_limit")] int? PurchaseLimit = null);

/// <summary>One objective on a Wizard's Vault track.</summary>
public sealed record VaultObjective(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("track")] string Track,
    [property: JsonPropertyName("acclaim")] int Acclaim,
    [property: JsonPropertyName("progress_current")] int ProgressCurrent,
    [property: JsonPropertyName("progress_complete")] int ProgressComplete,
    [property: JsonPropertyName("claimed")] bool Claimed)
{
    /// <summary>Done, but the reward is still sitting in the vault waiting to be taken.</summary>
    public bool IsUnclaimedCompletion => ProgressCurrent >= ProgressComplete && !Claimed;
}

/// <summary>
/// GET /v2/account/wizardsvault/{daily|weekly|special} — needs "account" and "progression".
/// All three tracks share this shape.
/// </summary>
public sealed record VaultTrack(
    [property: JsonPropertyName("meta_progress_current")] int MetaProgressCurrent,
    [property: JsonPropertyName("meta_progress_complete")] int MetaProgressComplete,
    [property: JsonPropertyName("meta_reward_astral")] int MetaRewardAstral,
    [property: JsonPropertyName("meta_reward_claimed")] bool MetaRewardClaimed,
    IReadOnlyList<VaultObjective> Objectives)
{
    /// <summary>
    /// The track's objectives, less any that count to nothing. The Special track has carried one,
    /// "Tenebral Ward", reported as 0 of 0 and unclaimed: by the numbers it is done and waiting,
    /// in the game there is nothing to claim. Dropped here, every reader agrees — the vault
    /// screen, its dot in the bar, Today, the widget and the alerts.
    /// </summary>
    [JsonPropertyName("objectives")]
    public IReadOnlyList<VaultObjective> Objectives { get; init; } =
        Objectives.Where(objective => objective.ProgressComplete > 0).ToList();

    /// <summary>Same reasoning as <see cref="Objectives"/>: a meta that counts to 0 has no reward.</summary>
    public bool IsMetaUnclaimed =>
        MetaProgressComplete > 0 && MetaProgressCurrent >= MetaProgressComplete && !MetaRewardClaimed;
}

// --- Trading post ----------------------------------------------------------

/// <summary>
/// GET /v2/commerce/transactions/{current|history}/{buys|sells} — needs "tradingpost".
/// `Purchased` is only set on history entries: it is the moment the trade actually closed.
/// </summary>
public sealed record Transaction(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("item_id")] int ItemId,
    [property: JsonPropertyName("price")] long Price,
    [property: JsonPropertyName("quantity")] int Quantity,
    [property: JsonPropertyName("created")] DateTimeOffset Created,
    [property: JsonPropertyName("purchased")] DateTimeOffset? Purchased);

/// <summary>One side of an item's market. Quantity is how deep that side of the book is.</summary>
public sealed record PriceSide(
    [property: JsonPropertyName("quantity")] int Quantity,
    [property: JsonPropertyName("unit_price")] long UnitPrice);

/// <summary>
/// GET /v2/commerce/prices — unauthenticated. "buys" is what people offer to pay, "sells"
/// is what people are asking; the gap between them is the spread you trade against.
/// </summary>
public sealed record ItemPrice(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("buys")] PriceSide? Buys,
    [property: JsonPropertyName("sells")] PriceSide? Sells);

/// <summary>
/// One price step of an item's order book: how many units are offered at that price.
///
/// GET /v2/commerce/listings returns every step, cheapest ask first and highest offer first,
/// which is what makes "how many are ahead of me" answerable at all.
/// </summary>
public sealed record ListingTier(
    [property: JsonPropertyName("listings")] int Listings,
    [property: JsonPropertyName("unit_price")] long UnitPrice,
    [property: JsonPropertyName("quantity")] int Quantity);

/// <summary>An item's whole order book. Far heavier than a price — some run to thousands of
/// steps — so it is fetched only where the depth answers something.</summary>
public sealed record ItemListings(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("buys")] IReadOnlyList<ListingTier>? Buys,
    [property: JsonPropertyName("sells")] IReadOnlyList<ListingTier>? Sells);

/// <summary>An item sitting in the pickup box.</summary>
public sealed record DeliveryItem(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("count")] int Count);

/// <summary>
/// GET /v2/commerce/delivery — needs "tradingpost". What the trading post is holding for
/// you: coins from completed sales and items from completed purchases.
/// </summary>
public sealed record DeliveryBox(
    [property: JsonPropertyName("coins")] long Coins,
    [property: JsonPropertyName("items")] IReadOnlyList<DeliveryItem>? Items);

/// <summary>
/// One continent, as /v2/continents/{id} describes it.
///
/// Fetched for exactly two numbers, and both are needed before a single map tile can be asked
/// for. <see cref="MaxZoom"/> is the level at which continent coordinates are pixels, so every
/// other level is derived from it; <see cref="ContinentDims"/> bounds the tile grid so the map
/// screen cannot ask for a tile that was never rendered.
///
/// Static data — Tyria answers 81920 x 114688 and zoom 8, and has for years — so one request per
/// continent per run is plenty.
/// </summary>
public sealed record ContinentInfo(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("continent_dims")] List<double>? ContinentDims,
    [property: JsonPropertyName("min_zoom")] int MinZoom,
    [property: JsonPropertyName("max_zoom")] int MaxZoom)
{
    /// <summary>The continent's size in continent units, or null when the API omits it.</summary>
    [JsonIgnore]
    public (double Width, double Height)? Dims =>
        ContinentDims is [var width, var height, ..] ? (width, height) : null;
}

/// <summary>
/// One map, as /v2/maps/{id} describes it.
///
/// <see cref="ContinentRect"/> is the map's footprint in continent coordinates — the same space
/// MumbleLink's playerX/playerY use and the same space every landmark below is published in.
/// That shared space is what makes a bearing computable at all: without it the character's
/// position and the objective's position would be two different kinds of number.
/// </summary>
/// <param name="RegionId">Needed to build the floor URL that carries the landmarks; /v2/maps
/// is the only place it is published.</param>
public sealed record MapInfo(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("region_id")] int RegionId,
    [property: JsonPropertyName("continent_id")] int ContinentId,
    [property: JsonPropertyName("default_floor")] int DefaultFloor,
    [property: JsonPropertyName("continent_rect")] List<List<double>>? ContinentRect,
    [property: JsonPropertyName("map_rect")] List<List<double>>? MapRect = null,
    [property: JsonPropertyName("type")] string? Type = null,
    [property: JsonPropertyName("region_name")] string? RegionName = null,
    [property: JsonPropertyName("continent_name")] string? ContinentName = null,
    [property: JsonPropertyName("floors")] List<int>? Floors = null)
{
    /// <summary>
    /// The middle of the map, in continent coordinates, or null when the rect is missing.
    ///
    /// Used for a target the API can only place to the nearest map — a world boss, whose arena
    /// has no published coordinate. Pointing at the middle of Queensdale is a true statement
    /// about where Queensdale is; inventing the Shadow Behemoth's exact spawn would not be.
    /// </summary>
    [JsonIgnore]
    public (double X, double Y)? Centre =>
        ContinentRect is [[var left, var top], [var right, var bottom]]
            ? ((left + right) / 2, (top + bottom) / 2)
            : null;

    /// <summary>The footprint in continent coordinates, where y grows south.</summary>
    [JsonIgnore]
    public (double Left, double Top, double Right, double Bottom)? ContinentArea =>
        ContinentRect is [[var left, var top], [var right, var bottom]]
            ? (left, top, right, bottom)
            : null;

    /// <summary>
    /// The footprint in the map's own coordinates, where y grows north.
    ///
    /// The other half of what makes a position inside a map convertible into a position in the
    /// world; see <see cref="Services.MapSpace"/>. Published by /v2/maps and by nothing else.
    /// </summary>
    [JsonIgnore]
    public (double Left, double Bottom, double Right, double Top)? MapArea =>
        MapRect is [[var left, var bottom], [var right, var top]]
            ? (left, bottom, right, top)
            : null;
}

/// <summary>A renown heart, as the floor endpoint publishes it.</summary>
public sealed record MapTask(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("objective")] string Objective,
    [property: JsonPropertyName("level")] int Level,
    [property: JsonPropertyName("coord")] List<double>? Coord);

/// <summary>
/// A point of interest: a waypoint, a landmark, a vista or a dungeon entrance.
///
/// <see cref="Type"/> tells them apart — "waypoint", "landmark", "vista", "unlock" — and matters
/// because a waypoint is somewhere you travel to and a vista is somewhere you climb.
/// </summary>
public sealed record MapPoi(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("coord")] List<double>? Coord);

/// <summary>
/// A hero point, as the floor endpoint calls it: a skill challenge.
///
/// The id is a string like "0-7" and it is the same id
/// <c>/v2/characters/{name}/heropoints</c> returns, which is what makes "hide the ones this
/// character has already done" possible at all. No other map feature has that.
/// </summary>
public sealed record MapSkillChallenge(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("coord")] List<double>? Coord);

/// <summary>
/// Everything placed on one map, from
/// /v2/continents/{c}/floors/{f}/regions/{r}/maps/{id}.
///
/// Both collections arrive keyed by id rather than as arrays, which is why they are dictionaries
/// — the key is the id repeated and nothing reads it.
/// </summary>
public sealed record MapDetail(
    [property: JsonPropertyName("tasks")] Dictionary<string, MapTask>? Tasks,
    [property: JsonPropertyName("points_of_interest")] Dictionary<string, MapPoi>? PointsOfInterest,
    [property: JsonPropertyName("skill_challenges")] List<MapSkillChallenge>? SkillChallenges);

/// <summary>
/// One of the game's own asset files, from /v2/files.
///
/// This is where the map marker art lives — <c>map_heart_empty</c>, <c>map_waypoint</c>,
/// <c>map_poi</c>, <c>map_vista</c> — so the compass can show the symbols a player already knows
/// from the in-game map rather than shapes invented here.
/// </summary>
public sealed record Gw2File(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("icon")] string Icon);
