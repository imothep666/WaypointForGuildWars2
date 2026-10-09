using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using GuildWarsCompanion.Gw2;
using GuildWarsCompanion.Host;
using GuildWarsCompanion.Services;

namespace GuildWarsCompanion.ViewModels;

/// <summary>
/// One category on the overview: how much of it the account has.
/// </summary>
public sealed partial class UnlockCategoryRow(UnlockTally tally) : ObservableObject
{
    public UnlockCategory Category { get; } = tally.Category;

    public string Name => Category.Name;

    /// <summary>"412 of 682", or a reason when the category could not be read.</summary>
    public string Line => IsUnknown ? "could not be read" : tally.Line;

    public double Fraction => tally.Fraction;

    /// <summary>
    /// Nothing at all came back for this category.
    ///
    /// Told apart from an account that owns none of something, which is a real answer and a
    /// bar at zero. A total of zero is the give-away: the public catalogue always has entries.
    /// </summary>
    public bool IsUnknown => tally.Total == 0;

    /// <summary>Whether tapping it leads anywhere. False for skins; see
    /// <see cref="UnlockCategory.Browsable"/>.</summary>
    public bool IsBrowsable => Category.Browsable && !IsUnknown;

    /// <summary>Said on the row rather than after a tap that does nothing.</summary>
    public string? Aside =>
        Category.Browsable ? null : "count only — fourteen thousand names is a download of its own";

    public bool HasAside => Aside is not null;
}

/// <summary>
/// One entry in a browsed category.
/// </summary>
public sealed partial class UnlockRow(UnlockEntry entry) : ObservableObject
{
    public string Name => entry.Name;

    public bool Owned => entry.Owned;

    public bool IsLocked => !entry.Owned;

    /// <summary>
    /// How far a locked entry is faded.
    ///
    /// Applied to each colour rather than to the cell's Opacity, and that is a performance
    /// decision, not a visual one. An Opacity below one puts the whole cell on its own render
    /// layer; six hundred of them is what made this grid stutter. An alpha on a paint colour
    /// costs nothing.
    /// </summary>
    private const float Locked = 0.4f;

    /// <summary>
    /// The dye's actual colour, faded when it is not owned.
    ///
    /// A Color rather than the "#RRGGBB" string the service produces. XAML converts a string to
    /// a Color for a literal attribute but not through a binding, so binding the string painted
    /// every one of the six hundred dyes the same red — the conversion failed silently and each
    /// swatch fell back to the same value.
    ///
    /// Worked out once here rather than in the getter, and that is the other half of what made
    /// the grid stutter. A computed getter runs on every rebind, and a CollectionView rebinds
    /// every cell it recycles: parsing a hex string and allocating two Colors, twenty-odd times
    /// a frame, all the way down six hundred dyes.
    /// </summary>
    public Color Swatch { get; } =
        Dim(entry.Swatch is { } hex ? Color.FromArgb(hex) : Colors.Transparent, entry.Owned);

    /// <summary>
    /// The frame round a swatch, in the rarity colours the game already teaches.
    ///
    /// A frame rather than a word because the tile is the size of a thumbnail: a rarity spelled
    /// out under every one of six hundred swatches would be a wall of text over a wall of
    /// colour, and the colour of a frame is read without being looked at.
    /// </summary>
    public Color Frame { get; } = Dim(Theme.DyeRarityColor(entry.Rarity), entry.Owned);

    /// <summary>The name under a swatch, faded with it.</summary>
    public Color Ink { get; } = Dim(Theme.MistColor, entry.Owned);

    /// <summary>"Rare", for the list rows that have room to say it.</summary>
    public string? Rarity => entry.Rarity;


    private static Color Dim(Color colour, bool owned) =>
        owned ? colour : colour.WithAlpha(Locked);
}

/// <summary>
/// Four swatches, handed to the list as one item.
///
/// A GridItemsLayout puts forty tiles on screen and each is an item view of its own; the same
/// page's plain list of 489 titles shows thirteen and scrolls at 10ms a frame while the grid
/// took 85. Grouping the tiles into rows puts ten item views on screen instead of forty and
/// uses the linear path that was already fast here.
///
/// The last row is short more often than not, so every cell is nullable and says whether it is
/// there. Padding the list with blanks would put four tappable nothings at the end.
/// </summary>
public sealed class TileRow(IReadOnlyList<UnlockRow> cells)
{
    public UnlockRow? A { get; } = At(cells, 0);
    public UnlockRow? B { get; } = At(cells, 1);
    public UnlockRow? C { get; } = At(cells, 2);
    public UnlockRow? D { get; } = At(cells, 3);

