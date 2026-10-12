using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GuildWarsCompanion.Gw2;
using GuildWarsCompanion.Host;
using GuildWarsCompanion.Services;

namespace GuildWarsCompanion.ViewModels;

public sealed partial class MaterialsViewModel(AccountDataService data, ApiKeySession session, INavigator navigator) : BaseViewModel
{
    [ObservableProperty] private CoinPurse? _instantValue;
    [ObservableProperty] private CoinPurse? _listedValue;
    [ObservableProperty] private string? _valueNote;
    [ObservableProperty] private bool _hasValue;

    public ObservableCollection<MaterialSection> Sections { get; } = [];

    protected override string DefaultEmptyMessage => L.T("Pull down to load your material storage.");

    /// <summary>Opens one stack's item. Every list in the app hands off to the same screen.</summary>
    [RelayCommand]
    public Task OpenAsync(MaterialRow? row) =>
        row is null
            ? Task.CompletedTask
            : navigator.GoToAsync($"item?id={row.ItemId}");

    [RelayCommand]
    public Task RefreshAsync() => RunAsync(async ct =>
    {
        Sections.Clear();
        HasValue = false;

        // A stored key is only known to be good once the restore has answered.
        await session.EnsureRestoredAsync(ct);

        if (!session.IsAuthenticated)
        {
            Notice = L.T("Add your API key under Settings to see material storage.");
            return;
        }

        if (!session.HasPermission(Gw2Permissions.Inventories))
        {
            Notice = L.T("This key has no 'inventories' permission. Create a new key that includes it.");
            return;
        }

        Notice = null;

        // First run also fills the item cache (~10 requests); later runs are one request
        // for the stacks plus one or two for current prices.
        var fetched = await data.GetMaterialsAsync(ct);
        var view = fetched.Value;

        InstantValue = view.InstantValue;
        ListedValue = view.ListedValue;
        ValueNote = view.ValueNote;
        HasValue = view.HasValue;

        foreach (var section in view.Sections)
            Sections.Add(section);

        if (fetched.FetchedAt is { } taken)
            Notice = Stale.Describe(L.T("your storage"), taken, DateTimeOffset.UtcNow);
    });
}
