using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GuildWarsCompanion.Host;
using GuildWarsCompanion.Services;

namespace GuildWarsCompanion.ViewModels;

/// <summary>
/// "Where is my X?" — and, because answering it means reading every place at once, what all
/// of it is worth.
///
/// The read is the expensive part and the search is free, so the screen loads once and then
/// searches offline against what it already has.
/// </summary>
public sealed partial class FindViewModel(StorageService storage, ItemLookup items, INavigator navigator)
    : BaseViewModel
{
    [ObservableProperty] private string _search = string.Empty;
    [ObservableProperty] private string? _searchNote;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTotal))]
    private string _totalText = string.Empty;

    [ObservableProperty] private string? _storageNote;
    [ObservableProperty] private bool _hasResults;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NotLoaded))]
    private bool _isLoaded;

    public bool HasTotal => !string.IsNullOrEmpty(TotalText);

    /// <summary>
    /// Drives the "pull down" line. An inverted boolean rather than a converter in the view:
    /// a Converter in XAML forces a reflection binding the trimmer cannot follow.
    /// </summary>
    public bool NotLoaded => !IsLoaded;

    public ObservableCollection<FoundItem> Results { get; } = [];
    public ObservableCollection<PlaceTotal> Places { get; } = [];

    /// <summary>
    /// Matches you do not own. Without these the item screen is reachable only for things
    /// already in your storage, which makes "what would it take to craft this?" unanswerable
    /// for anything you have not got yet.
    /// </summary>
    public ObservableCollection<FoundItem> Elsewhere { get; } = [];

    [ObservableProperty] private bool _hasElsewhere;

    protected override string DefaultEmptyMessage =>
        L.T("Pull down to read your bank, your bags and material storage.");

    /// <summary>
    /// Reads every storage place. Costs four account requests plus item lookups, so it is
    /// deliberately a manual refresh rather than something that happens on every visit.
    /// </summary>
    [RelayCommand]
    public Task RefreshAsync() => RunAsync(async ct =>
    {
        await storage.RefreshAsync(ct);

        Places.Clear();
        var overview = storage.Overview();
        foreach (var place in overview.Places)
            Places.Add(place);

        TotalText = overview.TotalText;
        StorageNote = overview.Note;
        IsLoaded = overview.HasAnything;

        // A search typed before the first load should answer itself once the data lands.
        if (Search.Trim().Length >= 2) Look();
    });

    /// <summary>Fills the second group: catalogue matches that are not in your storage.</summary>
    private async Task LookElsewhereAsync(IEnumerable<int> owned, string term)
    {
        Elsewhere.Clear();

        var mine = owned.ToHashSet();

        foreach (var item in await items.SearchAsync(term))
        {
            if (mine.Contains(item.Id)) continue;

            Elsewhere.Add(new FoundItem(
                item.Id, item.Name, item.Icon, item.Rarity,
                0, string.Empty, L.T("none — tap to see what it takes"), string.Empty));
        }

        HasElsewhere = Elsewhere.Count > 0;
    }

    /// <summary>Opens a result's item.</summary>
    [RelayCommand]
    public Task OpenAsync(FoundItem? item) =>
        item is null
            ? Task.CompletedTask
            : navigator.GoToAsync($"item?id={item.ItemId}");

    [RelayCommand]
    private void Find() => Look();

    private void Look()
    {
        Results.Clear();

        if (!storage.HasSnapshot)
        {
            SearchNote = L.T("Pull down to read your storage first.");
            HasResults = false;
            return;
        }

        if (Search.Trim().Length < 2)
        {
            SearchNote = L.T("Type at least two letters.");
            HasResults = false;
            return;
        }

        var found = storage.Find(Search);
        foreach (var item in found)
            Results.Add(item);

        HasResults = found.Count > 0;

        _ = LookElsewhereAsync(found.Select(f => f.ItemId), Search);

        // The count matters: it says the search covered everything owned, not everything
        // the app happens to have looked at before.
        SearchNote = found.Count > 0
            ? L.F("{0} of the {1:N0} different items you own.", found.Count, storage.DistinctItems)
            : L.F("Nothing matching that among the {0:N0} different items you own.", storage.DistinctItems);
    }
}
