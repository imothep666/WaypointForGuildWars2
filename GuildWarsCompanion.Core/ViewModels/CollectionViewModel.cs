using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GuildWarsCompanion.Host;
using GuildWarsCompanion.Services;

namespace GuildWarsCompanion.ViewModels;

/// <summary>
/// One collection achievement's checklist, opened from the achievements list.
/// </summary>
public sealed partial class CollectionViewModel(AchievementService achievements, INavigator navigator, ISystemActions system) : BaseViewModel, IQueryTarget
{
    [ObservableProperty] private string? _title;
    [ObservableProperty] private string? _requirement;
    [ObservableProperty] private string? _summary;
    [ObservableProperty] private string? _footnote;

    public ObservableCollection<CollectionRow> Rows { get; } = [];

    private int _id;

    public int AchievementId
    {
        get => _id;
        set
        {
            _id = value;
            _ = LoadAsync();
        }
    }

    public void ApplyQuery(IDictionary<string, object> query)
    {
        if (RouteQuery.Int(query, "id") is { } id) AchievementId = id;
    }

    protected override string DefaultEmptyMessage => L.T("Nothing to list for this achievement.");

    [RelayCommand]
    public Task LoadAsync() => RunAsync(async ct =>
    {
        Rows.Clear();

        var view = await achievements.GetCollectionAsync(_id, ct);

        if (view is null)
        {
            Notice = L.T("This achievement has no checklist — it counts progress rather than " +
                         "listing what to collect.");
            return;
        }

        Title = view.Name;
        Requirement = view.Requirement;
        Summary = view.Summary;
        Footnote = view.Note;

        foreach (var row in view.Rows)
            Rows.Add(row);
    });

    /// <summary>
    /// Opens an entry. Items have a page in the app worth landing on first — what it is, where
    /// yours are, what it crafts into — and it carries its own wiki button. A skin, a miniature
    /// or a map step has none of that here, so those go straight to the wiki, which is where
    /// the only remaining question — where do I get it — is answered.
    /// </summary>
    [RelayCommand]
    public async Task OpenAsync(CollectionRow? row)
    {
        if (row is null) return;

        if (row.HasItemPage)
        {
            await navigator.GoToAsync($"item?id={row.ItemId}");
            return;
        }

        try
        {
            await system.OpenLinkAsync(row.WikiUrl);
        }
        catch (Exception ex)
        {
            // A phone with no browser is unlikely, but silence would look like a dead row.
            ErrorMessage = L.F("Could not open the wiki: {0}", ex.Message);
        }
    }
}
