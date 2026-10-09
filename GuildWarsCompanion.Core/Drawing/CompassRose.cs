using GuildWarsCompanion.Services;


namespace GuildWarsCompanion.Drawing;

/// <summary>
/// The dial, drawn rather than composed out of views.
///
/// A <see cref="GraphicsView"/> because this redraws ten times a second with several dozen marks
/// on it: rebuilding a tree of rotated Border elements at that rate is how a live screen becomes
/// a warm phone. One <c>Invalidate</c> and one <c>Draw</c> is the whole cost.
///
/// Laid out like a radar warning receiver, because that instrument already solves this exact
/// problem — several things at once, each needing a direction and a rough range, readable in the
/// half second somebody can spare mid-fight. Angle is bearing, and distance from the middle is
/// range: the centre is here. On a real set the radial axis is signal strength, but a strong
/// emitter is a near one, so the instinct carries over unchanged.
///
/// The dial turns with the character. Top is always straight ahead, so the reading is "bear
/// left" and never "the objective is at 47 degrees and you are facing 92".
/// </summary>
public sealed class CompassRose : IDrawable
{
    /// <summary>
    /// Everything in range, the chosen one among them.
    ///
    /// An array and a count rather than a list, because the array is reused between frames and
    /// is therefore longer than what is in it. Read <see cref="BlipCount"/> entries and no more:
    /// what follows is last frame's, still sitting there.
    /// </summary>
    public Blip[] Blips { get; set; } = [];

    /// <summary>How many of <see cref="Blips"/> are this frame's.</summary>
    public int BlipCount { get; set; }

    /// <summary>
    /// Where the character has just walked, oldest first, already projected into the dial's frame
    /// as (bearing, distance) pairs.
    ///
    /// Projected by the caller rather than stored that way, because the dial is drawn relative to
    /// the character's facing: a trail kept as angles would swing wildly the moment they turned
    /// on the spot. See <see cref="Services.WalkedPath"/>.
    /// </summary>
    public (double Relative, double Distance)[] Trail { get; set; } = [];

    /// <summary>How many of <see cref="Trail"/> are this frame's. Same reason as
    /// <see cref="BlipCount"/>: the array outlives the frame that filled it.</summary>
    public int TrailCount { get; set; }

    /// <summary>The trail still to walk to the objective, projected the same way; nearest first.
    /// See <see cref="Services.TrailGuide"/>.</summary>
    public (double Relative, double Distance)[] Guide { get; set; } = [];

    /// <summary>How many of <see cref="Guide"/> are this frame's.</summary>
    public int GuideCount { get; set; }

    /// <summary>Which way the character faces, for turning the ring's letters.</summary>
    public double Facing { get; set; }

    /// <summary>Dimmed whole when the stream is not delivering, because marks frozen at their
    /// last angles are indistinguishable from marks that simply are not moving.</summary>
    public bool IsLive { get; set; }

    /// <summary>The game's own marker art, or null per kind while it is still loading.</summary>
    public Func<TargetKind, IImage?>? Symbol { get; set; }

    /// <summary>
    /// How the phone is being held, in radians clockwise from upright.
    ///
    /// The dial is turned back by this, so it stays upright to the eye whatever the handset is
    /// doing. The window itself never turns — the app is portrait-locked on purpose — so this is
    /// the only thing that keeps a propped-up phone readable.
    /// </summary>
    public double Tilt { get; set; }

    // The app's own palette, by value: a drawable cannot reach the XAML resource dictionary.
    private static readonly Color Brass = Color.FromArgb("#C9A227");
    private static readonly Color BrassDim = Color.FromArgb("#6B5A1E");
    private static readonly Color Mist = Color.FromArgb("#9BA8B8");
    private static readonly Color Slate = Color.FromArgb("#18212C");
    private static readonly Color Ember = Color.FromArgb("#C2603C");

    /// <summary>Big enough to tell a heart from a vista, small enough that a crowded dial is
    /// still a dial rather than a pile of stamps.</summary>
    private const float SymbolSize = 22;

    public void Draw(ICanvas canvas, RectF area)
    {
        // A square frame, so turning the dial can never push any of it outside the area the view
        // was given. The readout used to be drawn beneath it and is not any more: below the dial
        // is off the side of the screen once the phone is turned, which is exactly where the
        // chosen objective went missing. It lives in the rail now.
        var side = Math.Min(area.Width, area.Height);

        if (side <= 0) return;

        var centre = new PointF(area.Center.X, area.Center.Y);
        var radius = side / 2 - 12;

        if (radius <= 0) return;

        canvas.Alpha = IsLive ? 1f : 0.35f;

        // Everything from here is drawn in the dial's own frame, turned against the phone. The
        // dial is a circle, so turning it can never push anything outside the area it was given.
        canvas.SaveState();
        canvas.Rotate((float)(-Tilt * 180 / Math.PI), centre.X, centre.Y);

        Dial(canvas, centre, radius);
        RangeRings(canvas, centre, radius);
        Ticks(canvas, centre, radius);
        Cardinals(canvas, centre, radius);
        Ahead(canvas, centre, radius);

        // Under the marks: the trail is context, not a target.
        DrawTrail(canvas, centre, radius);

        // Over the walked path, under the marks: the way to go, as opposed to the way come.
        DrawGuide(canvas, centre, radius);

        // Unchosen first, so the chosen one is never drawn under something else. Two indexed
        // passes rather than two Where clauses: this runs thirty times a second, and a LINQ
        // iterator per pass is two more objects a frame for a loop over at most thirteen items.
        for (var i = 0; i < BlipCount; i++)
            if (!Blips[i].IsChosen)
                DrawBlip(canvas, centre, radius, Blips[i]);

        for (var i = 0; i < BlipCount; i++)
            if (Blips[i].IsChosen)
                DrawBlip(canvas, centre, radius, Blips[i]);

        Here(canvas, centre);

        canvas.RestoreState();

        canvas.Alpha = 1f;
    }

