using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace GuildWarsCompanion.Ava.Controls;

/// <summary>
/// The heading that opens a part of a list — Today's two, the vault's three tracks, buying and
/// selling: the carved heading on the left over a dry-brush stroke of the area's colour, its count
/// on the right.
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

    private readonly TextBlock _title = new() { Classes = { "heading" }, FontSize = 17, HorizontalAlignment = HorizontalAlignment.Left };

    private readonly TextBlock _summary = new()
    {
        Classes = { "caption" },
        VerticalAlignment = VerticalAlignment.Bottom,
        Margin = new Thickness(0, 0, 20, 0)
    };

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

        var heading = new Panel
        {
            Margin = new Thickness(20, 22, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Children = { stroke, _title }
        };

        _noteEdge = new Border
        {
            BorderThickness = new Thickness(2, 0, 0, 0),
            Padding = new Thickness(12, 0, 0, 0),
            Margin = new Thickness(20, 16, 20, 0),
            IsVisible = false,
            Child = _note
        };
        _noteEdge.Bind(Border.BorderBrushProperty, _noteEdge.GetResourceObservable("BrassDimBrush"));

        Grid.SetColumn(_summary, 1);
        Grid.SetRow(_noteEdge, 1);
        Grid.SetColumnSpan(_noteEdge, 2);

        Content = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            Margin = new Thickness(0, 0, 0, 10),
            Children = { heading, _summary, _noteEdge }
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
}
