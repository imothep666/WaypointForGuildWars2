using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using GuildWarsCompanion.Host;
using GuildWarsCompanion.Services;

namespace GuildWarsCompanion.ViewModels;

/// <summary>
/// One category of unlocks, opened from the overview.
///
/// A route of its own rather than a second state inside the overview, which is what it was
/// first. The screen it is modelled on — a character, an item, a collection — is a pushed page,
/// so Shell gives it the back arrow in place of the hamburger and Android's own back gesture
/// works without being taught to. An in-page BACK button had neither.
///
/// Nothing is re-fetched by pushing: <see cref="UnlockService"/> keeps both halves for the
/// session, so coming back to a category it has already read is immediate.
/// </summary>
public sealed partial class UnlockCategoryViewModel(UnlockService unlocks) : BaseViewModel, IQueryTarget
{
    /// <summary>What the dial draws: every entry, whether owned or not.</summary>
    public ObservableCollection<UnlockRow> Entries { get; } = [];

    /// <summary>
    /// The dye grid: one group per rarity for what is unlocked, then one for everything locked,
    /// each holding rows of four. See <see cref="TileRow"/> and <see cref="Unlocks.Group"/>.
    /// </summary>
    public ObservableCollection<TileGroup> Groups { get; } = [];

    /// <summary>How many swatches to a row.</summary>
    public const int Across = 4;

    [ObservableProperty] private string? _heading;

    [ObservableProperty] private string? _count;

    private UnlockCategory? _category;

    /// <summary>
    /// Dyes are drawn as a grid of swatches; everything else is a list of names.
    ///
    /// The split is about what there is to show. A dye is a colour, and four across says more in
    /// one screen than six hundred rows of the word for it. A title is a name and nothing else,
    /// and a grid of names is just a list with the words cut in half.
    /// </summary>
    public bool IsGrid => _category?.Kind == UnlockKind.Dye;

    /// <summary>
    /// The category's name as the route carries it.
    ///
    /// The enum's own name rather than an index: a route is a string a person can read in a log,
    /// and reordering <see cref="Unlocks.All"/> must not silently open a different screen.
    /// </summary>
    public string Kind
    {
        set
        {
            _category = Unlocks.All.FirstOrDefault(
                category => string.Equals(category.Kind.ToString(), value, StringComparison.OrdinalIgnoreCase));

            Heading = _category?.Name.ToUpperInvariant() ?? "UNLOCKS";

            _ = LoadAsync();
        }
    }

    public void ApplyQuery(IDictionary<string, object> query)
    {
        if (RouteQuery.Text(query, "kind") is { } kind) Kind = kind;
    }

    protected override string DefaultEmptyMessage => "Nothing to show for this category.";

    [RelayCommand]
    public Task LoadAsync() => RunAsync(async ct =>
    {
        Entries.Clear();
        Groups.Clear();

        if (_category is not { } category)
        {
            Notice = "That category is not one this app knows.";
            return;
        }

        var entries = await unlocks.EntriesAsync(category, ct);

        foreach (var entry in entries) Entries.Add(new UnlockRow(entry));

        if (category.Kind == UnlockKind.Dye)
            foreach (var group in Unlocks.Group(entries))
                Groups.Add(new TileGroup(
                    group,
                    group.Entries
                        .Select(entry => new UnlockRow(entry))
                        .Chunk(Across)
                        .Select(chunk => new TileRow(chunk))));

        // Counted from the drawn rows rather than from the overview's tally, so the number and
        // the list below it cannot disagree — see Unlocks.Counted.
        Count = Unlocks.Counted(entries);
    });
}
