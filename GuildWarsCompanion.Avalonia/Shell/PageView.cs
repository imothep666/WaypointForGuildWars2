using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace GuildWarsCompanion.Ava.Shell;

/// <summary>
/// One screen, as the shell hosts it: a title for the bar, a refresh the bar can offer, and the
/// two moments MAUI calls OnAppearing and OnDisappearing — which is where every page in the MAUI
/// app does its loading, so the ports keep the same shape.
/// </summary>
public class PageView : UserControl
{
    public static readonly StyledProperty<string?> PageTitleProperty =
        AvaloniaProperty.Register<PageView, string?>(nameof(PageTitle));

    /// <summary>What the bar says. Bindable, for pages titled by what they show.</summary>
    public string? PageTitle
    {
        get => GetValue(PageTitleProperty);
        set => SetValue(PageTitleProperty, value);
    }

    public static readonly StyledProperty<bool> ShowsWashProperty =
        AvaloniaProperty.Register<PageView, bool>(nameof(ShowsWash));

    /// <summary>
    /// Whether the shell paints its area's wash behind the bar and the top of this page — for a
    /// page that leads with a masthead of its own and leaves the top of itself clear for it.
    /// </summary>
    public bool ShowsWash
    {
        get => GetValue(ShowsWashProperty);
        set => SetValue(ShowsWashProperty, value);
    }

    public static readonly StyledProperty<IImage?> ArtProperty =
        AvaloniaProperty.Register<PageView, IImage?>(nameof(Art));

    /// <summary>
    /// A painting for the top right of the masthead, over the wash: a character's specialization.
    /// Set whenever it arrives; the shell follows it.
    /// </summary>
    public IImage? Art
    {
        get => GetValue(ArtProperty);
        set => SetValue(ArtProperty, value);
    }

    public static readonly StyledProperty<bool> ShowsBandProperty =
        AvaloniaProperty.Register<PageView, bool>(nameof(ShowsBand));

    /// <summary>
    /// The area's wash behind the bar alone, over a rule of the area's tint: for a page that is
    /// all tool from the bar down - the compass's dial, the map - and has no masthead to give up.
    /// </summary>
    public bool ShowsBand
    {
        get => GetValue(ShowsBandProperty);
        set => SetValue(ShowsBandProperty, value);
    }

    public static readonly StyledProperty<IBrush?> WashTintProperty =
        AvaloniaProperty.Register<PageView, IBrush?>(nameof(WashTint));

    /// <summary>
    /// A colour of the page's own for its wash and its strokes, in place of the area's: a
    /// character's page in their profession's colour. Null keeps the area's.
    /// </summary>
    public IBrush? WashTint
    {
        get => GetValue(WashTintProperty);
        set => SetValue(WashTintProperty, value);
    }

    public static readonly StyledProperty<IImage?> SceneProperty =
        AvaloniaProperty.Register<PageView, IImage?>(nameof(Scene));

    /// <summary>
    /// A landscape for behind the whole masthead, under the wash and muted: the loading screen
    /// of the map someone is playing on.
    /// </summary>
    public IImage? Scene
    {
        get => GetValue(SceneProperty);
        set => SetValue(SceneProperty, value);
    }

    /// <summary>The map whose loading screen was last asked for, so a late answer can be dropped.</summary>
    private string? _sceneMap;

    /// <summary>
    /// A map's loading screen as this page's <see cref="Scene"/>, or none. The painting comes from
    /// the wiki, and a newer map asked for while it was on its way wins: the character may have
    /// moved on, or left the game. A map without a loading screen leaves no scene, so the area's
    /// own wash stays.
    /// </summary>
    protected async void ShowMapScene(string? map)
    {
        // The map's view model hears of a new map on the stream's thread, and a page's properties
        // belong to the UI thread: set from anywhere else, the scene never arrived.
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => ShowMapScene(map));
            return;
        }

        if (map == _sceneMap) return;
        _sceneMap = map;

        if (map is null)
        {
            Scene = null;
            return;
        }

        var scene = await Ui.WikiArt.ForMapAsync(map);

        if (_sceneMap == map) Scene = scene;
    }

    /// <summary>Whether the bar offers a refresh button, the desktop's answer to pulling down.</summary>
    public virtual bool CanRefresh => false;

    public virtual Task RefreshAsync() => Task.CompletedTask;

    public virtual void OnAppearing()
    {
    }

    public virtual void OnDisappearing()
    {
    }

    /// <summary>
    /// Asked before the shell goes back, for a page with layers of its own — the compass's sheet.
    /// True when the page has dealt with it.
    /// </summary>
    public virtual bool HandleBack() => false;

    /// <summary>For a page's RefreshContainer: the pull stays open until the work is done.</summary>
    protected async void OnPullRefresh(object? sender, RefreshRequestedEventArgs e)
    {
        var deferral = e.GetDeferral();

        try
        {
            await RefreshAsync();
        }
        finally
        {
            deferral.Complete();
        }
    }
}
