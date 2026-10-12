using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GuildWarsCompanion.Bridge.Protocol;
using GuildWarsCompanion.Data;
using GuildWarsCompanion.Gw2;
using GuildWarsCompanion.Host;
using GuildWarsCompanion.Services.Alerts;
using GuildWarsCompanion.Services;

namespace GuildWarsCompanion.ViewModels;

/// <summary>
/// One permission the app asks for, as the settings screen lists it.
/// </summary>
/// <param name="Status">The scope and whether /v2/tokeninfo says the key has it.</param>
public sealed record ScopeRow(ScopeStatus Status)
{
    /// <summary>Spelled exactly as the key form on account.arena.net spells it, so the two can
    /// be read side by side.</summary>
    public string Permission => Status.Scope.Permission;

    public string Powers => Status.Scope.Powers;

    /// <summary>Two plain booleans and two labels rather than one label and a converter. A
    /// Converter in XAML forces a reflection-based binding, which the trimmer cannot see
    /// through — the same reason every other either-or on this screen is a bool.</summary>
    public bool IsGranted => Status.Granted;

    public bool IsMissing => !Status.Granted;

    public double Fade => Status.Granted ? 0.6 : 1;
}

/// <summary>One downloaded marker pack, as the settings screen lists it.</summary>
public sealed record MarkerPackRow(string Id, string Name, string Detail);

