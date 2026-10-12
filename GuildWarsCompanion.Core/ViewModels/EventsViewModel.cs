using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GuildWarsCompanion.Gw2;
using GuildWarsCompanion.Host;
using GuildWarsCompanion.Services;

namespace GuildWarsCompanion.ViewModels;

public sealed partial class EventsViewModel(
    EventService events, MetaSource metas, MetaReminderStore reminders, ApiKeySession session, MapViewModel map,
    INavigator navigator) : BaseViewModel
{
    [ObservableProperty] private string? _footnote;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasCharacter))] private string? _forCharacter;

    public bool HasCharacter => ForCharacter is not null;

    public ObservableCollection<EventRow> Bosses { get; } = [];

    /// <summary>Map metas, what is on first; see <see cref="MetaSchedule"/>.</summary>
    public ObservableCollection<MetaRow> Metas { get; } = [];

    /// <summary>Where the meta times come from, or why there are none.</summary>
    [ObservableProperty] private string? _metaNote;

    protected override string DefaultEmptyMessage => L.T("Pull down to load the timetable.");

    [RelayCommand]
    public Task RefreshAsync() => RunAsync(async ct =>
    {
        Bosses.Clear();

        // The schedule needs no key; the session only decides whether "done today" is known.
        await session.EnsureRestoredAsync(ct);

        var view = await events.GetUpcomingAsync(ct);

        // Naming the character makes the level marks explainable; without one the timetable
        // is simply unfiltered, which is also fine.
        ForCharacter = view.For is null
            ? null
            : L.F("Levels shown against {0}, level {1}.", view.For.Name, view.For.Level);

        foreach (var row in view.Bosses)
            Bosses.Add(row);

        await LoadMetasAsync(ct);

        Footnote = session.HasPermission(Gw2Permissions.Progression)
            ? L.T("Times are local. Drakkar and the Mists and Monsters titans count for the daily but are not on this timetable.")
            : L.T("Times are local. Add a key with the 'progression' permission to see which bosses you have already killed today.");
    });

    /// <summary>
    /// Opens the map a boss is fought on.
    ///
    /// The timetable already knows where every boss is — it says so on every row — so making
    /// somebody read "Frostgorge Sound", walk to the map screen and type it back in would be
    /// asking them to repeat what the app just told them. The map opens pinned, so this works
    /// with no game running, which is most of the point.
    ///
    /// A location with no id does nothing rather than opening something wrong; the schedule and
    /// the table are kept in step by <c>CompassTargetTests</c>, so this is the unreachable case.
    /// </summary>
    [RelayCommand]
    public async Task ShowOnMapAsync(EventRow? row)
    {
        if (row is null || BossMaps.For(row.Location) is not { } mapId) return;

        map.Pin(mapId, row.Location);

        await navigator.GoToAsync("//map");
    }

    /// <summary>
    /// The metas, what is on now first and then by how soon. Ordered when loaded; between loads
    /// only the words move, so a row does not jump while somebody is reading it.
    /// </summary>
    private async Task LoadMetasAsync(CancellationToken ct)
    {
        Metas.Clear();

        var now = DateTimeOffset.UtcNow;

        foreach (var row in (await metas.GetAsync(ct)).Select(meta => new MetaRow(meta, now, reminders)).OrderBy(row => row.Wait))
            Metas.Add(row);

        MetaNote = metas.Problem ??
                   (Metas.Count == 0
                       ? null
                       : L.T("Map meta times from the Guild Wars 2 Wiki's event timer. Tap a map to choose " +
                             "which of its events to be reminded of; reminders come with the others, when " +
                             "they are switched on in Settings."));
    }

    /// <summary>Advances the countdowns without touching the network.</summary>
    public void Tick()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var row in Bosses)
            row.Tick(now);
        foreach (var row in Metas)
            row.Tick(now);
    }

    /// <summary>True once the boss at the top of the list has come and gone.</summary>
    public bool NeedsReorder => Bosses.Count > 0 && Bosses[0].Next < DateTimeOffset.UtcNow;
}
