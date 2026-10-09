using GuildWarsCompanion.Host;

using Microsoft.Extensions.DependencyInjection;

namespace GuildWarsCompanion.Ava.Shell;

/// <summary>
/// Shell's navigation, rebuilt small: the same route strings, the same three moves.
///
/// <c>//area/page</c> switches to a top-level page and drops whatever was pushed on top of it.
/// <c>page?key=value</c> pushes a page and hands it the arguments. <c>..</c> goes back. Top-level
/// pages are kept once made, so switching back to one finds it as it was left — which is what
/// Shell does too, and what the singleton view models behind them expect.
/// </summary>
public sealed class Router(IServiceProvider services) : INavigator
{
    private readonly List<(string Name, PageView View)> _stack = [];
    private readonly Dictionary<string, PageView> _tops = [];

    /// <summary>Raised after every move, for the shell to redraw its bar and menu.</summary>
    public event Action? Changed;

    /// <summary>Set by the shell, which owns the overlay an alert is drawn in.</summary>
    public Func<string, string, string, Task>? ShowAlert { get; set; }

    public PageView? Current => _stack.Count == 0 ? null : _stack[^1].View;

    /// <summary>The name of the top-level page under everything pushed, for the menu's marker.</summary>
    public string? TopName => _stack.Count == 0 ? null : _stack[0].Name;

    public bool CanGoBack => _stack.Count > 1;

    public Task GoToAsync(string route)
    {
        var (path, query) = Parse(route);

        if (path == "..")
        {
            GoBack();
        }
        else if (path.StartsWith("//", StringComparison.Ordinal))
        {
            var parts = path[2..].Split('/', StringSplitOptions.RemoveEmptyEntries);
            var name = parts.Length > 1 ? parts[^1] : Routes.FirstPageOf(parts.FirstOrDefault() ?? "today");

            SwitchTo(name, query);
        }
        else
        {
            Push(path.Trim('/'), query);
        }

        return Task.CompletedTask;
    }

    public Task AlertAsync(string title, string message, string close) =>
        ShowAlert?.Invoke(title, message, close) ?? Task.CompletedTask;

    /// <summary>
    /// Drops every page and makes the top one again, for a shell that is being built anew; see
    /// <see cref="Shells"/>. Pages are controls, and a control belongs to one window: handing the
    /// old ones to a new activity is what crashed. What was pushed on top is not rebuilt — its
    /// arguments are gone with it — so the area comes back as its first page, the view models
    /// behind it unchanged, since they are the container's and not the page's.
    /// </summary>
    public void Rebuild()
    {
        var top = TopName;

        Current?.OnDisappearing();

        _stack.Clear();
        _tops.Clear();

        if (top is not null) SwitchTo(top, new Dictionary<string, object>(StringComparer.Ordinal));
    }

    /// <summary>Pops one page. False when there is nothing under the current one.</summary>
    public bool GoBack()
    {
        if (!CanGoBack) return false;

        var leaving = _stack[^1].View;
        _stack.RemoveAt(_stack.Count - 1);

        Move(leaving, _stack[^1].View);
        return true;
    }

    private void SwitchTo(string name, IDictionary<string, object> query)
    {
        var leaving = Current;

        if (!_tops.TryGetValue(name, out var view))
            _tops[name] = view = Create(name);

        _stack.Clear();
        _stack.Add((name, view));

        Apply(view, query);
        Move(leaving, view);
    }

    private void Push(string name, IDictionary<string, object> query)
    {
        var leaving = Current;
        var view = Create(name);

        _stack.Add((name, view));

        Apply(view, query);
        Move(leaving, view);
    }

    private PageView Create(string name)
    {
        var route = Routes.For(name);
        var view = route.View();

        // A page that binds its title keeps the binding; setting a value here would replace it.
        if (!view.IsSet(PageView.PageTitleProperty)) view.PageTitle = route.Title;

        if (route.ViewModel is { } type) view.DataContext = services.GetRequiredService(type);

        return view;
    }

    /// <summary>Arguments go in before the page appears, the order Shell uses.</summary>
    private static void Apply(PageView view, IDictionary<string, object> query)
    {
        if (query.Count > 0 && view.DataContext is IQueryTarget target) target.ApplyQuery(query);
    }

    private void Move(PageView? leaving, PageView arriving)
    {
        if (!ReferenceEquals(leaving, arriving)) leaving?.OnDisappearing();

        Changed?.Invoke();

        if (!ReferenceEquals(leaving, arriving)) arriving.OnAppearing();
    }

    /// <summary>
    /// Splits <c>item?id=5&amp;x=y</c>. Values stay escaped, as Shell hands them to
    /// IQueryAttributable; <see cref="RouteQuery"/> unescapes them on the way in.
    /// </summary>
    private static (string Path, IDictionary<string, object> Query) Parse(string route)
    {
        var mark = route.IndexOf('?');
        var query = new Dictionary<string, object>(StringComparer.Ordinal);

        if (mark < 0) return (route, query);

        foreach (var pair in route[(mark + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = pair.IndexOf('=');

            if (equals > 0) query[pair[..equals]] = pair[(equals + 1)..];
        }

        return (route[..mark], query);
    }
}
