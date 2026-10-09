namespace GuildWarsCompanion.Gw2;

/// <summary>
/// The scopes a key can carry (from /v2/tokeninfo). Check these before navigating to a
/// feature, so the user gets "your key is missing 'wallet'" instead of a 403.
/// </summary>
public static class Gw2Permissions
{
    public const string Account = "account";           // /v2/account, world, access level
    public const string Builds = "builds";             // character build/equipment tabs
    public const string Characters = "characters";     // /v2/characters
    public const string Guilds = "guilds";             // guild member/log/stash endpoints
    public const string Inventories = "inventories";   // bank, materials, character inventories
    public const string Progression = "progression";   // achievements, masteries, luck
    public const string Pvp = "pvp";                   // pvp stats, games, standings
    public const string TradingPost = "tradingpost";   // commerce/transactions, delivery
    public const string Unlocks = "unlocks";           // skins, dyes, minis, outfits, mounts
    public const string Wallet = "wallet";             // /v2/account/wallet
    public const string Wvw = "wvw";                   // /v2/account/wvw
}
