using GuildWarsCompanion.Ava.Views;
using GuildWarsCompanion.ViewModels;

namespace GuildWarsCompanion.Ava.Shell;

/// <summary>One page the router can open, by the name its route uses.</summary>
public sealed record PageRoute(string Name, string Title, Func<PageView> View, Type? ViewModel = null);

/// <summary>
/// One entry in the menu: an area of the account, and where it opens.
///
/// <paramref name="Glyph"/> is a line drawing on a 24-unit square, stroked rather than filled.
/// <paramref name="Tint"/> is the area's own colour, taken from the game: what the bottom bar
/// lights it in. <paramref name="Label"/> is the shorter name the bar has room for.
/// <paramref name="InBar"/> keeps it in the bottom bar always. Of the rest, the one last opened
/// takes the bar's fourth place (see <see cref="Routes.DefaultFourth"/>) and the others are under
/// More. <paramref name="Utility"/> keeps an area out of that place: opening Settings is not a
/// reason to give it a spot under the thumb.
/// <paramref name="Wash"/> names the painted wash its mastheads stand on, Assets/Images/wash_*.png.
/// </summary>
public sealed record Area(
    string Title, string Route, IReadOnlySet<string> Pages, string Glyph, string Tint,
    bool InBar = false, string? Label = null, string? Wash = null, bool Utility = false)
{
    public string BarLabel => Label ?? Title;
}

/// <summary>
/// The app's address book, in the same words AppShell.xaml uses, so a route a view model in Core
/// asks for means the same page here as it does under MAUI Shell.
///
/// Leaf names are unique across the app — <c>wallet</c>, <c>item</c>, <c>map</c> — which is what
/// lets <c>//account/wallet</c> and <c>//map</c> both be found by their last part.
/// </summary>
public static class Routes
{
    /// <summary>
    /// The areas, in the order the side rail and the More sheet list them. Three are always in the
    /// bar - the day's list, the vault, who is playing - and the fourth place is the player's: the
    /// compass at first, and then whatever they last opened from More.
    /// </summary>
    public static readonly IReadOnlyList<Area> Areas =
    [
        new("Today", "//today", Set("today"), Glyphs.Today, "#D9894A", InBar: true, Wash: "today"),
        new("Wizard's Vault", "//vault", Set("vault"), Glyphs.Vault, "#46B7AE", InBar: true, Label: "Vault", Wash: "vault"),
        new("Characters", "//characters", Set("characters"), Glyphs.Characters, "#C08F95", InBar: true, Wash: "characters"),
        new("Compass", "//compass", Set("compass"), Glyphs.Compass, "#5E9FD0", Wash: "compass"),
        new("Account", "//account/wallet", Set("wallet", "materials", "find", "standing", "masteries"),
            Glyphs.Account, "#D9A441", Wash: "account"),
        new("Trading Post", "//tradingpost/orders", Set("orders", "delivery", "watch", "history"),
            Glyphs.TradingPost, "#C8A34E", Wash: "tradingpost"),
        new("Map", "//map", Set("map"), Glyphs.Map, "#5E9FD0", Wash: "compass"),
        new("Timetable", "//events", Set("events"), Glyphs.Timetable, "#C0453B"),
        new("Crafting", "//crafting", Set("crafting"), Glyphs.Crafting, "#B4703C"),
        new("Achievements", "//achievements", Set("achievements"), Glyphs.Achievements, "#C8A34E"),
        new("Unlocks", "//unlocks", Set("unlocks"), Glyphs.Unlocks, "#9C7BC8"),
        new("Emotes", "//emotes", Set("emotes"), Glyphs.Emotes, "#C8A34E"),
#if DEBUG
        // The checklist of what is built but untried; the sideloaded Release APK has no use for it.
        new("Test plan", "//testplan", Set("testplan"), Glyphs.TestPlan, "#7E8B9B", Utility: true),
#endif
        new("Settings", "//settings", Set("settings"), Glyphs.Settings, "#7E8B9B", Utility: true)
    ];

    /// <summary>What the bar's fourth place holds until something else has been opened.</summary>
    public const string DefaultFourth = "//compass";

