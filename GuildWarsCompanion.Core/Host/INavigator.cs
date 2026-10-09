namespace GuildWarsCompanion.Host;

/// <summary>
/// Moving between screens, and the one modal the app uses.
///
/// Routes are the app's own addresses, in the shape MAUI Shell reads: <c>item?id=19721</c> pushes
/// a page, <c>//account/wallet</c> switches to a tab, <c>..</c> goes back. Shell takes them as
/// they are; another front end maps the same strings onto its own pages, which keeps the view
/// models from knowing which one they run under.
/// </summary>
public interface INavigator
{
    Task GoToAsync(string route);

    /// <summary>A message with a single button that only dismisses it.</summary>
    Task AlertAsync(string title, string message, string close);
}

/// <summary>
/// A view model that is opened with arguments, taken from the query part of its route.
///
/// The values arrive as the front end parsed them out of the route, still URL-escaped. The page
/// hosting the view model passes them on; the view model decides what they mean.
/// </summary>
public interface IQueryTarget
{
    void ApplyQuery(IDictionary<string, object> query);
}

/// <summary>Reads one argument out of a route's query, unescaped.</summary>
public static class RouteQuery
{
    /// <summary>The value under <paramref name="key"/>, or null when the route did not carry it.</summary>
    public static string? Text(IDictionary<string, object> query, string key) =>
        query.TryGetValue(key, out var value) && value?.ToString() is { } text
            ? Uri.UnescapeDataString(text)
            : null;

    /// <summary>The value under <paramref name="key"/> as a number, or null when it is missing or
    /// not one.</summary>
    public static int? Int(IDictionary<string, object> query, string key) =>
        int.TryParse(Text(query, key), out var number) ? number : null;
}
