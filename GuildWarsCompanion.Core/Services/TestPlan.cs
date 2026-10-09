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
    public const string Built = "built 5–7 October";

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
        new("trail-arrival", "Compass and trail",
            "Walk a trail to its objective.",
            "The phone clears the objective within about five seconds of arriving.", Auto: true),

        new("tekkit-chests", "Marker packs",
            "In a map like Dragon's End or Silverwastes, open the compass list and the map.",
            "Tekkit's chests and jumping puzzles show up beside the other packs', without doubles."),

        new("objective-chest", "Compass and trail",
            "Restart the bridge from today's build. Choose a pack chest with no trail to it, ideally one up high.",
            "In the game a beam stands on the chest at its own height; up close, the ring and marker sit on it."),
        new("objective-flat", "Compass and trail",
            "Choose a hero point or vista with no trail to it.",
            "A beam at the character's height that follows as you climb; the ring sits on the ground beside it."),
        new("objective-arrive", "Compass and trail",
            "Walk to an objective that has no trail.",
            "The ring flashes, and the phone clears the objective within a few seconds."),
        new("objective-boss", "Compass and trail",
            "Choose a world boss as the objective.",
            "Nothing is drawn in the game: its point is only the middle of its map."),

        new("voe-access", "Account",
            "Visit a Visions of Eternity map, then pull Account › Standing down.",
            "Note whether Visions of Eternity is listed now.", Auto: true)
    ];
}
