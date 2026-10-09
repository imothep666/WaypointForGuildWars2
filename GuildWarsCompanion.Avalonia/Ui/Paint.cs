using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Immutable;

using MauiColor = Microsoft.Maui.Graphics.Color;

namespace GuildWarsCompanion.Ava.Ui;

/// <summary>
/// Core's colours, as Avalonia brushes.
///
/// The rows in Core carry <see cref="MauiColor"/>, the Microsoft.Maui.Graphics colour Core is
/// written against; Avalonia paints with brushes, so every such binding here goes through
/// <see cref="Brush"/>. Brushes are cached per colour: a list recycles the same handful of
/// rarity and spine colours hundreds of times.
/// </summary>
public static class Paint
{
    private static readonly Dictionary<uint, IBrush> Cache = [];

    public static readonly IValueConverter Brush =
        new FuncValueConverter<MauiColor?, IBrush?>(colour => colour is null ? null : BrushOf(colour));

    public static IBrush BrushOf(MauiColor colour)
    {
        colour.ToRgba(out var r, out var g, out var b, out var a);

        var key = (uint)(a << 24 | r << 16 | g << 8 | b);

        lock (Cache)
        {
            if (!Cache.TryGetValue(key, out var brush))
                Cache[key] = brush = new ImmutableSolidColorBrush(Color.FromArgb(a, r, g, b));

            return brush;
        }
    }
}

/// <summary>For a list's empty state: visible while the collection has nothing in it.</summary>
public static class Counts
{
    public static readonly IValueConverter IsZero = new FuncValueConverter<int, bool>(count => count == 0);

    public static readonly IValueConverter IsAny = new FuncValueConverter<int, bool>(count => count > 0);
}