    private static readonly Dictionary<string, PageRoute> Pages = new PageRoute[]
    {
        new("today", "TODAY", () => new TodayView(), typeof(TodayViewModel)),
        new("events", "TIMETABLE", () => new EventsView(), typeof(EventsViewModel)),
        new("characters", "CHARACTERS", () => new CharactersView(), typeof(CharactersViewModel)),
        new("achievements", "ACHIEVEMENTS", () => new AchievementsView(), typeof(AchievementsViewModel)),
        new("collection", "COLLECTION", () => new CollectionView(), typeof(CollectionViewModel)),
        new("item", "ITEM", () => new ItemView(), typeof(ItemViewModel)),
        new("settings", "SETTINGS", () => new SettingsView(), typeof(SettingsViewModel)),
        new("wallet", "WALLET", () => new WalletView(), typeof(WalletViewModel)),
        new("materials", "STORAGE", () => new MaterialsView(), typeof(MaterialsViewModel)),
        new("find", "STORAGE", () => new FindView(), typeof(FindViewModel)),
        new("standing", "ACCOUNT", () => new StandingView(), typeof(StandingViewModel)),
        new("masteries", "MASTERIES", () => new MasteriesView(), typeof(MasteriesViewModel)),
        new("orders", "TRADING POST", () => new OrdersView(), typeof(OrdersViewModel)),
        new("delivery", "TRADING POST", () => new DeliveryView(), typeof(DeliveryViewModel)),
        new("watch", "TRADING POST", () => new WatchView(), typeof(WatchViewModel)),
        new("history", "TRADING POST", () => new HistoryView(), typeof(HistoryViewModel)),
        new("vault", "VAULT", () => new VaultView(), typeof(VaultViewModel)),
        new("crafting", "CRAFTING", () => new CraftingView(), typeof(CraftingViewModel)),
        new("character", "CHARACTER", () => new CharacterView(), typeof(CharacterViewModel)),
        new("unlocks", "UNLOCKS", () => new UnlocksView(), typeof(UnlocksViewModel)),
        new("unlockcategory", "UNLOCKS", () => new UnlockCategoryView(), typeof(UnlockCategoryViewModel)),
        new("emotes", "EMOTES", () => new EmotesView(), typeof(EmotesViewModel)),
        new("compass", "COMPASS", () => new CompassView(), typeof(CompassViewModel)),
        new("map", "MAP", () => new MapView(), typeof(MapViewModel)),
        new("mappicker", "CHOOSE A MAP", () => new MapPickerView(), typeof(MapPickerViewModel)),
        new("scan", "SCAN KEY", () => new ScanKeyView()),
        new("scanbridge", "SCAN A PC", () => new ScanBridgeView()),
        new("markerpacks", "MARKER PACKS", () => new MarkerPacksView(), typeof(MarkerPacksViewModel)),
#if DEBUG
        new("testplan", "TEST PLAN", () => new TestPlanView(), typeof(TestPlanViewModel)),
#endif
    }.ToDictionary(route => route.Name);

    /// <summary>The page for a name, or a plain one saying it has not been ported yet.</summary>
    public static PageRoute For(string name) =>
        Pages.TryGetValue(name, out var route)
            ? route
            : new PageRoute(name, name.ToUpperInvariant(), () => new NotPortedView(name));

    /// <summary>What an area opens on when it is named on its own: <c>//account</c>.</summary>
    public static string FirstPageOf(string area) => area switch
    {
        "account" => "wallet",
        "tradingpost" => "orders",
        _ => area
    };

    public static Area? AreaOf(string page) => Areas.FirstOrDefault(area => area.Pages.Contains(page));

    private static IReadOnlySet<string> Set(params string[] pages) => new HashSet<string>(pages);
}

/// <summary>The areas' line drawings, each on a 24-unit square.</summary>
public static class Glyphs
{
    public const string Today = "M3,17 H21 M7,17 A5,5 0 0 1 17,17 M12,6 V9 M5.6,9.6 L7.6,11.6 M18.4,9.6 L16.4,11.6";
    public const string Vault = "M12,3 L14,10 L21,12 L14,14 L12,21 L10,14 L3,12 L10,10 Z";
    public const string Characters = "M15.6,8 A3.6,3.6 0 1 1 8.4,8 A3.6,3.6 0 1 1 15.6,8 Z M5,20 C5,16 8.1,13.4 12,13.4 C15.9,13.4 19,16 19,20";
    public const string Compass = "M21,12 A9,9 0 1 1 3,12 A9,9 0 1 1 21,12 Z M12,6.5 L14.4,12 L12,17.5 L9.6,12 Z";
    public const string More = "M5,12 H6 M11.5,12 H12.5 M18,12 H19";
    public const string Account = "M3.5,7 H20.5 V19 H3.5 Z M3.5,10.5 H20.5 M15.5,14.5 H17.5";
    public const string TradingPost = "M12,4 V20 M7,20 H17 M5,8 H19 M5,8 L2.5,14 A2.5,2.5 0 0 0 7.5,14 Z M19,8 L16.5,14 A2.5,2.5 0 0 0 21.5,14 Z";
    public const string Map = "M3,6 L9,4 L15,6 L21,4 V18 L15,20 L9,18 L3,20 Z M9,4 V18 M15,6 V20";
    public const string Timetable = "M20.5,12 A8.5,8.5 0 1 1 3.5,12 A8.5,8.5 0 1 1 20.5,12 Z M12,7.5 V12 L15,14";
    public const string Crafting = "M4,14 H16 L14,17 H6 Z M8,17 V20 M12,17 V20 M6,20 H14 M14,4 L20,10 M17,7 L12,12";
    public const string Achievements = "M8,4 H16 V9 A4,4 0 0 1 8,9 Z M12,13 V17 M8.5,20 H15.5 M8,6 H5 A3,3 0 0 0 8,10 M16,6 H19 A3,3 0 0 1 16,10";
    public const string Unlocks = "M11.5,12 A3.5,3.5 0 1 1 4.5,12 A3.5,3.5 0 1 1 11.5,12 Z M11.5,12 H21 M17,12 V15 M20,12 V14";
    public const string Emotes = "M4,5 H20 V15 H10 L6,19 V15 H4 Z M9.5,12 A3.5,2 0 0 0 14.5,12";
    public const string Settings = "M15,12 A3,3 0 1 1 9,12 A3,3 0 1 1 15,12 Z M12,3 V6 M12,18 V21 M3,12 H6 M18,12 H21 M5.6,5.6 L7.7,7.7 M16.3,16.3 L18.4,18.4 M5.6,18.4 L7.7,16.3 M16.3,7.7 L18.4,5.6";
    public const string TestPlan = "M4,4 H20 V20 H4 Z M8,12 L11,15 L16,9";
}
