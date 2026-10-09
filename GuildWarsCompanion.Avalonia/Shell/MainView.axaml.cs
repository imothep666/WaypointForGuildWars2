using System.Collections.ObjectModel;

using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Platform;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;

using GuildWarsCompanion.Host;
using GuildWarsCompanion.Services;
using GuildWarsCompanion.ViewModels;

using Microsoft.Extensions.DependencyInjection;

namespace GuildWarsCompanion.Ava.Shell;

/// <summary>
/// One entry in the bar, the rail or the More sheet, and whether it is the area on screen. The
/// live one draws its glyph in the area's tint and its name in vellum; the rest are quiet.
/// </summary>
public sealed partial class AreaItem(Area? area, string label, string glyph, string tint) : ObservableObject
{
    private static readonly IBrush Quiet = new SolidColorBrush(Color.Parse("#7E8B9B"));
    private static readonly IBrush Lit = new SolidColorBrush(Color.Parse("#E6DECB"));

    public AreaItem(Area area) : this(area, area.BarLabel, area.Glyph, area.Tint)
    {
    }

    /// <summary>The area it opens; null for More, which opens the sheet.</summary>
    public Area? Area { get; } = area;

    public string Title { get; } = area?.Title ?? label;

    public string Label { get; } = label;

    public Geometry Geometry { get; } = Geometry.Parse(glyph);

    public IBrush Tint { get; } = new SolidColorBrush(Color.Parse(tint));

    public IBrush GlyphBrush => IsSelected ? Tint : Quiet;

    public IBrush LabelBrush => IsSelected ? Lit : Quiet;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GlyphBrush), nameof(LabelBrush))]
    private bool _isSelected;

    /// <summary>Something here wants the player: a vault reward waiting to be claimed.</summary>
    [ObservableProperty] private bool _hasDot;

    /// <summary>One line of news under the name in the More sheet: the boss that is up next.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNews))]
    private string? _news;

    public bool HasNews => News is not null;
}

public partial class MainView : UserControl
{
    /// <summary>Wide enough for the rail, which then replaces the bar. The desktop window's title
    /// bar reads it too: the rail's masthead names the app, so the bar need not.</summary>
    internal const double RailWidth = 720;

    private readonly Router _router;

    // The rail and the bar each have their own items: a control's items cannot be shared, and
    // the two are never on screen together anyway.
    private readonly List<AreaItem> _rail = Routes.Areas.Select(area => new AreaItem(area)).ToList();
    private readonly List<AreaItem> _fixed = Routes.Areas.Where(area => area.InBar).Select(area => new AreaItem(area)).ToList();

    // Everything not always in the bar: one of these holds the fourth place, the rest are under
    // More. Data rather than controls, so an item moves between the two freely.
    private readonly List<AreaItem> _loose = Routes.Areas.Where(area => !area.InBar).Select(area => new AreaItem(area)).ToList();
    private readonly ObservableCollection<AreaItem> _bar = [];
    private readonly ObservableCollection<AreaItem> _more = [];
    private readonly AreaItem _moreItem = new(null, "More", Glyphs.More, "#C8A34E");
    private readonly ISettings? _settings = HostServices.Current?.GetService<ISettings>();
    private AreaItem? _fourth;

    /// <summary>Where the bar's fourth place is remembered between runs.</summary>
    private const string FourthKey = "nav.fourth";
    private readonly VaultSignal? _vault = HostServices.Current?.GetService<VaultSignal>();
    private TaskCompletionSource? _alert;
    private IDisposable? _title;
    private IDisposable? _art;
    private PageView? _watched;
    private BaseViewModel? _watchedModel;
    private CancellationTokenSource? _sweeping;

    /// <summary>For the XAML loader and the designer only.</summary>
    public MainView() : this(new Router(new EmptyServices()))
    {
    }

    public MainView(Router router)
    {
        InitializeComponent();

        _router = router;
        _router.Changed += OnRouteChanged;
        _router.ShowAlert = ShowAlertAsync;

        Arrange(_settings?.Get(FourthKey, Routes.DefaultFourth) ?? Routes.DefaultFourth);

        RailList.ItemsSource = _rail;
        BarList.ItemsSource = _bar;
        MoreList.ItemsSource = _more;

        if (_vault is not null)
        {
            _vault.Changed += OnVaultChanged;
            OnVaultChanged();
        }
    }

