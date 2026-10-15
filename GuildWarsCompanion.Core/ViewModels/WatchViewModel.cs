using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GuildWarsCompanion.Gw2;
using GuildWarsCompanion.Host;
using GuildWarsCompanion.Services;

namespace GuildWarsCompanion.ViewModels;

public sealed partial class WatchViewModel(
    WatchService watches, TradingPostLink link, TestEvidence evidence)
    : BaseViewModel, IQueryTarget
{
    /// <summary>
    /// Opened from an item's page with <c>item=id</c>: that item is chosen, the market as it stands
    /// is said, and only the price is left to type. Whether to wait for a fall or a rise stays the
    /// player's; buying for a goal is a fall, which is the default.
    /// </summary>
    public void ApplyQuery(IDictionary<string, object> query)
    {
        if (RouteQuery.Int(query, "item") is not { } id) return;

        // Not through RunAsync: the page reloads its list as it appears, straight after this, and
        // that run would cancel this one — the item would silently never arrive.
        _ = ChooseByIdAsync(id);
    }

    private async Task ChooseByIdAsync(int id)
    {
        try
        {
            if (await watches.CandidateAsync(id) is not { } found)
            {
                ErrorMessage = L.T("The API does not know this item.");
                return;
            }

            // Whatever was searched for or typed last time was for another item.
            Search = string.Empty;
            Threshold = string.Empty;

            Choose(found);
            WatchSellPrice = true;

            evidence.Saw("watch-from-item");
        }
        catch (Gw2ApiException)
        {
            ErrorMessage = L.T("Could not read this item just now.");
        }
    }
    [ObservableProperty] private string _search = string.Empty;
    [ObservableProperty] private string _threshold = string.Empty;

    /// <summary>Buy price rising is a sell signal; sell price falling is a buy signal.</summary>
    [ObservableProperty] private bool _watchSellPrice = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCandidate))]
    [NotifyPropertyChangedFor(nameof(CandidateName))]
    [NotifyPropertyChangedFor(nameof(CandidateBaseline))]
    private WatchCandidate? _candidate;

    [ObservableProperty] private string? _searchNote;

    public bool HasCandidate => Candidate is not null;
    public string CandidateName => Candidate?.Name ?? string.Empty;

    /// <summary>Today's market under the chosen item, so the threshold is named against it.</summary>
    public string CandidateBaseline => Candidate is null
        ? string.Empty
        : $"{Candidate.Baseline} {L.T("Name the price you are waiting for.")}";

    /// <summary>Drives the empty line: a template cannot reach out to the view model.</summary>
    [ObservableProperty] private bool _hasNoWatches = true;

    public ObservableCollection<WatchRow> Watches { get; } = [];
    public ObservableCollection<WatchCandidate> Results { get; } = [];

    /// <summary>Watches whose price has arrived.</summary>
    public int Met => Watches.Count(watch => watch.IsMet);

    public string MetLabel => Watches.Count == 1
        ? L.T("of 1 watched price reached")
        : L.F("of {0} watched prices reached", Watches.Count);

    protected override string DefaultEmptyMessage =>
        L.T("No watches yet. Find an item below and name the price you are waiting for.");

    private void CountWatches()
    {
        HasNoWatches = Watches.Count == 0;
        OnPropertyChanged(nameof(Met));
        OnPropertyChanged(nameof(MetLabel));
    }

    [RelayCommand]
    public Task RefreshAsync() => RunAsync(async ct =>
    {
        Watches.Clear();

        // No key needed: commerce prices are public, so this screen works signed out.
        foreach (var row in await watches.ListAsync(ct))
            Watches.Add(row);

        CountWatches();
    });

    /// <summary>
    /// Searches the items the app has already seen. The API has no item search, so the local
    /// cache is the index — which means it covers what you have looked at, not everything.
    /// </summary>
    [RelayCommand]
    public Task FindAsync() => RunAsync(async ct =>
    {
        Results.Clear();
        Candidate = null;

        if (Search.Trim().Length < 2)
        {
            SearchNote = L.T("Type at least two letters.");
            return;
        }

        var found = await watches.SearchAsync(Search, ct);
        foreach (var item in found)
            Results.Add(item);

        var (known, isCatalogue) = await watches.SearchableCountAsync(ct);

        // Two different situations, and the difference matters: with the catalogue filled, a
        // miss means the market does not trade it. Without it, a miss means the app simply
        // has not looked at it yet — and that is fixable, so say where.
        SearchNote = (found.Count > 0, isCatalogue) switch
        {
            (true, true) => L.F("{0} of {1:N0} tradable items.", found.Count, known),
            (true, false) => L.F("{0} of {1:N0} items the app has seen.", found.Count, known),
            (false, true) => L.F("Nothing tradable matches that, among {0:N0} items.", known),
            (false, false) =>
                L.F("No match among the {0:N0} items the app has seen. Download the full " +
                    "item list under Settings to search the whole market.", known)
        };
    });

    /// <summary>Picks a search result as the subject of a new watch.</summary>
    public void Choose(WatchCandidate item)
    {
        Candidate = item;
        Results.Clear();
        SearchNote = null;

        if (item.Asking is not null || item.Offered is not null) evidence.Saw("watch-baseline");
    }

    [RelayCommand]
    public Task AddAsync() => RunAsync(async ct =>
    {
        if (Candidate is null)
        {
            ErrorMessage = L.T("Find an item first.");
            return;
        }

        var price = PriceRules.ParsePrice(Threshold);
        if (price is null)
        {
            ErrorMessage = L.T("Enter a price like 1g 20s, 45s, or a number of copper.");
            return;
        }

        // Falling below is what you wait for when buying; rising above when selling.
        await watches.AddAsync(Candidate, WatchSellPrice, WatchSellPrice, price.Value, ct);

        Candidate = null;
        Threshold = string.Empty;
        Search = string.Empty;

        foreach (var row in await watches.ListAsync(ct))
            if (Watches.All(w => w.Id != row.Id))
                Watches.Add(row);

        CountWatches();
        Notice = L.T("Watching. You will be told when the price gets there.");
    });

    [RelayCommand]
    public Task RemoveAsync(WatchRow? row) => row is null
        ? Task.CompletedTask
        : RunAsync(async ct =>
        {
            await watches.RemoveAsync(row.Id, ct);

            foreach (var existing in Watches.Where(w => w.Id == row.Id).ToList())
                Watches.Remove(existing);

            CountWatches();
        });

    /// <summary>
    /// The watched item's chat link to wherever the game can paste it, for the trading post's
    /// search; see <see cref="TradingPostLink"/>.
    /// </summary>
    [RelayCommand]
    public async Task SendToTradingPostAsync(WatchRow? row)
    {
        if (row is null) return;

        ErrorMessage = null;
        Notice = await link.SendAsync(row.ItemId, row.Name, sending => Notice = sending);
    }
}
