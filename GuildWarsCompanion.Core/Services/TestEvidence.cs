using GuildWarsCompanion.Host;

namespace GuildWarsCompanion.Services;

/// <summary>
/// Ticks a check of the <see cref="TestPlan"/> when the app sees it happen, in the debug build.
///
/// Only for what the app can prove: a notification was put on the shade, the playing line was
/// shown, the phone cleared an objective on arrival. Whether a line agrees with the game stays the
/// player's tick, because the app has nothing to compare it with.
///
/// The first sighting ticks; later ones only move the time. So a check the player unticked because
/// what showed was wrong stays unticked, and still says when it was last seen. In a Release build
/// this does nothing at all.
/// </summary>
public sealed class TestEvidence(ISettings settings)
{
    public void Saw(string id)
    {
#if DEBUG
        var seen = SeenKey(id);
        var first = settings.Get(seen, 0L) == 0;

        settings.Set(seen, DateTimeOffset.UtcNow.UtcTicks);
        if (first) settings.Set(TickKey(id), true);
#endif
    }

    /// <summary>When the app last saw it, or null.</summary>
    public DateTimeOffset? LastSeen(string id) =>
        settings.Get(SeenKey(id), 0L) is var ticks and > 0 ? new DateTimeOffset(ticks, TimeSpan.Zero) : null;

    public static string TickKey(string id) => $"testplan.{id}";

    private static string SeenKey(string id) => $"testplan.{id}.seen";
}
