namespace GuildWarsCompanion.Bridge.Protocol;

/// <summary>
/// Polls MumbleLink and says what the game is doing, if anything.
///
/// Holds the two pieces of state the answer needs: when the tick last moved, and the map names
/// seen so far. Everything else is worked out fresh each time, because the block is rewritten
/// every frame and there is nothing worth caching in it.
/// </summary>
/// <param name="maps">
/// Where map names come from. Injectable so a caller can hand over one that does not reach the
/// network; left alone it asks the real API.
/// </param>
public sealed class GameWatch(MapNames? maps = null) : IDisposable
{
    private readonly MumbleSource _source = new();
    private readonly MapNames _maps = maps ?? Gw2Maps.Names();
    private Liveness _liveness;
    private GameState? _last;

    public GameReport Poll(DateTimeOffset now)
    {
        var block = _source.Read();

        if (block.IsEmpty)
            return GameReport.Nothing(_source.Problem ?? "MumbleLink is not readable.");

        var state = MumbleLink.Decode(block);

        if (state is null)
            return GameReport.Nothing(
                "MumbleLink is there and empty — Guild Wars 2 is not running, or has not " +
                "reached a character yet. The mapping is this bridge's own; the game fills it " +
                "in within seconds of starting.");

        _liveness = _liveness.Advance(state.Tick, now);
        _last = state;

        var live = _liveness.IsLiveAt(now);

        return new GameReport(
            state,
            live,
            live
                ? "Reading Guild Wars 2 now."
                : "Guild Wars 2 is not running — this is what it left behind when it closed.",
            // Whatever is known by now; the request for anything else is under way and the
            // name lands on a later poll. Null here is "not yet", not "no such map".
            _maps.Lookup(state.MapId, now));
    }

    /// <summary>The last thing seen, live or not. For a screen that would rather show something
    /// stale and say so than show nothing.</summary>
    public GameState? Last => _last;

    /// <summary>
    /// Why MumbleLink could not be read, or null when it could.
    ///
    /// The distinction a status light needs and <see cref="GameReport.IsLive"/> cannot make.
    /// A readable block with a zero tick means the game is simply not running, which is not a
    /// fault and must not be drawn as one; being unable to open the mapping at all is.
    /// </summary>
    public string? Problem => _source.Problem;

    public void Dispose() => _source.Dispose();
}
