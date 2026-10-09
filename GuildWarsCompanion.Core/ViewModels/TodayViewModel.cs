using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GuildWarsCompanion.Bridge.Protocol;
using GuildWarsCompanion.Gw2;
using GuildWarsCompanion.Host;
using GuildWarsCompanion.Services;

namespace GuildWarsCompanion.ViewModels;

public sealed partial class TodayViewModel(
    TodayService today, HereService here, GoalService goals, LedgerService ledger, ApiKeySession session,
    INavigator navigator, BridgeClient bridge, TestEvidence evidence, WidgetService widgets)
    : BaseViewModel
{
    /// <summary>The last session, or this one; null until the ledger has seen any play.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSession))]
    private SessionText? _session;

    public bool HasSession => Session is not null;
    /// <summary>"14h 30m", "42 min": how long until the daily reset, the page's headline.</summary>
    [ObservableProperty] private string? _resetIn;

    /// <summary>"Imothep · Ranger · in Wayfarer Foothills" while the PC says so; otherwise null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPlaying))]
    private string? _playing;

    public bool HasPlaying => Playing is not null;

    /// <summary>The map the character being played is on, whose loading screen heads the page.</summary>
    [ObservableProperty] private string? _playingMap;

    /// <summary>The character the playing line names, for the tap that opens them.</summary>
    private string? _playingName;

    /// <summary>
    /// Opens the character being played. The line already names them, so finding them in the
    /// roster by hand was a second look for something on screen.
    /// </summary>
    [RelayCommand]
    public Task OpenPlayingAsync()
    {
        if (_playingName is not { Length: > 0 } name) return Task.CompletedTask;

        return navigator.GoToAsync($"character?name={Uri.EscapeDataString(name)}");
    }

    /// <summary>"Here: map chest waiting · 2 of 13 hero points left"; see <see cref="HereLine"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHere))]
    private string? _here;

    public bool HasHere => Here is not null;

    public ObservableCollection<TodaySection> Sections { get; } = [];

    protected override string DefaultEmptyMessage => "Pull down to see what is left today.";

    [RelayCommand]
    public Task RefreshAsync() => RunAsync(async ct =>
    {
        Sections.Clear();

        // Asked beside the API rather than before it, so a PC that is off never delays the list.
        // Needs no key either: it is the PC's answer, not the account's.
        var playing = ReadGameAsync(ct);

        var loaded = await LoadSectionsAsync(ct);

        var report = await playing;
        _loaded = loaded;

        // After the trackers, whose fresh lists it reads; see HereService.
        await ApplyGameAsync(report, ct);

        if (loaded) Session = await ReadSessionAsync(ct);
    });

    /// <summary>Whether the last refresh read today's trackers, which the Here line builds on.</summary>
    private bool _loaded;

    /// <summary>
    /// Where the last look found the game, or null for no live game. The map's name is part of
    /// it: the bridge names a new map a moment after reporting its id, and "in Map 50" should not
    /// stand until the minute's tick.
    /// </summary>
    private (string? Character, int? MapId, string? MapName)? _seen;

    /// <summary>One look between refreshes at a time; the 15-second and the minute's can meet.</summary>
    private int _looking;

    /// <summary>
    /// A look at the game between full refreshes, while the page is open: the PC again, and the
    /// playing and Here lines when they would read differently. A new map or character always
    /// redoes them; <paramref name="clockChanged"/> — the minute's tick — redoes the Here line
    /// anyway, whose "in 8 min" goes stale on its own. Never an error on the page: a PC that does
    /// not answer leaves the lines as they were until it does, or until a full refresh says otherwise.
    /// </summary>
    public async Task LookAtGameAsync(bool clockChanged, CancellationToken ct = default)
    {
        // A full refresh reads the game itself, and the trackers the Here line needs.
        if (IsBusy || Interlocked.Exchange(ref _looking, 1) == 1) return;

        try
        {
            var report = await ReadGameAsync(ct);
            var seen = Where(report);

            if (seen == _seen && !clockChanged) return;

            await ApplyGameAsync(report, ct);
        }
        catch (Exception)
        {
            // Unreachable, mid-restart, rate limited: the next look is a few seconds away.
        }
        finally
        {
            Interlocked.Exchange(ref _looking, 0);
        }
    }

    private async Task ApplyGameAsync(GameReport? report, CancellationToken ct)
    {
        _seen = Where(report);

        Playing = PlayingLine.Describe(report);
        PlayingMap = Playing is null ? null : report?.MapName;
        _playingName = Playing is null ? null : report?.State?.CharacterName;

        Here = _loaded && report is not null ? await DescribeHereAsync(report, ct) : null;

        // The Here widget shows the same thing; it should not wait for its own poll to say so.
        await widgets.StoreHereAsync(report, Here, ct);
    }

    private static (string? Character, int? MapId, string? MapName)? Where(GameReport? report) =>
        report is { IsLive: true, State: { } state } ? (state.CharacterName, state.MapId, report.MapName) : null;

    /// <summary>
    /// Takes a ledger point — opening Today is a moment worth recording, between the background
    /// polls — and says the latest session. A key without the scopes has no recap and no section.
    /// </summary>
    private async Task<SessionText?> ReadSessionAsync(CancellationToken ct)
    {
        try
        {
            await ledger.RecordAsync(ct);

            return await ledger.LatestAsync(ct) is { } played ? await ledger.DescribeAsync(played, ct) : null;
        }
        catch (Gw2ApiException)
        {
            // Offline or rate limited: the last recap is still true, but it is not worth an error here.
            return await ledger.LatestAsync(ct) is { } kept ? await ledger.DescribeAsync(kept, ct) : null;
        }
    }

    private async Task<string?> DescribeHereAsync(GameReport report, CancellationToken ct)
    {
        try
        {
            return await here.DescribeAsync(report, ct);
        }
        catch (Gw2ApiException)
        {
            // A line that could not be worked out is no line, not an error over today's list.
            return null;
        }
    }

    /// <returns>Whether today's trackers were read, which is what <see cref="Here"/> builds on.</returns>
    private async Task<bool> LoadSectionsAsync(CancellationToken ct)
    {
        // First, and whatever the key: the countdown needs none. Set only after the key checks,
        // a page without a key showed no headline until the view's 30-second timer came round.
        UpdateReset();

        await session.EnsureRestoredAsync(ct);

        if (!session.IsAuthenticated)
        {
            Notice = "Add your API key under Settings to see what is left today.";
            return false;
        }

        if (!session.HasPermission(Gw2Permissions.Progression))
        {
            Notice = "This key has no 'progression' permission. Create a new key that includes it.";
            return false;
        }

        Notice = null;

        foreach (var section in await today.GetAsync(ct))
            Sections.Add(section);

        if (await GoalsAsync(ct) is { } pinned) Sections.Add(pinned);

        return true;
    }

    /// <summary>Which pin or unpin the goals on screen were read after.</summary>
    private int _goalsVersion = -1;

    /// <summary>
    /// Re-reads the goals alone when one was pinned or unpinned since they were read — which is
    /// always on another screen, so coming back to Today used to show the list as it was before.
    /// The trackers above are left as they are: they are not what changed.
    /// </summary>
    public Task RefreshGoalsIfChangedAsync() =>
        Sections.Count == 0 || goals.Version == _goalsVersion
            ? Task.CompletedTask
            : RunAsync(async ct =>
            {
                var fresh = await GoalsAsync(ct);

                if (Sections.FirstOrDefault(section => section.Name == "Goals") is { } old) Sections.Remove(old);
                if (fresh is not null) Sections.Add(fresh);
            });

    /// <summary>
    /// The pinned goals, after what resets: those expire tonight, a goal does not. Pinned and
    /// unpinned on an item's own page; tapping one goes back there.
    /// </summary>
    private async Task<TodaySection?> GoalsAsync(CancellationToken ct)
    {
        _goalsVersion = goals.Version;

        try
        {
            if (await goals.RowsAsync(ct) is not { } rows) return null;

            evidence.Saw("goal-pin");

            return new TodaySection("Goals", TodaySummary.Of(rows), rows.OrderBy(row => row.IsComplete));
        }
        catch (Gw2ApiException)
        {
            // Holdings unreadable all at once: the trackers above are still worth the screen.
            return null;
        }
    }

    /// <summary>
    /// The bridge's answer, on a short leash. Today opens with the app and is refreshed often, so
    /// no subnet sweep — see <see cref="BridgeFinder.ResolveAsync"/> — and five seconds before a
    /// silent PC counts as no answer at all.
    /// </summary>
    private async Task<GameReport?> ReadGameAsync(CancellationToken ct)
    {
        using var leash = CancellationTokenSource.CreateLinkedTokenSource(ct);
        leash.CancelAfter(TimeSpan.FromSeconds(5));

        try
        {
            return await bridge.GameAsync(leash.Token, sweep: false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <summary>
    /// Opens the screen behind a tracker — the vault for its objectives, the timetable for
    /// world bosses. Rows with nowhere to go are inert, and say so by showing no chevron.
    /// </summary>
    [RelayCommand]
    public Task OpenAsync(TodayRow? row) =>
        row is null || !row.CanOpen
            ? Task.CompletedTask
            : navigator.GoToAsync(row.Route!);

    /// <summary>
    /// How long the list stays true. Without it "3 unfinished" is missing the only piece of
    /// context that decides whether you can still do anything about it.
    /// </summary>
    /// <returns>Whether a reset has passed since the last call: the list is then yesterday's.</returns>
    public bool UpdateReset()
    {
        var now = DateTimeOffset.UtcNow;
        var next = TodayService.NextDailyReset(now);
        var remaining = next - now;

        ResetIn = remaining.TotalHours >= 1
            ? $"{(int)remaining.TotalHours}h {remaining.Minutes:00}m"
            : $"{remaining.Minutes} min";

        var passed = _nextReset is { } before && next > before;
        _nextReset = next;
        return passed;
    }

    /// <summary>The reset the countdown last counted down to.</summary>
    private DateTimeOffset? _nextReset;
}
