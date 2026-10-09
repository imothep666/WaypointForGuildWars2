using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using GuildWarsCompanion.Host;
using GuildWarsCompanion.Services;

namespace GuildWarsCompanion.ViewModels;

/// <summary>
/// Every map in the game, by name, so the map screen can be opened without a PC.
///
/// The map has always drawn where the character is standing, which means it draws nothing at all
/// when there is no game running — and that is precisely the moment somebody wants to look at
/// Frostgorge and work out where they are going. This is the way in that does not need a bridge,
/// a game, or, once the list has been read once, a connection.
///
/// <b>The search is in memory and the list is held.</b> Eight hundred maps, filtered on every
/// keystroke — see <see cref="MapCatalogue"/> for why that is one list in hand rather than a
/// query per letter.
/// </summary>
public sealed partial class MapPickerViewModel(MapCatalogue catalogue, MapViewModel map, INavigator navigator)
    : BaseViewModel
{
    /// <summary>
    /// How many matches are shown.
    ///
    /// Above the 123 open-world maps on purpose, so the browse list is never truncated — the
    /// cap is for a search like "a", which matches most of a thousand instances. The line under
    /// the box says when it has bitten.
    /// </summary>
    private const int MostShown = 150;

    public ObservableCollection<MapChoice> Results { get; } = [];

    [ObservableProperty] private string _search = string.Empty;

    [ObservableProperty] private string _resultNote = string.Empty;

    protected override string DefaultEmptyMessage => "No map by that name.";

    /// <summary>
    /// Reads the list once and shows the start of it. Called when the page appears.
    /// </summary>
    [RelayCommand]
    public Task LoadAsync() => RunAsync(async ct =>
    {
        // The catalogue reports a missing list through a note rather than an exception: this
        // screen still has a search box to draw, and "nothing stored and no connection" is a
        // sentence, not a failure. ShowAsync carries the note onto the screen.
        await catalogue.AllAsync(ct);

        await ShowAsync(ct);
    });

    /// <summary>Re-filters as the name is typed. No command and no return key: a picker that
    /// waits for a button is a picker somebody types into and then wonders about.</summary>
    partial void OnSearchChanged(string value) => _ = RunAsync(ShowAsync);

    /// <summary>Draws the chosen map, and goes to it.</summary>
    [RelayCommand]
    public async Task OpenAsync(MapChoice? choice)
    {
        if (choice is null) return;

        // Pinned before the navigation rather than after it, so the load is already running by
        // the time the map screen appears and the tiles are on their way.
        map.Pin(choice.Id, choice.Name);

        await navigator.GoToAsync("//map");
    }

    private async Task ShowAsync(CancellationToken ct)
    {
        var found = await catalogue.SearchAsync(Search, ct);

        if (ct.IsCancellationRequested) return;

        // Re-stated on every pass, because RunAsync clears both messages when it starts: without
        // this, "no list stored and no connection" disappears the moment somebody types.
        Notice = catalogue.Note;

        Results.Clear();

        foreach (var choice in found.Take(MostShown)) Results.Add(choice);

        // The empty box shows the open world, so the line has to say so — otherwise somebody
        // who knows the game counts 123 maps, thinks of Ascalonian Catacombs, and concludes it
        // is missing rather than one keystroke away.
        ResultNote = Search.Trim().Length == 0
            ? $"{found.Count} open-world maps — type a name to reach story and dungeon instances."
            : found.Count switch
            {
                0 => string.Empty,
                > MostShown => $"{found.Count} maps match — showing the first {MostShown}.",
                1 => "1 map.",
                _ => $"{found.Count} maps."
            };
    }
}
