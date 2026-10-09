namespace GuildWarsCompanion.Services;

/// <summary>
/// Where the character has just been, in continent coordinates.
///
/// Kept in world coordinates rather than in dial coordinates, and that is the whole design. The
/// dial is drawn relative to which way the character is facing, so a trail stored as angles would
/// be wrong the instant they turned — it has to be re-projected from world positions every frame,
/// exactly as the landmarks are.
///
/// A ring buffer with a fixed cap, so a screen left open for an hour costs the same as one opened
/// a minute ago.
/// </summary>
public sealed class WalkedPath
{
    /// <summary>
    /// How many points the tail keeps.
    ///
    /// At <see cref="MinimumStep"/> apart this is a couple of thousand continent units of history,
    /// which is about the width of the dial at full range — further back than that would be
    /// drawn on top of itself at the rim and tell nobody anything.
    /// </summary>
    public const int Capacity = 160;

    /// <summary>
    /// How far the character has to move before a new point is recorded.
    ///
    /// MumbleLink jitters by a unit or two while somebody stands still, and without this the
    /// buffer fills with a shivering dot in ten seconds and the real trail is gone. Standing
    /// still should cost nothing at all.
    /// </summary>
    private const double MinimumStep = 12;

    /// <summary>
    /// How far apart two consecutive points can be before the trail is treated as broken.
    ///
    /// A waypoint teleport, a map change or a mount leap puts the character somewhere unrelated,
    /// and joining that up would draw a confident straight line through terrain nobody walked.
    /// Past this the tail is dropped and started again.
    /// </summary>
    private const double Jump = 600;

    private readonly (double X, double Y)[] _points = new (double, double)[Capacity];
    private int _count;
    private int _next;

    /// <summary>Which map the trail belongs to, so crossing a border clears it.</summary>
    private int _mapId = -1;

    /// <summary>Oldest first. A fresh array each call, because the caller reads it on the drawing
    /// thread while this is being written on the stream's.</summary>
    public IReadOnlyList<(double X, double Y)> Points()
    {
        lock (_points)
        {
            var taken = new (double X, double Y)[_count];

            for (var i = 0; i < _count; i++)
                taken[i] = _points[(_next - _count + i + Capacity) % Capacity];

            return taken;
        }
    }

    /// <summary>
    /// Copies the trail into a buffer the caller already owns.
    ///
    /// Beside <see cref="Points"/> rather than in place of it. Points hands back a fresh list,
    /// which is what a test wants and what a caller reading it once wants; the dial asks thirty
    /// times a second, and an array per call at that rate was part of what the compass screen
    /// was measured never giving back.
    /// </summary>
    /// <returns>How many entries of the buffer are live. Anything past that is stale and must
    /// not be read.</returns>
    public int Snapshot((double X, double Y)[] buffer)
    {
        lock (_points)
        {
            var taken = Math.Min(_count, buffer.Length);

            for (var i = 0; i < taken; i++)
                buffer[i] = _points[(_next - _count + i + Capacity) % Capacity];

            return taken;
        }
    }

    /// <summary>
    /// Records where the character is now, if they have gone anywhere worth recording.
    /// </summary>
    public void Add(int mapId, double x, double y)
    {
        lock (_points)
        {
            if (mapId != _mapId)
            {
                _mapId = mapId;
                _count = 0;
                _next = 0;
            }

            if (_count > 0)
            {
                var last = _points[(_next - 1 + Capacity) % Capacity];

                var moved = Bearing.Distance(last.X, last.Y, x, y);

                if (moved < MinimumStep) return;

                // A teleport, not a walk. Start again rather than draw a line through nowhere.
                if (moved > Jump)
                {
                    _count = 0;
                    _next = 0;
                }
            }

            _points[_next] = (x, y);
            _next = (_next + 1) % Capacity;

            if (_count < Capacity) _count++;
        }
    }

    public void Clear()
    {
        lock (_points)
        {
            _count = 0;
            _next = 0;
            _mapId = -1;
        }
    }
}
