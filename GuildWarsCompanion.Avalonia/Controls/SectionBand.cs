using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace GuildWarsCompanion.Ava.Controls;

/// <summary>
/// The heading that opens a part of a list — Today's two, the vault's three tracks, buying and
/// selling: the carved heading on the left over a dry-brush stroke of the area's colour, its count
/// on the right.
///
/// A long heading - German makes them - would run into the count beside it, so the count drops
/// under the heading when the two do not fit on one line.
///
/// <see cref="Note"/> is the line the vault needs when what it lists is out of date, set off by a
/// tarnished edge; nothing is drawn for it when it is empty.
/// </summary>
public sealed class SectionBand : UserControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<SectionBand, string?>(nameof(Title));

    public static readonly StyledProperty<string?> SummaryProperty =
        AvaloniaProperty.Register<SectionBand, string?>(nameof(Summary));

    public static readonly StyledProperty<string?> NoteProperty =
        AvaloniaProperty.Register<SectionBand, string?>(nameof(Note));

    private readonly TextBlock _title = new()
    {
        Classes = { "heading" },
        FontSize = 17,
        HorizontalAlignment = HorizontalAlignment.Left,
        // Only ever needed on a screen too narrow for the heading on a line of its own.
        TextWrapping = TextWrapping.Wrap
    };

    private readonly TextBlock _summary = new() { Classes = { "caption" } };

    private readonly TextBlock _note = new() { Classes = { "caption" } };

    private readonly Border _noteEdge;

    public SectionBand()
    {
        _title.Bind(TextBlock.ForegroundProperty, _title.GetResourceObservable("VellumBrush"));

        // The stroke runs a little past the heading on both sides, as a brush would. Sized from
        // the heading: a stretched path left to itself takes all the width it is offered.
        var stroke = new Border
        {
            Height = 10,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(-8, 0, 0, -11)
        };
        _title.SizeChanged += (_, e) => stroke.Width = e.NewSize.Width + 34;
        stroke.Bind(Border.BackgroundProperty, stroke.GetResourceObservable("AreaTintBrush"));
        stroke.Bind(Visual.OpacityMaskProperty, stroke.GetResourceObservable("BrushStrokeMask"));

        var heading = new Panel { Children = { stroke, _title } };

        _noteEdge = new Border
        {
            BorderThickness = new Thickness(2, 0, 0, 0),
            Padding = new Thickness(12, 0, 0, 0),
            Margin = new Thickness(20, 16, 20, 0),
            IsVisible = false,
            Child = _note
        };
        _noteEdge.Bind(Border.BorderBrushProperty, _noteEdge.GetResourceObservable("BrassDimBrush"));

        Content = new StackPanel
        {
            Margin = new Thickness(0, 0, 0, 10),
            Children =
            {
                new HeadingRow { Margin = new Thickness(20, 22, 20, 0), Children = { heading, _summary } },
                _noteEdge
            }
        };
    }

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Summary
    {
        get => GetValue(SummaryProperty);
        set => SetValue(SummaryProperty, value);
    }

    public string? Note
    {
        get => GetValue(NoteProperty);
        set => SetValue(NoteProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TitleProperty) _title.Text = Title;
        else if (change.Property == SummaryProperty) _summary.Text = Summary;
        else if (change.Property == NoteProperty)
        {
            _note.Text = Note;
            _noteEdge.IsVisible = !string.IsNullOrEmpty(Note);
        }
    }

    /// <summary>
    /// The heading on the left and the count at the right of its foot, as long as they fit side
    /// by side; otherwise the count goes under the heading, clear of its stroke.
    /// </summary>
    private sealed class HeadingRow : Panel
    {
        /// <summary>The least room between the heading and the count beside it.</summary>
        private const double Gap = 16;

        /// <summary>From the heading to the count under it: the stroke hangs into this.</summary>
        private const double Below = 16;

        private bool _stacked;

        protected override Size MeasureOverride(Size availableSize)
        {
            Control heading = Children[0], summary = Children[1];

            heading.Measure(Size.Infinity);
            summary.Measure(Size.Infinity);

            var wide = heading.DesiredSize.Width + Gap + summary.DesiredSize.Width;
            _stacked = wide > availableSize.Width;

            if (!_stacked)
                return new Size(
                    double.IsInfinity(availableSize.Width) ? wide : availableSize.Width,
                    Math.Max(heading.DesiredSize.Height, summary.DesiredSize.Height));

            heading.Measure(availableSize.WithHeight(double.PositiveInfinity));
            summary.Measure(availableSize.WithHeight(double.PositiveInfinity));

            return new Size(
                Math.Max(heading.DesiredSize.Width, summary.DesiredSize.Width),
                heading.DesiredSize.Height + (string.IsNullOrEmpty((summary as TextBlock)?.Text)
                    ? 0
                    : Below + summary.DesiredSize.Height));
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            Control heading = Children[0], summary = Children[1];
            Size h = heading.DesiredSize, s = summary.DesiredSize;

            if (_stacked)
            {
                heading.Arrange(new Rect(0, 0, finalSize.Width, h.Height));
                summary.Arrange(new Rect(0, h.Height + Below, finalSize.Width, s.Height));
            }
            else
            {
                heading.Arrange(new Rect(0, finalSize.Height - h.Height, h.Width, h.Height));
                summary.Arrange(new Rect(finalSize.Width - s.Width, finalSize.Height - s.Height, s.Width, s.Height));
            }

            return finalSize;
        }
    }
}
