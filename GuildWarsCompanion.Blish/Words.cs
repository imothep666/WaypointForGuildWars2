using Blish_HUD;
using Gw2Sharp.WebApi;

namespace GuildWarsCompanion.Blish;

/// <summary>
/// The module's few words of its own, in the language Blish HUD is set to: German or English.
///
/// Everything with a name in it — the objective, an alert — arrives already in the phone's
/// language. What is left is a handful of labels, too few for resource files.
/// </summary>
internal static class Words
{
    private static bool German => GameService.Overlay.UserLocale.Value == Locale.German;

    public static string Arrived => German ? "Angekommen" : "Arrived";

    public static string OtherMap => German ? "Auf einer anderen Karte" : "On another map";

    public static string Connected => German
        ? "Waypoint: mit der Bridge verbunden. Klicken blendet die Zielkarte ein oder aus."
        : "Waypoint: connected to the bridge. Click to show or hide the objective card.";

    public static string Waiting => German
        ? "Waypoint: wartet auf die Bridge auf diesem PC."
        : "Waypoint: waiting for the bridge on this PC.";

    public static string ShowCard => German ? "Zielkarte zeigen" : "Show the objective card";

    public static string ShowAlerts => German ? "Hinweise vom Handy zeigen" : "Show alerts from the phone";

    public static string ShowAlertsWhy => German
        ? "Preisbeobachtungen, Weltbosse und andere Hinweise der Waypoint-App erscheinen im Spiel statt auf dem Handy, solange du spielst."
        : "Price watches, world bosses and the Waypoint app's other alerts appear in the game instead of on the phone while you play.";

    public static string ShowCardWhy => German
        ? "Name, Entfernung und Höhe des Ziels, das du in der Waypoint-App gewählt hast. Zum Verschieben ziehen."
        : "The name, distance and height of the objective chosen in the Waypoint app. Drag it to move it.";
}
