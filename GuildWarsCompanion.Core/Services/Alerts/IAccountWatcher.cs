namespace GuildWarsCompanion.Services.Alerts;

/// <summary>
/// Something worth telling the player about. <paramref name="Key"/> must be stable for the
/// same real-world event and different for a new one — it is what stops a reminder firing
/// on every poll.
/// </summary>
public sealed record AccountAlert(string Key, string Title, string Body)
{
    /// <summary>Copied from <see cref="IAccountWatcher.GameSaysItToo"/> by the service, so a
    /// watcher never has to set it on each alert.</summary>
    public bool GameSaysItToo { get; init; }

    /// <summary>The <see cref="IAccountWatcher.Name"/> that found it, set by the service.</summary>
    public string? Watcher { get; init; }

    /// <summary>Where a tap on it opens the app, as a route; null opens the app as it was.</summary>
    public string? Route { get; init; }

    /// <summary>The item it is about, when there is one the player may go to the trading post for:
    /// the notification then offers to send its chat link to the PC.</summary>
    public int? TradingPostItem { get; init; }

    /// <summary>That item's name, for saying where its link went.</summary>
    public string? TradingPostName { get; init; }
}

/// <summary>
/// One thing worth watching between app launches. Guild Wars 2 has no push API, so every
/// watcher works the same way: poll, describe what it found, and let
/// <see cref="AlertService"/> decide what is actually new.
/// </summary>
public interface IAccountWatcher
{
    /// <summary>Stable name; used to record that this watcher has taken its baseline.</summary>
    string Name { get; }

    /// <summary>Scopes the API key must carry. Without them the watcher is skipped.</summary>
    IReadOnlyList<string> RequiredPermissions { get; }

    /// <summary>
    /// Whether the first run should be swallowed as a baseline.
    ///
    /// True for watchers that report everything currently true — 90 days of trade history
    /// would otherwise arrive as 90 days of notifications. False for a watcher that already
    /// reports only transitions, where swallowing the first run would eat the very first
    /// alert the user asked for.
    /// </summary>
    bool NeedsBaseline => true;

    /// <summary>
    /// Whether the game tells a logged-in player the same thing itself — a sale message, the
    /// vault's own progress. Such alerts are held back while the bridge says a character is in
    /// the game, because a phone repeating what the screen in front of the player just said is
    /// noise. False for anything the game does not announce: world bosses, price watches.
    /// </summary>
    bool GameSaysItToo => false;

    /// <summary>
    /// Everything currently true and worth reporting — not "what changed". Reporting the
    /// full current state keeps watchers stateless; the store handles the diff.
    /// </summary>
    Task<IReadOnlyList<AccountAlert>> CheckAsync(CancellationToken ct);
}