    /// <summary>The dot on the vault's entry, wherever it is listed.</summary>
    private void OnVaultChanged() => Dispatcher.UIThread.Post(() =>
    {
        var waiting = _vault?.Waiting == true;

        foreach (var item in _rail.Concat(_bar).Where(item => item.Area?.Route == "//vault")) item.HasDot = waiting;
    });

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);

        var wide = e.NewSize.Width >= RailWidth;

        Split.IsPaneOpen = wide;
        BottomBar.IsVisible = !wide;
        if (wide) MoreLayer.IsVisible = false;
    }

    /// <summary>Stops following the router, for a shell whose window has been replaced; see
    /// <see cref="Shells"/>. The new shell sets the alert hook for itself.</summary>
    public void Release()
    {
        _router.Changed -= OnRouteChanged;

        if (_vault is not null) _vault.Changed -= OnVaultChanged;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // Android's back gesture and button, and the browser-style back key on a desktop mouse.
        if (TopLevel.GetTopLevel(this) is { } top) top.BackRequested += OnBackRequested;

        if (_router.Current is null) _ = _router.GoToAsync("//today");
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is { } top) top.BackRequested -= OnBackRequested;

        base.OnDetachedFromVisualTree(e);
    }

    private void OnRouteChanged()
    {
        var page = _router.Current;

        PageHost.Content = page;

        _title?.Dispose();
        _title = page is null ? null : TitleText.Bind(TextBlock.TextProperty, page.GetObservable(PageView.PageTitleProperty));

        _art?.Dispose();
        _art = page is null ? null : ArtImage.Bind(Image.SourceProperty, page.GetObservable(PageView.ArtProperty));

        Watch(page);

        BackButton.IsVisible = _router.CanGoBack;
        TitleText.Margin = new Thickness(_router.CanGoBack ? 4 : 20, 0, 0, 0);
        RefreshButton.IsVisible = page?.CanRefresh == true;

        var area = _router.TopName is { } top ? Routes.AreaOf(top) : null;

        // Opening an area that is neither always in the bar nor a utility gives it the fourth place.
        if (area is { InBar: false, Utility: false } && !ReferenceEquals(_fourth?.Area, area))
        {
            Arrange(area.Route);
            _settings?.Set(FourthKey, area.Route);
        }

        foreach (var item in _rail.Concat(_fixed).Concat(_loose)) item.IsSelected = item.Area is not null && ReferenceEquals(item.Area, area);

        // More stands lit for whatever lives under it: only Settings and the test plan, now.
        _moreItem.IsSelected = area is not null && _more.Any(item => ReferenceEquals(item.Area, area));

        _area = area;

        Paint(page);
        ShowScene(page);

        MoreLayer.IsVisible = false;
    }

    private void OnBackClicked(object? sender, RoutedEventArgs e) => _router.GoBack();

    /// <summary>
    /// The bar: the three that are always there, the area holding the fourth place, and More.
    /// The one it displaces goes back under More in its usual order, so nothing is ever lost.
    /// </summary>
    private void Arrange(string fourthRoute)
    {
        _fourth = _loose.FirstOrDefault(item => item.Area is { Utility: false } area && area.Route == fourthRoute)
                  ?? _loose.First(item => item.Area?.Route == Routes.DefaultFourth);

        _bar.Clear();
        foreach (var item in _fixed) _bar.Add(item);
        _bar.Add(_fourth);
        _bar.Add(_moreItem);

        _more.Clear();
        foreach (var item in _loose.Where(item => !ReferenceEquals(item, _fourth))) _more.Add(item);
    }

    private Area? _area;

    /// <summary>
    /// The page's colours: the area's tint for whatever in the page asks for it (the stroke under
    /// a live tab or a section's heading), and the area's wash behind the masthead. A page with a
    /// tint of its own has both in that colour instead. Again whenever the page changes its tint.
    /// </summary>
    private void Paint(PageView? page)
    {
        var own = page?.WashTint;

        Resources["AreaTintBrush"] = own ??
            (_area is null ? Application.Current!.FindResource("BrassBrush") : new SolidColorBrush(Color.Parse(_area.Tint)));

        var washes = page is { ShowsWash: true };
        var wash = washes && own is null && _area?.Wash is { } name ? Wash(name) : null;

        WashImage.Source = wash;
        WashImage.IsVisible = wash is not null;

        TintedWash.Background = own;
        TintedWash.IsVisible = washes && own is not null;

        var band = page is { ShowsBand: true } && _area?.Wash is { } banded ? Wash(banded) : null;

        BandImage.Source = band;
        BandImage.IsVisible = band is not null;
        BandRule.IsVisible = band is not null;

        BarRule.IsVisible = band is null && (!washes || (wash is null && own is null));
    }

    private void OnPagePropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (!ReferenceEquals(sender, _router.Current)) return;

        if (e.Property == PageView.WashTintProperty) Paint(_router.Current);
        if (e.Property == PageView.SceneProperty) ShowScene(_router.Current);
    }

    /// <summary>How strongly a landscape shows: nearly whole, with SceneShade under the headline.</summary>
    private const double SceneStrength = 0.9;

    /// <summary>Which of the two landscape layers is the one showing, in the masthead and in the band.</summary>
    private bool _sceneAInFront, _bandSceneAInFront;

    /// <summary>
    /// What each pair is showing, kept rather than read back off the layers: mid-fade, a layer's
    /// opacity is wherever the fade has got to. Leaving the compass for the map — the same map,
    /// the same painting — read a band still fading out as one still showing it, and left it blank.
    /// </summary>
    private IImage? _sceneShown, _bandSceneShown;

    /// <summary>
    /// A page's landscape in place of the wash, or the wash back: in the masthead for a page that
    /// has one, in the band for a band page (the map, the compass). Only opacities change, and
    /// each layer fades them (see the Transitions in MainView.axaml).
    /// </summary>
    private void ShowScene(PageView? page)
    {
        var scene = page?.Scene;
        var inBand = page is { ShowsBand: true };

        CrossFade(SceneA, SceneB, ref _sceneAInFront, ref _sceneShown, inBand ? null : scene, SceneStrength);
        CrossFade(BandSceneA, BandSceneB, ref _bandSceneAInFront, ref _bandSceneShown, inBand ? scene : null, SceneStrength);

        var masthead = scene is not null && !inBand;
        var band = scene is not null && inBand;

        WashImage.Opacity = masthead ? 0 : 1;
        TintedWash.Opacity = masthead ? 0 : 1;
        ArtImage.Opacity = masthead ? 0 : 0.8;
        SceneShade.Opacity = masthead ? 1 : 0;

        BandImage.Opacity = band ? 0 : 1;
        BandShade.Opacity = band ? 1 : 0;
    }

    /// <summary>
    /// A new landscape goes on the layer that is not showing and fades in while the other fades
    /// out, so one map gives way to the next the same way the wash gives way to the first.
    /// </summary>
    private static void CrossFade(Image a, Image b, ref bool aInFront, ref IImage? shown, IImage? scene, double strength)
    {
        if (ReferenceEquals(shown, scene)) return;
        shown = scene;

        var front = aInFront ? a : b;
        var back = aInFront ? b : a;

        if (scene is null)
        {
            front.Opacity = 0;
            back.Opacity = 0;
            return;
        }

        back.Source = scene;
        back.Opacity = strength;
        front.Opacity = 0;
        aInFront = !aInFront;
    }

    /// <summary>
    /// Follows whether the page on screen is fetching, for the line under the bar. By hand rather
    /// than a binding on a path: the model arrives as the page's DataContext, sometimes after the
    /// page itself, and a reflection binding is what the trimmer cannot see.
    /// </summary>
    private void Watch(PageView? page)
    {
        if (_watched is not null)
        {
            _watched.DataContextChanged -= OnPageModelChanged;
            _watched.PropertyChanged -= OnPagePropertyChanged;
        }

        _watched = page;

        if (page is not null)
        {
            page.DataContextChanged += OnPageModelChanged;
            page.PropertyChanged += OnPagePropertyChanged;
        }

        OnPageModelChanged(null, EventArgs.Empty);
    }

    private void OnPageModelChanged(object? sender, EventArgs e)
    {
        if (_watchedModel is not null) _watchedModel.PropertyChanged -= OnPageModelPropertyChanged;

        _watchedModel = _watched?.DataContext as BaseViewModel;

        if (_watchedModel is not null) _watchedModel.PropertyChanged += OnPageModelPropertyChanged;

        ShowBusy();
    }

    private void OnPageModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BaseViewModel.IsBusy)) Dispatcher.UIThread.Post(ShowBusy);
    }

    private void ShowBusy()
    {
        var busy = _watchedModel?.IsBusy == true;

        if (busy == BusyTrack.IsVisible) return;

        BusyTrack.IsVisible = busy;

        _sweeping?.Cancel();
        _sweeping = null;

        if (busy) Sweep(_sweeping = new CancellationTokenSource());
    }

    /// <summary>
    /// The stroke crosses from just off the left edge to just off the right, again and again,
    /// easing in and out as a brush would. Measured each time it starts, so a desktop window of
    /// any width gets a stroke in proportion.
    /// </summary>
    private async void Sweep(CancellationTokenSource sweeping)
    {
        // The track has no width until it has been laid out once, having just been shown.
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);

        var width = Math.Max(BusyTrack.Bounds.Width, 1);
        var stroke = width * 0.35;

        BusySweep.Width = stroke;

        var glide = new Animation
        {
            Duration = TimeSpan.FromSeconds(1.3),
            IterationCount = IterationCount.Infinite,
            Easing = new SineEaseInOut(),
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(TranslateTransform.XProperty, -stroke) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(TranslateTransform.XProperty, width) } }
            }
        };

        try
        {
            // On the control, whose RenderTransform the animator finds for itself; handed the
            // transform directly, it threw an InvalidCastException that took the app down.
            await glide.RunAsync(BusySweep, sweeping.Token);
        }
        catch (OperationCanceledException)
        {
            // Done fetching.
        }
        catch (Exception)
        {
            // A line that does not move is no reason to lose the app.
        }
    }

    private static readonly Dictionary<string, Bitmap> Washes = [];

    /// <summary>A wash, decoded once and kept: six small images, shown again and again.</summary>
    private static Bitmap Wash(string name)
    {
        if (Washes.TryGetValue(name, out var bitmap)) return bitmap;

        using var stream = AssetLoader.Open(new Uri($"avares://GuildWarsCompanion.Avalonia/Assets/Images/wash_{name}.png"));

        return Washes[name] = new Bitmap(stream);
    }

    private async void OnRefreshClicked(object? sender, RoutedEventArgs e)
    {
        if (_router.Current is { CanRefresh: true } page) await page.RefreshAsync();
    }

    private void OnAreaClicked(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is AreaItem { Area: { } area }) Open(area);
    }

    /// <summary>A bar entry: its area, or for More the sheet, which a second tap puts away.</summary>
    private void OnBarClicked(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not AreaItem item) return;

        if (item.Area is { } area)
            Open(area);
        else
        {
            if (!MoreLayer.IsVisible) Tell();

            MoreLayer.IsVisible = !MoreLayer.IsVisible;
        }
    }

    /// <summary>
    /// The news the sheet carries, worked out as it opens: the next world boss, which needs only
    /// the clock, and orders the market has moved past, as far as the trading post has been read
    /// this run. Nothing is fetched for it.
    /// </summary>
    private void Tell()
    {
        foreach (var item in _more)
        {
            item.News = item.Area?.Route switch
            {
                "//events" => NextBoss(DateTimeOffset.UtcNow),
                "//tradingpost/orders" => Beaten(),
                _ => null
            };
        }
    }

    private static string? NextBoss(DateTimeOffset now)
    {
        if (EventSchedule.NextAppearances(now) is not [var next, ..]) return null;

        var wait = next.Next - now;

        return wait < TimeSpan.FromHours(1)
            ? $"{next.Name} in {Math.Max(1, (int)Math.Ceiling(wait.TotalMinutes))} min"
            : $"{next.Name} at {next.Next.ToLocalTime():HH:mm}";
    }

    private static string? Beaten()
    {
        if (HostServices.Current?.GetService<OrdersViewModel>() is not { } orders) return null;

        var parts = orders.Sections
            .Select(section => (section.Name, Count: section.Count(row => row.IsBeaten)))
            .Where(part => part.Count > 0)
            .Select(part => part.Name == "Selling" ? $"{part.Count} undercut" : $"{part.Count} outbid")
            .ToList();

        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    private void OnMoreBackdropPressed(object? sender, PointerPressedEventArgs e) => MoreLayer.IsVisible = false;

    /// <summary>
    /// Opens an area at its first page. Tapping the area already on screen goes back to that
    /// page from wherever inside it the player had gone, as a bottom bar is expected to.
    /// </summary>
    private void Open(Area area)
    {
        MoreLayer.IsVisible = false;

        _ = _router.GoToAsync(area.Route);
    }

    private void OnBackRequested(object? sender, RoutedEventArgs e)
    {
        if (AlertLayer.IsVisible)
            CloseAlert();
        else if (MoreLayer.IsVisible)
            MoreLayer.IsVisible = false;
        else if (_router.Current?.HandleBack() != true && !_router.GoBack())
            return;

        e.Handled = true;
    }

    private Task ShowAlertAsync(string title, string message, string close)
    {
        CloseAlert();

        AlertTitle.Text = title;
        AlertMessage.Text = message;
        AlertClose.Content = close;
        AlertLayer.IsVisible = true;

        _alert = new TaskCompletionSource();
        return _alert.Task;
    }

    private void OnAlertClosed(object? sender, RoutedEventArgs e) => CloseAlert();

    private void OnAlertBackdropPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ReferenceEquals(e.Source, AlertLayer)) CloseAlert();
    }

    private void CloseAlert()
    {
        AlertLayer.IsVisible = false;
        _alert?.TrySetResult();
        _alert = null;
    }

    private sealed class EmptyServices : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
