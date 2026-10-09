using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GuildWarsCompanion.Services;

namespace GuildWarsCompanion.ViewModels;

/// <summary>
/// The marker packs a repository offers, to add with one tap instead of a typed address.
///
/// Opened from Settings, where the pasted address stays for anything the list does not have.
/// Adding goes through <see cref="MarkerStore.AddAsync"/>, the same download as a pasted address,
/// so a pack added here is a pack like any other: updated and removed on the Settings screen.
/// </summary>
public sealed partial class MarkerPacksViewModel(PackRepository repository, MarkerStore markers) : BaseViewModel
{
    private IReadOnlyList<PackListing> _all = [];
    private CancellationTokenSource? _adding;

    [ObservableProperty] private string _search = string.Empty;
    [ObservableProperty] private string? _summary;

    /// <summary>The list's address as typed, applied with USE THIS LIST.</summary>
    [ObservableProperty] private string _address = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotAdding))]
    private bool _isAdding;

    public bool IsNotAdding => !IsAdding;

    public ObservableCollection<PackRow> Packs { get; } = [];

    protected override string DefaultEmptyMessage => "No pack matches that.";

    partial void OnSearchChanged(string value) => Show();

    [RelayCommand]
    public Task RefreshAsync() => LoadAsync(force: true);

    public Task LoadAsync(bool force = false) => RunAsync(async ct =>
    {
        Address = repository.Address;

        var catalogue = await repository.GetAsync(force, ct);

        _all = catalogue.Packs;
        Show();

        var host = Uri.TryCreate(repository.Address, UriKind.Absolute, out var uri) ? uri.Host : repository.Address;
        var left = catalogue.Skipped > 0 ? $" · {catalogue.Skipped} left out, offered over plain http" : "";

        Summary = $"{_all.Count} packs from {host}{left}";
        ErrorMessage = repository.Problem;
    });

    /// <summary>Points the screen at another list, or back at Blish HUD's when the box is empty.</summary>
    [RelayCommand]
    public Task UseAddressAsync()
    {
        repository.Address = Address;

        return LoadAsync(force: true);
    }

    [RelayCommand]
    public Task UseDefaultAsync()
    {
        repository.Address = PackRepository.DefaultAddress;

        return LoadAsync(force: true);
    }

    /// <summary>
    /// Downloads one pack. One at a time: a pack can be tens of megabytes over mobile data, and
    /// two at once would only make both slower.
    /// </summary>
    public async Task AddAsync(PackRow row)
    {
        if (IsAdding || row.IsAdded) return;

        IsAdding = true;
        row.Status = row.Listing.SizeMb is { } size ? $"Downloading {Megabytes(size)}…" : "Downloading…";

        _adding?.Dispose();
        _adding = new CancellationTokenSource();

        try
        {
            var done = await markers.AddAsync(row.Listing.Download, _adding.Token);

            row.Status = done.Note;
            row.IsAdded = Added(row.Listing);
        }
        catch (OperationCanceledException)
        {
            row.Status = "Stopped.";
        }
        finally
        {
            IsAdding = false;
        }
    }

    [RelayCommand]
    public void Stop() => _adding?.Cancel();

    private void Show()
    {
        var words = Search.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        Packs.Clear();

        foreach (var listing in _all.Where(listing => words.All(word => Matches(listing, word))))
            Packs.Add(new PackRow(listing, Added(listing), Byline(listing)));
    }

    /// <summary>A word anywhere a player would look: name, author, categories, description.</summary>
    private static bool Matches(PackListing listing, string word) =>
        listing.Name.Contains(word, StringComparison.OrdinalIgnoreCase) ||
        listing.Author?.Contains(word, StringComparison.OrdinalIgnoreCase) == true ||
        listing.Categories.Any(category => category.Contains(word, StringComparison.OrdinalIgnoreCase)) ||
        listing.Description?.Contains(word, StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>The same key the store files a pack under, so "added" means added by any route.</summary>
    private bool Added(PackListing listing)
    {
        var id = MarkerIndex.Name(listing.Download);

        return markers.Packs.Any(pack => pack.Id == id);
    }

    /// <summary>"Metallis · 6.8 MB · updated Jun 2025 · 154 maps", leaving out what the list does not say.</summary>
    private static string Byline(PackListing listing)
    {
        var said = new List<string>();

        if (listing.Author is { } author) said.Add(author);
        if (listing.SizeMb is { } size) said.Add(Megabytes(size));
        if (listing.Updated is { } updated)
            said.Add("updated " + updated.ToString("MMM yyyy", CultureInfo.InvariantCulture));
        if (listing.Maps > 0) said.Add(listing.Maps == 1 ? "1 map" : $"{listing.Maps} maps");

        return string.Join(" · ", said);
    }

    private static string Megabytes(double size) =>
        size < 1 ? "under 1 MB" : $"{size.ToString("0.#", CultureInfo.InvariantCulture)} MB";
}

public sealed partial class PackRow(PackListing listing, bool isAdded, string byline) : ObservableObject
{
    public PackListing Listing { get; } = listing;

    public string Name => Listing.Name;

    public string Byline { get; } = byline;

    public string Categories => string.Join(" · ", Listing.Categories);

    public bool HasCategories => Listing.Categories.Count > 0;

    public string? Description => Listing.Description;

    public bool HasDescription => Listing.Description is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAdd), nameof(Mark))]
    private bool _isAdded = isAdded;

    public bool CanAdd => !IsAdded;

    /// <summary>Said on the row rather than as a button that does nothing.</summary>
    public string Mark => IsAdded ? "ADDED" : string.Empty;

    /// <summary>What the last add of this pack did: downloading, kept, or why not.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string? _status;

    public bool HasStatus => Status is not null;
}