    private static void Dial(ICanvas canvas, PointF centre, float radius)
    {
        canvas.FillColor = Slate;
        canvas.FillCircle(centre, radius);

        canvas.StrokeColor = BrassDim;
        canvas.StrokeSize = 2;
        canvas.DrawCircle(centre, radius);
    }

    /// <summary>
    /// The range rings, labelled.
    ///
    /// Without these the dial says "roughly that way, roughly that far" and the radial axis is
    /// decoration. With them it is a measurement: a mark between the second and third ring is
    /// between five hundred and two thousand units out, and the scale being logarithmic stops
    /// mattering because nobody reads it as a ruler.
    /// </summary>
    private static void RangeRings(ICanvas canvas, PointF centre, float radius)
    {
        canvas.StrokeSize = 1;
        canvas.FontSize = 9;

        foreach (var ring in RadarScale.Rings)
        {
            var at = radius * (float)RadarScale.Fraction(ring);

            if (at >= radius - 2) continue;

            canvas.StrokeColor = BrassDim.WithAlpha(0.5f);
            canvas.DrawCircle(centre, at);

            canvas.FontColor = Mist.WithAlpha(0.5f);

            canvas.DrawString(
                RadarScale.Label(ring), centre.X - 18, centre.Y - at - 12, 36, 12,
                HorizontalAlignment.Center, VerticalAlignment.Center);
        }
    }

    /// <summary>
    /// Thirty-six ticks, the cardinals longer, turning with the character.
    ///
    /// The turning is what communicates that the player moved rather than the world: marks alone
    /// would look identical either way.
    /// </summary>
    private void Ticks(ICanvas canvas, PointF centre, float radius)
    {
        canvas.StrokeSize = 1;

        for (var i = 0; i < 36; i++)
        {
            var cardinal = i % 9 == 0;
            var angle = i * Math.Tau / 36 - Facing;

            var outer = At(centre, radius - 2, angle);
            var inner = At(centre, radius - (cardinal ? 14 : 7), angle);

            canvas.StrokeColor = cardinal ? Brass : BrassDim;
            canvas.DrawLine(inner, outer);
        }
    }

    private void Cardinals(ICanvas canvas, PointF centre, float radius)
    {
        string[] letters = ["N", "E", "S", "W"];

        canvas.FontSize = 13;
        canvas.FontColor = Mist;

        for (var i = 0; i < letters.Length; i++)
        {
            var at = At(centre, radius - 30, i * Math.PI / 2 - Facing);

            canvas.DrawString(
                letters[i], at.X - 10, at.Y - 9, 20, 18,
                HorizontalAlignment.Center, VerticalAlignment.Center);
        }
    }

    /// <summary>
    /// The fixed mark at the top: where the character is already looking.
    ///
    /// The path is kept rather than rebuilt. It is three points that depend on nothing but the
    /// size of the dial, and the dial is only resized when the window is — so building a PathF
    /// on every frame was an allocation for a shape that had not changed since the screen opened.
    /// </summary>
    private void Ahead(ICanvas canvas, PointF centre, float radius)
    {
        if (_ahead is null || _aheadAt != centre || _aheadRadius != radius)
        {
            var path = new PathF();

            path.MoveTo(centre.X, centre.Y - radius + 4);
            path.LineTo(centre.X - 7, centre.Y - radius + 18);
            path.LineTo(centre.X + 7, centre.Y - radius + 18);
            path.Close();

            _ahead = path;
            _aheadAt = centre;
            _aheadRadius = radius;
        }

        canvas.FillColor = Mist;
        canvas.FillPath(_ahead);
    }

    private PathF? _ahead;
    private PointF _aheadAt;
    private float _aheadRadius;