public sealed partial class SettingsViewModel(
    ApiKeySession session,
    Gw2Client client,
    AlertStore alerts,
    ActivityStore activity,
    LedgerStore ledger,
    SnapshotStore snapshots,
    AlertService alertService,
    ItemCatalogue catalogue,
    MarkerStore markers,
    INotifier notifier,
    NotifySchedule schedule,
    IWatchScheduler scheduler,
    BridgeClient bridge,
    Bridge.Protocol.Journal journal,
    INavigator navigator,
    ISystemActions system,
    IWindowMode windowMode,
    IAppUpdates updates,
    AppEnvironment environment) : BaseViewModel
{
    /// <summary>Guards the switch against reacting to its own corrections.</summary>
    private bool _applyingReminderState;

    /// <summary>The catalogue fill's own lifetime, independent of the screen's.</summary>
    private CancellationTokenSource? _fill;

    /// <summary>The pack download's own lifetime, for the same reason.</summary>
    private CancellationTokenSource? _packing;

    [ObservableProperty] private string _markerUrl = string.Empty;

    /// <summary>The packs on the device, one row each.</summary>
    public ObservableCollection<MarkerPackRow> MarkerPacks { get; } = [];

    public bool HasMarkerPacks => MarkerPacks.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMarkerNote))]
    private string _markerNote = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotDownloadingMarkers))]
    private bool _isDownloadingMarkers;

    public bool IsNotDownloadingMarkers => !IsDownloadingMarkers;

    public bool HasMarkerNote => !string.IsNullOrWhiteSpace(MarkerNote);

    /// <summary>
    /// Fetches the pack at the address the player gave, and keeps the chests and jumping puzzles
    /// out of it.
    ///
    /// The address is theirs to choose and nothing is shipped: a pack is somebody else's work
    /// published on their own terms, so the app fetches it rather than carrying a copy. See
    /// <see cref="MarkerStore"/>.
    /// </summary>
    [RelayCommand]
    public Task BrowsePacksAsync() => navigator.GoToAsync("markerpacks");

    [RelayCommand]
    public Task DownloadMarkersAsync() => RunMarkersAsync(
        L.T("Downloading…"), token => markers.AddAsync(MarkerUrl, token));

    /// <summary>
    /// Asks a pack's server whether anything changed, and downloads only if it has.
    ///
    /// Free when nothing moved — see <see cref="MarkerStore.UpdateAsync"/>.
    /// </summary>
    public Task UpdateMarkerPackAsync(MarkerPackRow row) => RunMarkersAsync(
        L.T("Checking…"), token => markers.UpdateAsync(row.Id, token));

    /// <summary>
    /// Asks every pack's server whether anything changed.
    ///
    /// Cheap enough to offer as one button now that an unchanged pack answers 304 with no body.
    /// </summary>
    [RelayCommand]
    public Task UpdateAllMarkersAsync() => RunMarkersAsync(
        L.T("Checking…"), token => markers.UpdateAllAsync(token));

    /// <summary>Puts one pack down, leaving the others alone.</summary>
    public void RemoveMarkerPack(MarkerPackRow row)
    {
        markers.Remove(row.Id);

        MarkerNote = L.F("{0} removed.", row.Name);

        ShowMarkerPacks();
    }

    /// <summary>Stops a download that is taking longer than the player wants to wait.</summary>
    [RelayCommand]
    public void StopMarkers() => _packing?.Cancel();

    /// <summary>One shape for both fetches: the screen is busy, something happens, the screen
    /// says what.</summary>
    private async Task RunMarkersAsync(string doing, Func<CancellationToken, Task<MarkerImport>> work)
    {
        if (IsDownloadingMarkers) return;

        IsDownloadingMarkers = true;
        MarkerNote = doing;

        _packing?.Cancel();
        _packing?.Dispose();
        _packing = new CancellationTokenSource();

        try
        {
            MarkerNote = (await work(_packing.Token)).Note;
        }
        finally
        {
            IsDownloadingMarkers = false;

            ShowMarkerPacks();
        }
    }

    /// <summary>Pours the store's list into the bound one.</summary>
    private void ShowMarkerPacks()
    {
        MarkerPacks.Clear();

        foreach (var pack in markers.Packs)
            MarkerPacks.Add(new MarkerPackRow(pack.Id, pack.Name, Describe(pack)));

        OnPropertyChanged(nameof(HasMarkerPacks));
    }

    /// <summary>
    /// What a pack gave, in one line.
    ///
    /// Both halves are named, and only the halves that exist. A pack of routes read "0 markers ·
    /// 0 maps" while carrying 1334 routes, which reads as a failed import of exactly the thing
    /// it succeeded at.
    /// </summary>
    private static string Describe(MarkerPackRecord pack)
    {
        var said = new List<string>();

        if (pack.Count > 0) said.Add(L.F("{0} markers · {1} maps", pack.Count, pack.Maps));
        if (pack.Routes > 0) said.Add(L.F("{0} routes", pack.Routes));

        return said.Count == 0 ? L.T("nothing this app carries") : string.Join(" · ", said);
    }

    [ObservableProperty] private string _apiKey = string.Empty;

    // Pre-composed display strings: a XAML StringFormat would force a reflection-based
    // binding that the trimmer cannot see through.
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasSessionDetails))] private string? _keyLine;
    [ObservableProperty] private string? _accountLine;
    [ObservableProperty] private string? _permissionsLine;

    /// <summary>What the app asks a key for, beside what this key carries.</summary>
    public ObservableCollection<ScopeRow> Scopes { get; } = [];

    /// <summary>Whether there is anything to act on, and what.</summary>
    [ObservableProperty] private string? _scopeSummary;

    /// <summary>Named rather than hidden: a list that does not match should say which side the
    /// difference is on.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSpareScopes))]
    private string? _spareScopesLine;

    public bool HasSpareScopes => !string.IsNullOrWhiteSpace(SpareScopesLine);

    [ObservableProperty] private bool _remindersEnabled;

    /// <summary>Set by the scanner; consumed by the next <see cref="LoadAsync"/>.</summary>
    public string? ScannedKey { get; set; }

    /// <summary>A scanned bridge card, waiting to be paired by the next <see cref="LoadAsync"/>.
    /// Handed over the same way a scanned key is, and for the same reason: doing the work from
    /// the scanner raced with this page's own load.</summary>
    public BridgeCard? ScannedCard { get; set; }

    public bool HasSessionDetails => !string.IsNullOrWhiteSpace(KeyLine);
    public bool IsSignedIn => session.IsAuthenticated;

    // --- item catalogue ----------------------------------------------------

    [ObservableProperty] private string _catalogueLine = string.Empty;

    [ObservableProperty] private double _catalogueProgress;

    /// <summary>
    /// How the last fill ended, kept apart from <see cref="BaseViewModel.Notice"/>.
    ///
    /// Notice is cleared whenever the screen reloads, and returning to this page — or the
    /// phone waking — does exactly that. A download that ran for minutes must not have its
    /// only report wiped by the act of coming back to look at it.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCatalogueOutcome))]
    private string? _catalogueOutcome;

    public bool HasCatalogueOutcome => !string.IsNullOrWhiteSpace(CatalogueOutcome);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotFilling))]
    private bool _isFilling;

    /// <summary>
    /// An inverted boolean rather than a converter in the view: a Converter in XAML forces a
    /// reflection binding the trimmer cannot follow.
    /// </summary>
    public bool IsNotFilling => !IsFilling;

    [RelayCommand]
    public Task LoadAsync() => RunAsync(async ct =>
    {
        SetReminderSwitch(scheduler.IsEnabled);
        LoadWindow();

        // The address and the last import's verdict both survive a restart, so the screen can
        // still say what is on the device and update it without the player finding the address
        // again.
        ShowMarkerPacks();

        // How the last pairing attempt went belongs to that attempt. This view model outlives
        // the page, so without this, coming back to Settings re-showed a verdict about a PC —
        // "sent", or a reason it failed — that nothing had checked since. CatalogueOutcome is
        // pointedly left alone: see the note on it for why that one has to survive a reload.
        BridgeOutcome = null;

        // Before the list is read, so a pairing carried over from the single-PC days is in it.
        await bridge.IsSetUpAsync();

        LoadBridge();

        // The strip has no selection of its own until something marks one.
        if (Tabs.All(tab => !tab.IsSelected)) Select(Tabs[0]);

        // Coming back from the scanner: that key is the whole reason this page reappeared.
        if (ScannedKey is { } scanned)
        {
            ScannedKey = null;
            await AcceptKeyAsync(scanned, ct);
            return;
        }

        if (ScannedCard is { } card)
        {
            ScannedCard = null;
            await AcceptCardAsync(card, ct);
            return;
        }

        if (session.IsAuthenticated)
            await ShowSessionAsync(ct);

        await ShowCatalogueAsync(ct);
    });

    /// <summary>
    /// Downloads every item the game knows about, so searching means searching everything
    /// rather than only what the app has happened to look at.
    ///
    /// Deliberately a button rather than something that happens on its own: it is hundreds of
    /// requests and a few megabytes, and that is the user's call to make on a phone.
    ///
    /// It runs on its own cancellation token rather than through
    /// <see cref="BaseViewModel.RunAsync"/>,
    /// which cancels whatever the screen was previously doing. Returning to this page reloads
    /// it, and the screen going to sleep and waking is enough to do that — so sharing the
    /// screen's token meant the phone's display timeout silently killed its own download
    /// several minutes in.
    /// </summary>
    [RelayCommand]
    public async Task FillCatalogueAsync()
    {
        if (IsFilling) return;

        _fill?.Dispose();
        _fill = new CancellationTokenSource();
        var ct = _fill.Token;

        IsFilling = true;
        CatalogueProgress = 0;
        CatalogueOutcome = null;

        // Progress<T> posts each report to the UI thread rather than running it, so the last one
        // can arrive after the fill has returned and the summary has been written, and leave
        // "Finishing up…" standing under a finished download. Reports are ignored once it is over.
        var over = false;

        try
        {
            var progress = new Progress<CatalogueProgress>(p =>
            {
                if (over) return;

                CatalogueProgress = p.Total == 0 ? 1 : (double)p.Done / p.Total;
                CatalogueLine = p.Done >= p.Total
                    ? L.T("Finishing up…")
                    // Items and recipes are fetched in one pass and counted together; "items"
                    // alone read as more items than the game has (87,693 against 74,287).
                    : L.F("Fetched {0:N0} of {1:N0} new items and recipes.", p.Done, p.Total);
            });

            var added = await catalogue.FillAsync(progress, ct);

            over = true;
            await ShowCatalogueAsync(ct);
            CatalogueOutcome = added == 0
                ? L.T("Already up to date.")
                : L.F("Added {0:N0} entries. Search covers the whole market, and the " +
                      "crafting guide has every recipe.", added);
        }
        catch (OperationCanceledException)
        {
            over = true;
            await ShowCatalogueAsync(CancellationToken.None);
            CatalogueOutcome =
                L.T("Stopped. What arrived is kept — starting again picks up where this left off.");
        }
        catch (Gw2ApiException ex)
        {
            over = true;
            await ShowCatalogueAsync(CancellationToken.None);
            CatalogueOutcome = Describe(ex);
        }
        finally
        {
            IsFilling = false;
        }
    }

    /// <summary>Stops a fill in progress; whatever arrived is kept and the next run resumes.</summary>
    [RelayCommand]
    public void StopFilling() => _fill?.Cancel();

    private async Task ShowCatalogueAsync(CancellationToken ct)
    {
        var status = await catalogue.StatusAsync(ct);

        CatalogueLine = status.Known == 0
            ? L.T("Nothing downloaded yet. Search only finds what the app has already seen, and " +
                  "the crafting guide has no recipes to work from.")
            : L.F("{0:N0} items known, {1:N0} of them tradable · {2:N0} recipes.",
                  status.Known, status.Tradable, status.Recipes) +
              (status.OtherLanguage == 0
                  ? string.Empty
                  : " " + L.F("{0:N0} still named in the phone's earlier language; downloading renames them.",
                      status.OtherLanguage));
    }

    [RelayCommand]
    public Task SignInAsync() => RunAsync(async ct =>
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            ErrorMessage = L.T("Paste a key first.");
            return;
        }

        await AcceptKeyAsync(ApiKey, ct);
    });

    private async Task AcceptKeyAsync(string key, CancellationToken ct)
    {
        var previousKeyId = session.Token?.Id;

        await session.SignInAsync(key, ct);
        ApiKey = string.Empty; // Stored encrypted; no reason to keep it on screen.

        // A different key starts fresh: another account's trade history and objectives are
        // not news. Re-entering the same key keeps its history, so re-scanning your own QR
        // does not silently reset what you have already been told about.
        if (session.Token?.Id != previousKeyId)
        {
            await alerts.ClearAsync(ct);
            await activity.ClearAsync(ct);
            await ledger.ClearAsync(ct);

            // Another account's wallet is not a useful thing to show while offline.
            await snapshots.ClearAsync(ct);
        }

        await ShowSessionAsync(ct);
        Notice = L.T("Key accepted. Pull to refresh on the other screens.");
    }

    [RelayCommand]
    public Task SignOutAsync() => RunAsync(async ct =>
    {
        await session.SignOutAsync();
        await alerts.ClearAsync(ct);
        await activity.ClearAsync(ct);
        await ledger.ClearAsync(ct);
        await snapshots.ClearAsync(ct);
        scheduler.Disable();
        SetReminderSwitch(false);

        KeyLine = AccountLine = PermissionsLine = null;
        ScopeSummary = SpareScopesLine = null;
        Scopes.Clear();

        Notice = L.T("Key removed from this device.");
        OnPropertyChanged(nameof(IsSignedIn));
    });

    /// <summary>
    /// Runs the background check immediately instead of waiting for the next 30-minute
    /// window — useful for confirming reminders work, and for a deliberate look before
    /// putting the phone away.
    /// </summary>
    [RelayCommand]
    public Task CheckNowAsync() => RunAsync(async ct =>
    {
        if (!session.IsAuthenticated)
        {
            ErrorMessage = L.T("Connect an API key first.");
            return;
        }

        var run = await alertService.CheckAndNotifyAsync(ct);
        Notice = (run.Sent, run.Held) switch
        {
            (0, 0) when run.Quiet > 0 =>
                L.F("Checked. {0} held back: it is outside your notification hours. {1}",
                    run.Quiet, QuietHours.Describe(schedule.Current)),
            (0, 0) => L.T("Checked. Nothing waiting that you have not already seen."),
            (_, 0) => run.Sent == 1 ? L.T("Checked. One reminder sent.") : L.F("Checked. {0} reminders sent.", run.Sent),
            // Said, because a check that found something and showed nothing looks broken.
            _ => L.F("Checked. {0} sent, {1} held back: {2} is in the game, " +
                     "which shows trading post and vault news itself.", run.Sent, run.Held, run.Player)
        };
    });

    /// <summary>Opens the scanner; it fills in the key and signs in on a successful read.</summary>
    [RelayCommand]
    public Task ScanAsync() => navigator.GoToAsync("scan");

    /// <summary>
    /// Four unrelated things lived on one scroll: a key, a background poll, an 11 MB download
    /// and an account summary. Nothing here is read top to bottom, so grouping beats ordering.
    /// </summary>
    public ObservableCollection<PageTab> Tabs { get; } = windowMode.IsSupported
        ? [new(L.T("Key"), 0), new(L.T("Reminders"), 1), new(L.T("Data"), 2), new(L.T("Bridge"), 3), new(L.T("Desktop"), 4)]
        : [new(L.T("Key"), 0), new(L.T("Reminders"), 1), new(L.T("Data"), 2), new(L.T("Bridge"), 3)];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsKey))]
    [NotifyPropertyChangedFor(nameof(IsReminders))]
    [NotifyPropertyChangedFor(nameof(IsData))]
    [NotifyPropertyChangedFor(nameof(IsBridge))]
    [NotifyPropertyChangedFor(nameof(IsDesktop))]
    private int _selectedTab;

    public bool IsKey => SelectedTab == 0;
    public bool IsReminders => SelectedTab == 1;
    public bool IsData => SelectedTab == 2;
    public bool IsBridge => SelectedTab == 3;
    public bool IsDesktop => SelectedTab == 4;

    /// <summary>The window's own settings, bound directly: F11 changes them too, not only this page.
    /// Only a desktop head offers the tab.</summary>
    public IWindowMode WindowMode => windowMode;

    /// <summary>The app's own updates, bound directly: they change in the background.</summary>
    public IAppUpdates Updates => updates;

    [RelayCommand]
    private Task CheckForUpdateAsync() => updates.CheckAsync();

    [RelayCommand]
    private void RestartToUpdate() => updates.RestartToUpdate();

    /// <summary>Switches which group is on screen.</summary>
    public void Select(PageTab tab)
    {
        SelectedTab = tab.Index;

        foreach (var candidate in Tabs)
            candidate.IsSelected = candidate.Index == tab.Index;
    }

    // --- when notifications may arrive ------------------------------------

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(WindowLine))] private bool _windowEnabled;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(WindowLine))] private TimeSpan _windowFrom;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(WindowLine))] private TimeSpan _windowTo;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(WindowLine))] private bool _windowWeekends;

    private bool _loadingWindow;

    /// <summary>What the setting currently does, so it can be read back rather than inferred
    /// from four controls.</summary>
    public string WindowLine => QuietHours.Describe(CurrentWindow);

    private NotifyWindow CurrentWindow => new(
        WindowEnabled,
        TimeOnly.FromTimeSpan(WindowFrom),
        TimeOnly.FromTimeSpan(WindowTo),
        WindowWeekends);

    private void LoadWindow()
    {
        var window = schedule.Current;

        _loadingWindow = true;
        WindowEnabled = window.Enabled;
        WindowFrom = window.From.ToTimeSpan();
        WindowTo = window.To.ToTimeSpan();
        WindowWeekends = window.AllDayAtWeekends;
        _loadingWindow = false;
    }

    /// <summary>Saved on every change: there is no OK button on a settings page.</summary>
    private void SaveWindow()
    {
        if (_loadingWindow) return;

        schedule.Current = CurrentWindow;
    }

    partial void OnWindowEnabledChanged(bool value) => SaveWindow();
    partial void OnWindowFromChanged(TimeSpan value) => SaveWindow();
    partial void OnWindowToChanged(TimeSpan value) => SaveWindow();
    partial void OnWindowWeekendsChanged(bool value) => SaveWindow();

    partial void OnRemindersEnabledChanged(bool value)
    {
        if (_applyingReminderState) return;
        _ = ApplyReminderStateAsync(value);
    }

    private async Task ApplyReminderStateAsync(bool enable)
    {
        if (!enable)
        {
            scheduler.Disable();
            Notice = L.T("Reminders off.");
            return;
        }

        if (!await notifier.RequestPermissionAsync())
        {
            SetReminderSwitch(false);
            ErrorMessage = L.T("Android needs notification permission. Grant it in system settings, then try again.");
            return;
        }

        scheduler.Enable();
        ErrorMessage = null;
        Notice = L.T("Reminders on. The app checks every 30 minutes and tells you what is waiting.");
    }

    private void SetReminderSwitch(bool value)
    {
        _applyingReminderState = true;
        RemindersEnabled = value;
        _applyingReminderState = false;
    }

    private async Task ShowSessionAsync(CancellationToken ct)
    {
        KeyLine = L.F("Key: {0}", session.Token?.Name);

        var granted = session.Token?.Permissions;

        PermissionsLine = L.F("This key carries: {0}", string.Join(", ", granted ?? []));

        // What the app asks for, beside what arrived. Worth a list rather than the flat line
        // this used to be: a missing scope does not always announce itself — heropoints answers
        // 200 with an empty array — so the only way to catch one is to read the two side by side.
        Scopes.Clear();

        foreach (var status in Gw2Scopes.Compare(granted)) Scopes.Add(new ScopeRow(status));

        ScopeSummary = Gw2Scopes.Summary(granted);

        var spare = Gw2Scopes.Spare(granted);

        SpareScopesLine = spare.Count == 0
            ? null
            : L.F("Also on the key, unused by the app: {0}.", string.Join(", ", spare));

        AccountLine = session.HasPermission(Gw2Permissions.Account)
            ? L.F("Account: {0}", (await client.GetAccountAsync(ct)).Name)
            : L.T("Account: (key has no 'account' permission)");

        OnPropertyChanged(nameof(IsSignedIn));
    }

    // --- the PC bridge -----------------------------------------------------

    /// <summary>
    /// Where the desktop bridge is, and the code it is expecting.
    ///
    /// Typed rather than discovered. mDNS would save the typing and cost a dependency, a
    /// permission and a class of failure that is invisible from the phone; the bridge window
    /// prints its own address, and this is entered once.
    /// </summary>
    [ObservableProperty] private string _bridgeHost = string.Empty;
    [ObservableProperty] private string _bridgeCode = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBridgeOutcome))]
    private string? _bridgeOutcome;

    public bool HasBridgeOutcome => !string.IsNullOrWhiteSpace(BridgeOutcome);

    public string BridgeNote => L.T(
        "Guild Wars 2 has no Android client, so the phone's clipboard is nowhere near the game. " +
        "Run the bridge app on the PC you play on and scan the QR code it shows — tapping an " +
        "emote here then puts it on that PC's clipboard, ready for Ctrl+V in the chat box. " +
        "Pair as many PCs as you like; whichever one is awake on your network gets the paste, " +
        "and the one running the game wins if several are. Addresses are found, not typed.");

    /// <summary>The paired PCs, as the list on the page shows them.</summary>
    public ObservableCollection<KnownBridge> Bridges { get; } = [];

    /// <summary>
    /// A known PC seen at an address the phone has not agreed to trust, or null.
    ///
    /// Only ever set for a pairing that was typed rather than scanned: a short code cannot prove
    /// anything, so an id heard on the network is not enough to start sending somewhere new.
    /// Scanned pairings never come through here — they prove themselves and move silently.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMove))]
    [NotifyPropertyChangedFor(nameof(MoveNote))]
    private BridgeMove? _move;

    public bool HasMove => Move is not null;

    public string MoveNote =>
        Move is not { } move
            ? string.Empty
            : L.F("{0} is answering at {1} instead of {2}. " +
                  "Use it? This is asked because that PC was paired by typing a code, which cannot " +
                  "prove it is really the same machine. Pairing by QR code never has to ask.",
                  move.Bridge.Label, move.To, move.From ?? L.T("its old address"));

    /// <summary>
    /// Brings the shown list of PCs up to date with the stored one.
    ///
    /// Reconciled in place rather than cleared and refilled, and that is load-bearing rather
    /// than tidy. <see cref="ObservableCollection{T}.Clear"/> leaves the collection empty for an
    /// instant, which makes the page's <c>BindableLayout</c> build its empty view and disconnect
    /// every row handler on the way — and on a Settings page that has been navigated away from
    /// and back to, one of those handlers belongs to an already-disposed platform button. That
    /// threw <see cref="ObjectDisposedException"/> on the main thread and took the whole app
    /// down, reproducibly, on the second visit to the page.
    ///
    /// <see cref="KnownBridge"/> is a record, so an entry whose values have not changed compares
    /// equal and is left alone entirely: in the common case this rebuilds nothing.
    /// </summary>
    private void LoadBridge()
    {
        var known = bridge.Known;

        for (var i = Bridges.Count - 1; i >= 0; i--)
            if (known.All(k => k.Id != Bridges[i].Id))
                Bridges.RemoveAt(i);

        for (var i = 0; i < known.Count; i++)
        {
            var shown = Bridges.FirstOrDefault(b => b.Id == known[i].Id);

            if (shown is null)
                Bridges.Insert(Math.Min(i, Bridges.Count), known[i]);
            else if (shown != known[i])
                Bridges[Bridges.IndexOf(shown)] = known[i];
        }

        Move = bridge.Move;
    }

    /// <summary>
    /// Finds a paired PC and proves it, then pastes one sentence to show it really worked.
    ///
    /// No address to save first, which is the difference from what this used to be: there is
    /// nothing to type, so there is nothing to type wrongly.
    /// </summary>
    [RelayCommand]
    public Task PairBridgeAsync() => RunAsync(async ct =>
    {
        if (!await bridge.IsSetUpAsync())
        {
            BridgeOutcome = L.T("No PC paired yet. Scan the QR code in the bridge window.");
            return;
        }

        BridgeOutcome = L.T("Looking for your PCs…");

        // Reachability first, because "none of them answered" and "the credential is wrong" are
        // different problems and only one of them is about the pairing.
        var found = await bridge.TestAsync(ct);

        LoadBridge();

        if (!found.IsSent)
        {
            // Lead with what was actually learnt, when anything was. "Is the bridge running?" is
            // the wrong question to put to somebody whose bridge is running and answering and
            // whose secret simply no longer matches — and that is the one case where the app
            // knows the answer and used to bury it behind the guess.
            BridgeOutcome = bridge.Trouble is { } why
                ? why
                : L.T("No paired PC answered. Is the bridge running, and is this phone on the same " +
                      "network?");

            return;
        }

        // A real paste, because a handshake that does not exercise the credential proves nothing.
        var sent = await bridge.SendAsync(L.T("Paired with the Guild Wars 2 Companion"), ct);

        BridgeOutcome = sent.IsSent
            ? L.F("Paired with {0}. That sentence is on its clipboard now — paste it anywhere " +
                  "to check.", sent.Pc)
            : L.F("Found {0} but {1}.", found.Pc, sent.Text);
    });

    /// <summary>
    /// Pairs from a scanned QR code. The strong path: a long secret, so the phone can recognise
    /// that PC at any address afterwards without ever being told one.
    /// </summary>
    private async Task AcceptCardAsync(BridgeCard card, CancellationToken ct)
    {
        var paired = await bridge.PairAsync(card);

        LoadBridge();

        BridgeOutcome = L.F("{0} paired. Looking for it…", paired.Label);

        var sent = await bridge.SendAsync(L.T("Paired with the Guild Wars 2 Companion"), ct);

        LoadBridge();

        BridgeOutcome = sent.IsSent
            ? L.F("Paired with {0}. That sentence is on its clipboard now.", sent.Pc)
            : L.F("{0} is paired but {1}. It will be found when it is running.", paired.Label, sent.Text);
    }

    /// <summary>
    /// Pairs by hand, for when the camera is unavailable or the QR will not read.
    ///
    /// The address is still asked for here, and only here, because there is nothing yet to find
    /// the PC by — the id arrives with the bridge's first answer and takes over from then on.
    /// </summary>
    [RelayCommand]
    public Task PairTypedAsync() => RunAsync(async ct =>
    {
        if (BridgeHost.Trim().Length == 0 || BridgeCode.Trim().Length == 0)
        {
            BridgeOutcome = L.T("Enter both the address and the code from the bridge window.");
            return;
        }

        await bridge.PairAsync(BridgeHost, BridgeCode);

        BridgeHost = string.Empty;
        BridgeCode = string.Empty;

        LoadBridge();

        await PairBridgeCommand.ExecuteAsync(null);
    });

    /// <summary>
    /// Hands the diagnostic log to whatever the phone can share with.
    ///
    /// Sharing rather than showing: the log is worth reading next to the bridge's own, on the
    /// PC, and a wall of monospace on a phone screen is the least useful place to put it. It
    /// records what happened, never what was pasted.
    /// </summary>
    [RelayCommand]
    public async Task ShareLogAsync()
    {
        if (!File.Exists(journal.Location))
        {
            LogLine = L.T("Nothing logged yet. Use the bridge once and it will be here.");
            return;
        }

        LogLine = $"{new FileInfo(journal.Location).Length / 1024} KB";

        await system.ShareFileAsync(L.T("Guild Wars 2 Companion log"), journal.Location);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLogLine))]
    private string? _logLine;

    public bool HasLogLine => !string.IsNullOrWhiteSpace(LogLine);

    /// <summary>
    /// Which build is installed: 1.0.42+ee30fd6, the repository version and the commit it came
    /// from (see Directory.Build.targets). Read from the installed package, so it is what Android
    /// actually has rather than what the last build produced.
    /// </summary>
    public string VersionLine => $"Waypoint {environment.Version}";

    /// <summary>Opens the camera. Pairing itself happens back here, on the way in.</summary>
    [RelayCommand]
    public Task ScanBridgeAsync() => navigator.GoToAsync("scanbridge");

    /// <summary>Accepts the new address for a typed pairing, once the user has said so.</summary>
    [RelayCommand]
    public Task AllowMoveAsync() => RunAsync(async ct =>
    {
        if (Move is not { } move) return;

        bridge.Allow(move);

        LoadBridge();

        var sent = await bridge.SendAsync(L.T("Paired with the Guild Wars 2 Companion"), ct);

        LoadBridge();

        BridgeOutcome = sent.IsSent
            ? L.F("{0} is at {1} now.", sent.Pc, move.To)
            : L.F("{0} moved but {1}.", move.Bridge.Label, sent.Text);
    });

    [RelayCommand]
    public void ForgetBridge(KnownBridge? known)
    {
        if (known is null) return;

        bridge.Forget(known.Id);

        LoadBridge();

        BridgeOutcome = Bridges.Count == 0
            ? L.T("Forgotten. Emotes now only reach this phone's clipboard.")
            : L.F("{0} forgotten.", known.Label);
    }
}
