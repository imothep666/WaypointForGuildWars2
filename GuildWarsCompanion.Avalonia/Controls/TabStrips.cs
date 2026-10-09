using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;

using GuildWarsCompanion.Ava.Ui;
using GuildWarsCompanion.Host;
using GuildWarsCompanion.ViewModels;

using Microsoft.Extensions.DependencyInjection;

namespace GuildWarsCompanion.Ava.Controls;

/// <summary>
/// The strip of headings both kinds of tab share: carved capitals in the tab's tint over a stroke
/// of the area's colour for the live one.
/// </summary>
internal static class StripLook
{
    public static Control Heading(string labelPath, string tintPath, string selectedPath, Action<object?> tapped)
    {
        var label = new TextBlock
        {
            Classes = { "heading" },
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        label.Bind(TextBlock.TextProperty, new Binding(labelPath));
        label.Bind(TextBlock.ForegroundProperty, new Binding(tintPath) { Converter = Paint.Brush });

        var rule = new Border
        {
            Height = 8,
            Margin = new Thickness(-6, 0)
        };
        rule.Bind(Border.BackgroundProperty, rule.GetResourceObservable("AreaTintBrush"));
        rule.Bind(Visual.OpacityMaskProperty, rule.GetResourceObservable("BrushStrokeMask"));
        rule.Bind(Visual.IsVisibleProperty, new Binding(selectedPath));

        var button = new Button
        {
            Classes = { "row" },
            MinWidth = 64,
            Content = new StackPanel { Spacing = 5, Children = { label, rule } }
        };
        button.Click += (sender, _) => tapped((sender as Control)?.DataContext);

        return button;
    }

    public static ItemsControl Row(IDataTemplate template) => new()
    {
        ItemsPanel = new FuncTemplate<Panel?>(() => new StackPanel { Orientation = Orientation.Horizontal, Spacing = 26 }),
        ItemTemplate = template
    };

    /// <summary>
    /// The row's sideways scroll. A finger drags it; a mouse has only the wheel, which a
    /// sideways-only scroller ignores, so a tab past the edge was out of reach on a desktop.
    /// </summary>
    public static ScrollViewer Scroller(ItemsControl items, Thickness padding = default)
    {
        var scroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Padding = padding,
            Content = items
        };

        // Tunnelling, so the page's own scroll does not take the wheel first. A row that fits
        // lets it through to the page.
        scroller.AddHandler(InputElement.PointerWheelChangedEvent, (_, e) =>
        {
            if (e.Delta.Y == 0 || scroller.Extent.Width <= scroller.Viewport.Width) return;

            scroller.Offset = scroller.Offset.WithX(scroller.Offset.X - e.Delta.Y * 50);
            e.Handled = true;
        }, RoutingStrategies.Tunnel);

        return scroller;
    }
}

/// <summary>
/// A page's own headings — Settings' four or five, a character's five, the compass's two. The page owns
/// the tabs (a <see cref="PageTab"/> list on its view model) and decides what a tap does.
/// </summary>
public sealed class PageTabStrip : UserControl
{
    public static readonly StyledProperty<IEnumerable<PageTab>?> TabsProperty =
        AvaloniaProperty.Register<PageTabStrip, IEnumerable<PageTab>?>(nameof(Tabs));

    private readonly ItemsControl _items;

    public PageTabStrip()
    {
        _items = StripLook.Row(new FuncDataTemplate<PageTab>((_, _) =>
            StripLook.Heading(nameof(PageTab.Label), nameof(PageTab.Tint), nameof(PageTab.IsSelected),
                context => { if (context is PageTab tab) Selected?.Invoke(tab); })));

        Content = StripLook.Scroller(_items);
    }

    public IEnumerable<PageTab>? Tabs
    {
        get => GetValue(TabsProperty);
        set => SetValue(TabsProperty, value);
    }

    public event Action<PageTab>? Selected;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TabsProperty) _items.ItemsSource = Tabs;
    }
}

/// <summary>
/// The strip of sibling views inside an area — Account's five, the Trading Post's four.
///
/// A page states which area it belongs to and which view it is; the strip works out the rest from
/// <see cref="Areas"/>, so the pages of an area cannot disagree about what it contains. Moving
/// between them is a switch rather than a push, as it is under Shell.
/// </summary>
public sealed class AreaTabStrip : UserControl
{
    public static readonly StyledProperty<string?> AreaProperty =
        AvaloniaProperty.Register<AreaTabStrip, string?>(nameof(Area));

    public static readonly StyledProperty<string?> CurrentProperty =
        AvaloniaProperty.Register<AreaTabStrip, string?>(nameof(Current));

    private readonly ItemsControl _items;

    public AreaTabStrip()
    {
        _items = StripLook.Row(new FuncDataTemplate<AreaTab>((_, _) =>
            StripLook.Heading(nameof(AreaTab.Label), nameof(AreaTab.Tint), nameof(AreaTab.IsSelected), OnTapped)));

        Content = StripLook.Scroller(_items, new Thickness(20, 12, 20, 0));
    }

    public string? Area
    {
        get => GetValue(AreaProperty);
        set => SetValue(AreaProperty, value);
    }

    /// <summary>The name of the page this strip sits on, so it can mark itself.</summary>
    public string? Current
    {
        get => GetValue(CurrentProperty);
        set => SetValue(CurrentProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property != AreaProperty && change.Property != CurrentProperty) return;

        var tabs = Areas.For(Area);

        foreach (var tab in tabs) tab.IsSelected = tab.Route.EndsWith($"/{Current}", StringComparison.Ordinal);

        _items.ItemsSource = tabs;
    }

    private static async void OnTapped(object? context)
    {
        if (context is not AreaTab tab || tab.IsSelected) return;

        await App.Services.GetRequiredService<INavigator>().GoToAsync(tab.Route);
    }
}
