using System.Collections.Concurrent;
using System.Globalization;
using System.Xml.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

using GuildWarsCompanion.Services;

namespace GuildWarsCompanion.Ava.Ui;

/// <summary>
/// An image that is either a URL or one of the files bundled under Assets/Images:
/// <c>ui:AppImage.Source="{Binding BannerSource}"</c>.
///
/// Core names a picture the way MAUI's Image took it — <c>spec_12.jpg</c> for a cut banner,
/// <c>body_charr.png</c> for a silhouette, a render.guildwars2.com address for anything else — so
/// one property has to understand all three. The retired MAUI app rasterised its SVGs to PNG at
/// build time, which is why Core asks for <c>body_charr.png</c> when the file is
/// <c>body_charr.svg</c>: a name with no file of its own falls back to the SVG beside it, drawn by
/// <see cref="SvgArt"/>.
/// </summary>
public static class AppImage
{
    public static readonly AttachedProperty<string?> SourceProperty =
        AvaloniaProperty.RegisterAttached<Image, string?>("Source", typeof(AppImage));

    private const string Folder = "avares://GuildWarsCompanion.Avalonia/Assets/Images/";

    private static readonly ConcurrentDictionary<string, IImage?> Bundled = new();

    static AppImage() => SourceProperty.Changed.AddClassHandler<Image>(OnSourceChanged);

    public static string? GetSource(Image image) => image.GetValue(SourceProperty);

    public static void SetSource(Image image, string? value) => image.SetValue(SourceProperty, value);

    private static void OnSourceChanged(Image image, AvaloniaPropertyChangedEventArgs e)
    {
        var source = e.NewValue as string;

        if (source is not null && source.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            WebImage.SetUrl(image, source);
            return;
        }

        WebImage.SetUrl(image, null);
        image.Source = string.IsNullOrEmpty(source) ? null : Bundled.GetOrAdd(source, Load);
    }

    private static IImage? Load(string name)
    {
        try
        {
            var exact = new Uri(Folder + name);
            if (AssetLoader.Exists(exact)) return new Bitmap(AssetLoader.Open(exact));

            var svg = new Uri(Folder + Path.ChangeExtension(name, ".svg"));
            if (AssetLoader.Exists(svg)) return SvgArt.Read(AssetLoader.Open(svg));
        }
        catch (Exception)
        {
            // A broken bundled file draws nothing, as a failed web image does.
        }

        return null;
    }
}

/// <summary>
/// Just enough SVG for the papercut silhouettes and emblems the app ships: groups with a fill and
/// a translate, holding circles, ellipses and paths. Avalonia has no SVG of its own, and pulling in
/// a renderer for seven hand-drawn files is more than they need.
/// </summary>
public static class SvgArt
{
    public static IImage? Read(Stream stream)
    {
        var root = XDocument.Load(stream).Root;
        if (root is null) return null;

        var box = ViewBox(root);
        var drawing = new DrawingGroup();

        // An invisible frame the size of the view box, so the image keeps the drawing's own
        // margins rather than shrinking to the shapes inside it.
        drawing.Children.Add(new GeometryDrawing
        {
            Brush = Brushes.Transparent,
            Geometry = new RectangleGeometry(box)
        });

        Add(drawing, root, null, Matrix.Identity);

        return new DrawingImage(drawing);
    }

    private static void Add(DrawingGroup into, XElement element, IBrush? fill, Matrix transform)
    {
        fill = Fill(element) ?? fill;
        transform = Translate(element) * transform;

        foreach (var child in element.Elements())
        {
            var geometry = child.Name.LocalName switch
            {
                "g" => null,
                "circle" => Circle(child),
                "ellipse" => Ellipse(child),
                "path" => child.Attribute("d") is { } d ? Geometry.Parse(d.Value) : null,
                "rect" => Rectangle(child),
                _ => null
            };

            if (child.Name.LocalName == "g")
            {
                Add(into, child, fill, transform);
                continue;
            }

            if (geometry is null) continue;

            var own = Translate(child) * transform;
            if (own != Matrix.Identity) geometry.Transform = new MatrixTransform(own);

            into.Children.Add(new GeometryDrawing { Brush = Fill(child) ?? fill ?? Brushes.Black, Geometry = geometry });
        }
    }