    public bool HasA => A is not null;
    public bool HasB => B is not null;
    public bool HasC => C is not null;
    public bool HasD => D is not null;

    private static UnlockRow? At(IReadOnlyList<UnlockRow> cells, int index) =>
        index < cells.Count ? cells[index] : null;
}

/// <summary>
/// One heading in the dye grid and the rows of swatches beneath it.
///
/// A List rather than a wrapper with a collection inside, because that is what MAUI's grouping
/// wants: the group object must itself be the sequence of items.
/// </summary>
public sealed class TileGroup(UnlockGroup group, IEnumerable<TileRow> rows) : List<TileRow>(rows)
{
    public string Heading { get; } = group.Heading;

    /// <summary>How many swatches are under this heading.</summary>
    public string Line { get; } = group.Line;

    /// <summary>
    /// The heading in its own rarity's colour, so it reads as the same fact the frames below it
    /// carry. The locked group takes the dim brass every other quiet heading on this screen uses
    /// — it is not a rarity and should not borrow one's colour.
    /// </summary>
    public Color Tint { get; } =
        group.Rarity == Unlocks.Locked
            ? Color.FromArgb("#6E5A2E")
            : Theme.DyeRarityColor(group.Rarity);
}

/// <summary>
/// Everything the account has unlocked: dyes, skins, minis, outfits, mount skins and the rest.
///
/// Two views in one screen. The overview counts all twelve categories, which is cheap — ids
/// only, one request each side. Opening one fetches its names, which is not, so it happens for
/// the one category asked for and is then kept.
/// </summary>
public sealed partial class UnlocksViewModel(UnlockService unlocks, ApiKeySession session, INavigator navigator)
    : BaseViewModel
{
    public ObservableCollection<UnlockCategoryRow> Categories { get; } = [];

    /// <summary>
    /// Drives the pull-to-refresh spinner, and nothing else does.
    ///
    /// Deliberately not <see cref="BaseViewModel.IsBusy"/>, which is what this was bound to
    /// first. A RefreshView fires its command whenever IsRefreshing becomes true, including
    /// when code sets it — so opening a category set IsBusy, which started a refresh, which
    /// emptied the list and cancelled the open. Tapping a category simply cleared the screen.
    ///
    /// Only ever set to false from here. The pull sets it true itself; setting it true in code
    /// would fire the command that sets it true, which is the same trap one turn further on.
    /// </summary>
    [ObservableProperty] private bool _isRefreshing;

    protected override string DefaultEmptyMessage => "Pull down to read your unlocks.";

    [RelayCommand]
    public Task RefreshAsync() => RunAsync(async ct =>
    {
        try
        {
            unlocks.Forget();

            Categories.Clear();

            await session.EnsureRestoredAsync(ct);

            if (!session.IsAuthenticated)
            {
                Notice = "Add your API key under Settings to see your unlocks.";
                return;
            }

            if (!unlocks.CanRead)
            {
                Notice = "Your API key is missing the 'unlocks' permission. " +
                         "Settings shows which permissions this key carries.";
                return;
            }

            Notice = null;

            foreach (var tally in await unlocks.TalliesAsync(ct))
                Categories.Add(new UnlockCategoryRow(tally));
        }
        finally
        {
            // Whatever happened, the spinner stops. An early return above is the ordinary case,
            // not the exception: no key at all is the first thing this screen meets.
            IsRefreshing = false;
        }
    });

    /// <summary>
    /// Opens one category.
    ///
    /// A real navigation rather than a second state on this screen. Shell then gives the pushed
    /// page the back arrow in place of the hamburger, and Android's own back gesture works
    /// without being taught to — which is how every other drill-in in the app behaves, and was
    /// the one thing this screen did differently. Nothing is re-fetched by pushing:
    /// <see cref="UnlockService"/> keeps what it has already read.
    /// </summary>
    [RelayCommand]
    public Task OpenAsync(UnlockCategoryRow? row) =>
        row is not { IsBrowsable: true }
            ? Task.CompletedTask
            : navigator.GoToAsync($"unlockcategory?kind={row.Category.Kind}");
}
