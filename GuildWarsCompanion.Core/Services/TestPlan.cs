namespace GuildWarsCompanion.Services;

/// <summary>One thing to try on the phone or in the game, and what it should show.</summary>
/// <param name="Id">Kept in settings with its tick, so never reused for a different check.</param>
/// <param name="Auto">Ticked by <see cref="TestEvidence"/> when the app sees it happen.</param>
public sealed record TestCheck(string Id, string Area, string Do, string Expect, bool Auto = false);

/// <summary>
/// What has been built but not yet tried for real, as a list the player ticks off in the app.
///
/// Most of it was seen working against the bridge and MumbleSim, which is not the game: the
/// simulator has no real playtime, no trading post sales and no evening inside the notification
/// hours. These are the checks only a real session settles. New work adds its checks here with new
/// ids; a check that is no longer worth trying is deleted, and its tick goes with it.
/// </summary>
public static class TestPlan
{
    public const string Built = "built 5–15 October";

    public static readonly IReadOnlyList<TestCheck> Checks =
    [
        new("today-here", "Today",
            "Stand in a map with a meta or a world boss and pull Today down.",
            "A \"Here:\" line names the map chest, a boss due within two hours and the map's meta. Compare it with the game."),

        new("goal-pin", "Goals and prices",
            "On an item's page, tap PIN AS GOAL, then open Today.",
            "A Goals section with the item, what you have of it, and what is short.", Auto: true),
        new("goal-quantity", "Goals and prices",
            "On the pinned item's page, tap + twice.",
            "× 3, and Today counts three times as much. Every tap on + lands."),
        new("goal-ready", "Goals and prices",
            "Pin something whose materials you already hold, inside your notification hours.",
            "One notification: \"Everything for … is in hand\".", Auto: true),
        new("watch-from-item", "Goals and prices",
            "On a tradable item's page, tap WATCH PRICE.",
            "The watch opens with that item and what sellers ask now. Only the price is left to type.", Auto: true),
        new("watch-baseline", "Goals and prices",
            "With the game closed, search for an item under Price Watch and pick it.",
            "Each result shows what sellers ask. The chosen item says what sellers ask and buyers offer.", Auto: true),
        new("watch-to-tp", "Goals and prices",
            "In the game, tap SEND TO TP on a watch, open the trading post and paste into its search.",
            "The trading post shows exactly the watched item. The tap asked nothing about deleting the watch."),
        new("watch-notify-open", "Goals and prices",
            "When a watched price arrives, tap the notification.",
            "The app opens on Price Watch, not on Today. The notification shows the app's gold star, not a warning triangle."),
        new("watch-notify-send", "Goals and prices",
            "With the bridge running, tap SEND TO TP on a price notification without opening the app.",
            "The notification now says the link went to your PC, and pasting into the trading post's search finds the item."),
        new("order-to-tp", "Goals and prices",
            "On Orders, tap SEND TO TP on an order the market has moved past.",
            "Only undercut or outbid orders have the button. The line under the tabs says where the link went."),

        new("meta-remind", "Reminders",
            "Timetable: tap a map, switch on REMIND for one event, and wait for its next start.",
            "One notification, up to 35 minutes before it starts.", Auto: true),
        new("reset-reminder", "Reminders",
            "Leave a Wizard's Vault daily open on a Friday or Saturday night.",
            "Between 01:00 and 02:00: \"Daily reset in … min\" with what is left, once.", Auto: true),

        new("meta-list", "Timetable",
            "Open the timetable and scroll below the world bosses.",
            "Map metas, what is on now first, at local start times that agree with the wiki's timer."),
        new("meta-chosen", "Timetable",
            "Close a map row after switching on REMIND for one of its events.",
            "The chosen event shows under the closed row."),

        new("port-trail", "Compass and trail",
            "Follow a trail to an objective far off, then open the compass.",
            "Port advice reads \"via … Waypoint · then … to the trail, … along it\", not the waypoint nearest the objective."),
        new("compass-remeasure", "Compass and trail",
            "Port within the same map, then open the compass's target list.",
            "Distances from where you are now, nearest first."),

        new("tekkit-chests", "Marker packs",
            "In a map like Dragon's End or Silverwastes, open the compass list and the map.",
            "Tekkit's chests and jumping puzzles show up beside the other packs', without doubles."),

        new("objective-card", "Compass and trail",
            "With Blish HUD started by the bridge, choose an objective on the compass and walk towards it.",
            "A card in the game names it and counts the metres down, with an arrow and the metres up or down; it says Arrived when you get there. Drag it somewhere else and it stays there."),
        new("alert-in-game", "Compass and trail",
            "While playing, with Blish HUD started by the bridge, let a price watch or a world boss reminder come due.",
            "It appears in the game at the top of the screen, and not on the phone.", Auto: true),

        new("objective-boss", "Compass and trail",
            "Choose a world boss as the objective.",
            "Nothing is drawn in the game: its point is only the middle of its map."),

        new("fractals-row", "Fractals",
            "With a fractal level, open Today.",
            "A Daily fractals row: today's three fractals at your tier and the recommended scales you can enter.",
            Auto: true),
        new("fractals-done", "Fractals",
            "Run one of today's fractals at your tier, then pull Today down.",
            "The row counts one more done, and that fractal leaves its line."),
        new("fractals-page", "Fractals",
            "Tap the Daily fractals row.",
            "Each fractal with the tiers done, the scales, tomorrow's fractals, and your relics and essence as the wallet shows them."),

        new("festival-today", "Festivals",
            "During Halloween, open Today.",
            "A Halloween section with the next Mad King Says and what is left of Halloween Rituals and Shadow of the Mad King.",
            Auto: true),
        new("festival-timetable", "Festivals",
            "During Halloween, open the timetable and tap the Halloween row.",
            "Mad King Says with its countdown; REMIND sends a reminder before it like any meta."),
        new("festival-ends", "Festivals",
            "After 3 November, open Today and the timetable.",
            "No Halloween section and no Halloween row."),

        new("session-page", "Session recap",
            "Tap the session recap on Today.",
            "A page with the session's playtime, characters and every currency it changed.", Auto: true),
        new("session-loot", "Session recap",
            "Open Today before playing, play for half an hour or more, then open the recap after logging out.",
            "What the session earned in coin and items, an hour's worth, and the items gained and spent. Compare a few with the game.",
            Auto: true),
        new("session-progress", "Session recap",
            "Finish a vault objective or an achievement in a recorded session, then open its recap.",
            "It is listed under PROGRESS, with mastery points and crafting levels if any were gained.", Auto: true),
        new("session-earlier", "Session recap",
            "After a second session, open the recap and tap an earlier one in the list.",
            "The earlier session fills the page; the list shows each day with its playtime.", Auto: true),
        new("session-notification", "Session recap",
            "Play a recorded session inside your notification hours and wait about half an hour after logging out.",
            "The recap notification leads with what the session came to in all and an hour, then the wallet."),

        new("here-art", "Widgets",
            "Play a map you have never opened in the app, with the Here widget on the home screen. Wait a quarter hour, or open Today.",
            "The widget shows that map's loading screen behind its words, darkened where they are."),
        new("guide-paste", "Characters",
            "Copy a build code in the game, then on a character's Guide tab tap PASTE.",
            "The code lands in the box and is compared at once: specializations, skills and weapons, with a note that a code holds no gear."),
        new("guide-other-profession", "Characters",
            "On another profession's Guide tab, compare the thief's MetaBattle link.",
            "\"This is a Thief build, and … is a …\" instead of a comparison."),
        new("here-calling", "Widgets",
            "Play a character with an elite specialization, with the Here widget on the home screen. Open Today.",
            "The widget says \"name · Chronomancer\" (the elite, in the phone's language) with its white emblem before it; after the game closes, the emblem stays."),
        new("here-chest", "Widgets",
            "Stand in a map with a hero's choice chest (Auric Basin, Crystal Oasis), open Today, then loot the chest and open Today again.",
            "First \"Map chest waiting\" in brass, then \"Map chest looted\" in grey - and still so hours after the game closes."),
        new("here-boss", "Widgets",
            "Stand in Queensdale or another world boss map, then kill its boss.",
            "Before: \"Shadow Behemoth at 13:45\". After: \"Shadow Behemoth · done today\" in grey."),

        new("voe-access", "Account",
            "Visit a Visions of Eternity map, then pull Account › Standing down.",
            "Note whether Visions of Eternity is listed now.", Auto: true),

        new("lang-names", "Language",
            "With the phone in German, open storage, the map picker and the Wizard's Vault.",
            "Items, maps and objectives are named in German, and so are the app's own words."),
        new("lang-search", "Language",
            "Under Price Watch, search for an English name such as \"ecto\".",
            "The item comes up under its German name, Ektoplasmakugel."),
        new("lang-catalogue", "Language",
            "Open Settings where the item catalogue is, then download it.",
            "Before: how many items are still named in English. After: none, and searching in English still works."),
        new("lang-art", "Language",
            "Open the map, a character with an elite specialization, and the vault's featured rewards.",
            "The loading screen, the specialization painting and the banners all still show."),
        new("lang-map-line", "Language",
            "Play with the compass and Today open.",
            "The map is named in German above the compass and in Today's \"in …\" line."),
        new("lang-mastery", "Language",
            "Open the mastery plan with points to spend.",
            "Tracks your points cover are still marked as affordable.")
    ];
}