    private static Rect ViewBox(XElement root)
    {
        if (root.Attribute("viewBox")?.Value.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries) is { Length: 4 } parts)
            return new Rect(Number(parts[0]), Number(parts[1]), Number(parts[2]), Number(parts[3]));

        return new Rect(0, 0, Number(root, "width"), Number(root, "height"));
    }

    private static Geometry Circle(XElement e)
    {
        var r = Number(e, "r");
        return new EllipseGeometry(new Rect(Number(e, "cx") - r, Number(e, "cy") - r, r * 2, r * 2));
    }

    private static Geometry Ellipse(XElement e)
    {
        var rx = Number(e, "rx");
        var ry = Number(e, "ry");
        return new EllipseGeometry(new Rect(Number(e, "cx") - rx, Number(e, "cy") - ry, rx * 2, ry * 2));
    }

    private static Geometry Rectangle(XElement e) =>
        new RectangleGeometry(new Rect(Number(e, "x"), Number(e, "y"), Number(e, "width"), Number(e, "height")));

    private static IBrush? Fill(XElement e) =>
        e.Attribute("fill")?.Value is { } fill && fill != "none" && Color.TryParse(fill, out var colour)
            ? new SolidColorBrush(colour)
            : null;

    /// <summary>translate(x,y) only, which is the one transform these files use.</summary>
    private static Matrix Translate(XElement e)
    {
        if (e.Attribute("transform")?.Value is not { } value || !value.StartsWith("translate(", StringComparison.Ordinal))
            return Matrix.Identity;

        var parts = value["translate(".Length..].TrimEnd(')').Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
        var x = parts.Length > 0 ? Number(parts[0]) : 0;
        var y = parts.Length > 1 ? Number(parts[1]) : 0;

        return Matrix.CreateTranslation(x, y);
    }

    private static double Number(XElement e, string name) =>
        e.Attribute(name)?.Value is { } value ? Number(value) : 0;

    private static double Number(string value) =>
        double.TryParse(value.Replace("px", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? number
            : 0;
}

/// <summary>
/// The paper doll's layout: each tile placed by its <see cref="LoadoutSlot.Bounds"/>, which are
/// proportional in position and fixed in size — what MAUI's AbsoluteLayout calls
/// PositionProportional. A tile at X 0.5 sits centred, at 1 against the right edge.
/// </summary>
public sealed class DollPanel : Panel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children)
        {
            var bounds = BoundsOf(child);
            child.Measure(new Size(bounds.Width, bounds.Height));
        }

        return new Size(
            double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var child in Children)
        {
            var bounds = BoundsOf(child);
            var x = (finalSize.Width - bounds.Width) * bounds.X;
            var y = (finalSize.Height - bounds.Height) * bounds.Y;

            child.Arrange(new Rect(x, y, bounds.Width, bounds.Height));
        }

        return finalSize;
    }

    private static Microsoft.Maui.Graphics.Rect BoundsOf(Control child) =>
        child.DataContext is LoadoutSlot slot ? slot.Bounds : default;
}

/// <summary>
/// A row of children with the spare width shared out between them — MAUI's FlexLayout with
/// JustifyContent SpaceBetween — so the three trait tiers divide the banner the way the game
/// divides them, the first at one end and the last at the other.
/// </summary>
public sealed class SpreadPanel : Panel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        double width = 0, height = 0;

        foreach (var child in Children)
        {
            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            width += child.DesiredSize.Width;
            height = Math.Max(height, child.DesiredSize.Height);
        }

        return new Size(double.IsInfinity(availableSize.Width) ? width : availableSize.Width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var used = Children.Sum(child => child.DesiredSize.Width);
        var gap = Children.Count > 1 ? Math.Max(0, (finalSize.Width - used) / (Children.Count - 1)) : 0;
        var x = 0.0;

        foreach (var child in Children)
        {
            var top = (finalSize.Height - child.DesiredSize.Height) / 2;
            child.Arrange(new Rect(x, top, child.DesiredSize.Width, child.DesiredSize.Height));
            x += child.DesiredSize.Width + gap;
        }

        return finalSize;
    }
}
