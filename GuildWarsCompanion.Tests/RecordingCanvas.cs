using Microsoft.Maui.Graphics;
using Microsoft.Maui.Graphics.Text;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// One call a drawing made, with the alpha it was made at. Coordinates are as passed, before any
/// transform: the tests here ask what was drawn, in what order and how strongly, not where the
/// pixels ended up.
/// </summary>
public sealed record DrawCall(string Name, float X, float Y, float Width, float Height, float Alpha,
    string? Text = null);

/// <summary>
/// A canvas that draws nothing and remembers everything, so a drawable can be tested without a
/// screen. Only the calls the app's drawing makes are recorded; the rest are accepted silently.
/// </summary>
public sealed class RecordingCanvas : ICanvas
{
    private readonly Stack<float> _alphas = new();

    public List<DrawCall> Calls { get; } = [];

    public float DisplayScale { get; set; } = 1;

    public float StrokeSize { get; set; } = 1;

    public float MiterLimit { get; set; }

    public Color StrokeColor { get; set; } = Colors.Black;

    public LineCap StrokeLineCap { get; set; }

    public LineJoin StrokeLineJoin { get; set; }

    public float[] StrokeDashPattern { get; set; } = [];

    public float StrokeDashOffset { get; set; }

    public Color FillColor { get; set; } = Colors.White;

    public Color FontColor { get; set; } = Colors.Black;

    public IFont Font { get; set; } = Microsoft.Maui.Graphics.Font.Default;

    public float FontSize { get; set; } = 12;

    public float Alpha { get; set; } = 1;

    public bool Antialias { get; set; }

    public BlendMode BlendMode { get; set; }

    private void Record(string name, float x = 0, float y = 0, float width = 0, float height = 0,
        string? text = null) =>
        Calls.Add(new DrawCall(name, x, y, width, height, Alpha, text));

    public void DrawLine(float x1, float y1, float x2, float y2) => Record(nameof(DrawLine), x1, y1, x2, y2);

    public void DrawArc(float x, float y, float width, float height, float startAngle, float endAngle,
        bool clockwise, bool closed) => Record(nameof(DrawArc), x, y, width, height);

    public void DrawRectangle(float x, float y, float width, float height) =>
        Record(nameof(DrawRectangle), x, y, width, height);

    public void DrawRoundedRectangle(float x, float y, float width, float height, float cornerRadius) =>
        Record(nameof(DrawRoundedRectangle), x, y, width, height);

    public void DrawEllipse(float x, float y, float width, float height) =>
        Record(nameof(DrawEllipse), x, y, width, height);

    public void DrawString(string value, float x, float y, HorizontalAlignment horizontalAlignment) =>
        Record(nameof(DrawString), x, y, text: value);

    public void DrawString(string value, float x, float y, float width, float height,
        HorizontalAlignment horizontalAlignment, VerticalAlignment verticalAlignment,
        TextFlow textFlow = TextFlow.ClipBounds, float lineSpacingAdjustment = 0) =>
        Record(nameof(DrawString), x, y, width, height, value);

    public void DrawText(IAttributedText value, float x, float y, float width, float height) =>
        Record(nameof(DrawText), x, y, width, height, value.Text);

    public void DrawPath(PathF path) => Record(nameof(DrawPath));

    public void FillPath(PathF path, WindingMode windingMode) => Record(nameof(FillPath));

    public void FillArc(float x, float y, float width, float height, float startAngle, float endAngle,
        bool clockwise) => Record(nameof(FillArc), x, y, width, height);

    public void FillRectangle(float x, float y, float width, float height) =>
        Record(nameof(FillRectangle), x, y, width, height);

    public void FillRoundedRectangle(float x, float y, float width, float height, float cornerRadius) =>
        Record(nameof(FillRoundedRectangle), x, y, width, height);

    public void FillEllipse(float x, float y, float width, float height) =>
        Record(nameof(FillEllipse), x, y, width, height);

    public void DrawImage(IImage image, float x, float y, float width, float height) =>
        Record(nameof(DrawImage), x, y, width, height);

    public void SubtractFromClip(float x, float y, float width, float height)
    {
    }

    public void ClipPath(PathF path, WindingMode windingMode = WindingMode.NonZero)
    {
    }

    public void ClipRectangle(float x, float y, float width, float height)
    {
    }

    public void Translate(float tx, float ty)
    {
    }

    public void Scale(float sx, float sy)
    {
    }

    public void Rotate(float degrees, float x, float y)
    {
    }

    public void Rotate(float degrees)
    {
    }

    public void ConcatenateTransform(System.Numerics.Matrix3x2 transform)
    {
    }

    public void SaveState() => _alphas.Push(Alpha);

    public bool RestoreState()
    {
        if (_alphas.Count == 0) return false;

        Alpha = _alphas.Pop();

        return true;
    }

    public void ResetState() => Alpha = 1;

    public void SetShadow(SizeF offset, float blur, Color color)
    {
    }

    public void SetFillPaint(Paint paint, RectF rectangle)
    {
    }

    public SizeF GetStringSize(string value, IFont font, float fontSize) => new(value.Length * fontSize / 2, fontSize);

    public SizeF GetStringSize(string value, IFont font, float fontSize, HorizontalAlignment horizontalAlignment,
        VerticalAlignment verticalAlignment) => GetStringSize(value, font, fontSize);
}

/// <summary>An image that is never drawn for real: drawables and caches only hold it.</summary>
public sealed class BlankImage : IImage
{
    public float Width => 256;

    public float Height => 256;

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
    }

    public IImage Downsize(float maxWidthOrHeight, bool disposeOriginal = false) => this;

    public IImage Downsize(float maxWidth, float maxHeight, bool disposeOriginal = false) => this;

    public IImage Resize(float width, float height, ResizeMode resizeMode = ResizeMode.Fit,
        bool disposeOriginal = false) => this;

    public void Save(Stream stream, ImageFormat format = ImageFormat.Png, float quality = 1) =>
        throw new NotSupportedException();

    public Task SaveAsync(Stream stream, ImageFormat format = ImageFormat.Png, float quality = 1) =>
        throw new NotSupportedException();

    public IImage ToPlatformImage() => this;

    public void Dispose()
    {
    }
}
