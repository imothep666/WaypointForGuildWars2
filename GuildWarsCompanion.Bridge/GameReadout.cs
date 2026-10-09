using System.Text.Json;

using GuildWarsCompanion.Bridge.Protocol;

namespace GuildWarsCompanion.Bridge;

/// <summary>
/// The window's and the phone's shared view of what the game is doing.
///
/// One poll feeds both: the window binds to the sentences, the phone gets the JSON. Keeping
/// them off separate polls means the two can never disagree about which character is logged in,
/// which would be a confusing thing to have to debug for no benefit.
/// </summary>
public sealed class GameReadout : IDisposable
{
    private readonly GameWatch _watch = new();

    /// <summary>Serialised once per poll rather than per request. A phone that polls every few
    /// seconds should not make this machine re-encode the same object each time.</summary>
    public string Json { get; private set; } = "{}";

    /// <summary>
    /// The same reading as an object, for the compass stream.
    ///
    /// The stream serialises per connection rather than sharing <see cref="Json"/>, because
    /// SignalR owns the encoding on its side of the wire and handing it a pre-made string would
    /// mean sending JSON inside JSON. Reference assignment, read from hub threads: a record is
    /// immutable once built, so a reader sees either the old one whole or the new one whole.
    /// </summary>
    public GameReport Report { get; private set; } = GameReport.Nothing("Looking for Guild Wars 2…");

    public string Line { get; private set; } = "Looking for Guild Wars 2…";

    public string Detail { get; private set; } = string.Empty;

    public bool IsLive { get; private set; }

    /// <summary>
    /// Whether MumbleLink itself could not be read — a fault, as opposed to a game that is
    /// merely closed. Kept apart so the window can tell the two states apart in one glance.
    /// </summary>
    public bool IsUnreadable { get; private set; }

    /// <param name="arrived">The objective the Blish HUD module last reported reached, carried to
    /// the phone in the report; see <see cref="GameReport.Arrived"/>.</param>
    public void Update(DateTimeOffset now, TrailArrival? arrived = null)
    {
        var report = _watch.Poll(now) with { Arrived = arrived };

        // Encoded before it becomes the current report: one that cannot be encoded must never be
        // what the stream sends, or every connection breaks on it until something replaces it.
        var json = JsonSerializer.Serialize(report, GameJson.Default.GameReport);

        Report = report;
        Json = json;
        IsLive = report.IsLive;
        IsUnreadable = _watch.Problem is not null;

        if (report.State is not { } state)
        {
            Line = "Guild Wars 2 is not running";
            Detail = report.Note;
            return;
        }

        Line = state.Line;

        // The state is worth showing even when it is not live, because it is the last thing that
        // was true — but it has to be labelled, or it reads as somebody currently in combat in
        // a map they left last night.
        Detail = report.IsLive
            ? Describe(report, state)
            : $"Last seen in {report.MapLabel}. {report.Note}";
    }

    private static string Describe(GameReport report, GameState state)
    {
        // The name if the API has answered about this map yet, the id if it has not. Reading
        // "Map 28" for a second before it becomes "Wayfarer Foothills" is the intended
        // behaviour: the lookup happens off this thread and the window is not made to wait.
        var parts = new List<string> { report.MapLabel };

        if (state.IsMounted) parts.Add($"on a {state.Mount}");
        if (state.InCombat) parts.Add("in combat");

        parts.Add(state.HasFocus ? "playing" : "alt-tabbed away");

        return string.Join(" · ", parts);
    }

    public void Dispose() => _watch.Dispose();
}