    /// <summary>
    /// One mark, at its bearing and its range.
    ///
    /// The chosen one gets a ring round it and a line back to the middle — on a dial holding
    /// forty landmarks, a colour change alone loses the thing the player is actually walking
    /// towards.
    /// </summary>
    private void DrawBlip(ICanvas canvas, PointF centre, float radius, in Blip blip)
    {
        var out_ = radius * (float)RadarScale.Fraction(blip.Distance);

        // Kept inside the rim so a far mark is not half-clipped by the dial's edge.
        out_ = Math.Min(out_, radius - 26);

        var at = At(centre, out_, blip.Relative);

        // The straight line to the objective, unless a trail is being drawn to it: then the line
        // would be the one direction the trail exists to argue with.
        if (blip.IsChosen && GuideCount < 2)
        {
            canvas.StrokeColor = Brass.WithAlpha(0.45f);
            canvas.StrokeSize = 1;
            canvas.DrawLine(centre, at);
        }

        // Out of range at all is worth showing differently: the direction is still true and the
        // distance has stopped meaning anything. ICanvas.Alpha is write-only, so the base value
        // is tracked here rather than read back.
        var baseAlpha = IsLive ? 1f : 0.35f;

        var alpha = baseAlpha * (RadarScale.BeyondRange(blip.Distance) ? 0.45f : 1f);

        canvas.Alpha = alpha;

        if (Symbol?.Invoke(blip.Kind) is { } image)
        {
            // By hand where the platform allows it. DrawImage is the one call on this dial that
            // grows the process, and it is called more than any other; see SymbolStamp.
            if (!SymbolStamp.TryDraw(canvas, image, at.X, at.Y, SymbolSize, alpha))
                canvas.DrawImage(
                    image, at.X - SymbolSize / 2, at.Y - SymbolSize / 2, SymbolSize, SymbolSize);
        }
        else
        {
            // No art yet, or it would not download. A dot still carries direction and range,
            // which is most of the screen's job.
            canvas.FillColor = TargetDot.For(blip.Kind);
            canvas.FillCircle(at, 5);
        }

        canvas.Alpha = baseAlpha;

        if (!blip.IsChosen) return;

        canvas.StrokeColor = Brass;
        canvas.StrokeSize = 2;
        canvas.DrawCircle(at, SymbolSize / 2 + 5);
    }

    /// <summary>
    /// The walked path, fading out towards the oldest end.
    ///
    /// Drawn as a run of short segments rather than one path so each can carry its own alpha —
    /// a single stroked path can only have one. The fade is what makes it read as a direction of
    /// travel instead of a loop of string: the bright end is where the character is now.
    ///
    /// Every point goes through the same <see cref="RadarScale"/> as the marks, so the trail
    /// bends as it approaches the rim. That is honest rather than pretty: it is the same
    /// compression the landmarks are drawn with, and a trail drawn linearly beside logarithmic
    /// marks would cross them in the wrong places.
    /// </summary>
    private void DrawTrail(ICanvas canvas, PointF centre, float radius)
    {
        if (TrailCount < 2) return;

        var baseAlpha = IsLive ? 1f : 0.35f;

        canvas.StrokeSize = 2;
        canvas.StrokeLineCap = LineCap.Round;

        PointF? previous = null;

        for (var i = 0; i < TrailCount; i++)
        {
            var (relative, distance) = Trail[i];

            var out_ = Math.Min(radius * (float)RadarScale.Fraction(distance), radius - 4);

            var at = At(centre, out_, relative);

            if (previous is { } from)
            {
                // Oldest almost invisible, newest nearly solid.
                var age = (i + 1) / (float)TrailCount;

                canvas.Alpha = baseAlpha * 0.55f * age;
                canvas.StrokeColor = Mist;
                canvas.DrawLine(from, at);
            }

            previous = at;
        }

        canvas.Alpha = baseAlpha;
    }

    /// <summary>
    /// The way to the objective along a trail: solid brass, from the character outwards, through
    /// the same <see cref="RadarScale"/> as everything else so it meets the objective's mark where
    /// the mark is drawn.
    /// </summary>
    private void DrawGuide(ICanvas canvas, PointF centre, float radius)
    {
        if (GuideCount < 2) return;

        canvas.StrokeColor = Brass;
        canvas.StrokeSize = 3;
        canvas.StrokeLineCap = LineCap.Round;

        var previous = centre;

        for (var i = 0; i < GuideCount; i++)
        {
            var (relative, distance) = Guide[i];

            var out_ = Math.Min(radius * (float)RadarScale.Fraction(distance), radius - 4);

            var at = At(centre, out_, relative);

            // From the character to the first trail point too, so the way starts underfoot.
            canvas.DrawLine(previous, at);

            previous = at;
        }
    }

    /// <summary>The pip in the middle: the character. Everything else is measured from it.</summary>
    private static void Here(ICanvas canvas, PointF centre)
    {
        canvas.FillColor = Slate;
        canvas.FillCircle(centre, 5);

        canvas.StrokeColor = Mist;
        canvas.StrokeSize = 2;
        canvas.DrawCircle(centre, 5);
    }

    /// <summary>
    /// A point on the dial at a bearing, measured clockwise from the top.
    ///
    /// Screen Y grows downwards, so "up" is -Y and a clockwise bearing puts sin on X and -cos on
    /// Y. The same shape as the continent-coordinate convention in <see cref="Bearing"/>, for
    /// the same reason.
    /// </summary>
    private static PointF At(PointF centre, float radius, double bearing) =>
        new(
            centre.X + radius * (float)Math.Sin(bearing),
            centre.Y - radius * (float)Math.Cos(bearing));
}
