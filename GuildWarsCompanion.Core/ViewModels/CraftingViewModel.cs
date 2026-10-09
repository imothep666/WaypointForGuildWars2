using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GuildWarsCompanion.Gw2;
using GuildWarsCompanion.Services;

namespace GuildWarsCompanion.ViewModels;

public sealed partial class CraftingViewModel(CraftingService crafting, ApiKeySession session)
    : BaseViewModel
{
    [ObservableProperty] private string? _suggestionHeading;
    [ObservableProperty] private string? _note;

    /// <summary>Which discipline the advice is about; null until one is chosen.</summary>
    private string? _chosen;

    public ObservableCollection<DisciplineRow> Disciplines { get; } = [];
    public ObservableCollection<LevellingRow> Suggestions { get; } = [];

    /// <summary>What the account could make this minute, from what it already holds.</summary>
    public ObservableCollection<MakeableItemRow> Makeable { get; } = [];

    [ObservableProperty] private string? _makeableSummary;
    [ObservableProperty] private string? _makeableNote;
    [ObservableProperty] private bool _isReadingStorage;

    public bool HasMakeable => Makeable.Count > 0;

    /// <summary>
    /// Hidden once the answer is on screen. Reading every character's bags is the expensive
    /// half, exactly as it is on the wallet, so it is asked for rather than done on arrival.
    /// </summary>
    public bool CanReadStorage => MakeableSummary is null && !IsReadingStorage;

    protected override string DefaultEmptyMessage => "Pull down to read your disciplines.";

    [RelayCommand]
    public Task RefreshAsync() => RunAsync(async ct =>
    {
        await session.EnsureRestoredAsync(ct);

        if (!session.IsAuthenticated)
        {
            Notice = "Add your API key under Settings to see your crafting.";
            return;
        }

        if (!session.HasPermission(Gw2Permissions.Characters))
        {
            Notice = "This key has no 'characters' permission. Create a new key that includes it.";
            return;
        }

        Notice = null;

        var view = await crafting.GetAsync(_chosen, ct);

        Disciplines.Clear();
        foreach (var row in view.Disciplines)
            Disciplines.Add(row);

        Suggestions.Clear();
        foreach (var row in view.Suggestions)
            Suggestions.Add(row);

        SuggestionHeading = view.SuggestionHeading;
        Note = view.Note;
    });

    /// <summary>Works out what can be made outright from what the account is holding.</summary>
    [RelayCommand]
    public async Task ReadStorageAsync()
    {
        if (IsReadingStorage) return;

        IsReadingStorage = true;
        OnPropertyChanged(nameof(CanReadStorage));

        try
        {
            var view = await crafting.GetMakeableAsync();

            Makeable.Clear();
            foreach (var row in view.Rows)
                Makeable.Add(row);

            MakeableSummary = view.Summary.Length > 0 ? view.Summary : null;
            MakeableNote = view.Note;
        }
        catch (Gw2ApiException ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsReadingStorage = false;
            OnPropertyChanged(nameof(CanReadStorage));
            OnPropertyChanged(nameof(HasMakeable));
        }
    }

    /// <summary>Switches the advice to another discipline.</summary>
    public Task ChooseAsync(DisciplineRow row)
    {
        _chosen = row.Name;
        return RefreshAsync();
    }
}
