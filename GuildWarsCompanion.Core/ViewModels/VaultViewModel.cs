using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GuildWarsCompanion.Gw2;
using GuildWarsCompanion.Services;

namespace GuildWarsCompanion.ViewModels;

public sealed partial class VaultViewModel(VaultService vault, ApiKeySession session) : BaseViewModel
{
    [ObservableProperty] private string? _seasonLine;
    [ObservableProperty] private string _acclaimText = string.Empty;
    [ObservableProperty] private bool _hasAcclaim;

    public ObservableCollection<VaultSection> Sections { get; } = [];

    /// <summary>The shop, by the game's three places; see <see cref="VaultShop"/>.</summary>
    public ObservableCollection<VaultShopSection> Shop { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasShopNote))]
    private string? _shopNote;

    public bool HasShopNote => ShopNote is not null;

    /// <summary>What there is to do, and what there is to spend it on: two lists, not one scroll.</summary>
    public ObservableCollection<PageTab> Tabs { get; } = [new(L.T("Objectives"), 0) { IsSelected = true }, new(L.T("Shop"), 1)];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsObjectives))]
    [NotifyPropertyChangedFor(nameof(IsShop))]
    private int _selectedTab;

    public bool IsObjectives => SelectedTab == 0;
    public bool IsShop => SelectedTab == 1;

    public void Select(PageTab tab)
    {
        SelectedTab = tab.Index;

        foreach (var candidate in Tabs) candidate.IsSelected = candidate.Index == tab.Index;
    }

    protected override string DefaultEmptyMessage => L.T("Pull down to load your objectives.");

    [RelayCommand]
    public Task RefreshAsync() => RunAsync(async ct =>
    {
        Sections.Clear();
        Shop.Clear();

        // A stored key is only known to be good once the restore has answered.
        await session.EnsureRestoredAsync(ct);

        if (!session.IsAuthenticated)
        {
            Notice = L.T("Add your API key under Settings to see the Wizard's Vault.");
            return;
        }

        if (!session.HasPermission(Gw2Permissions.Progression))
        {
            Notice = L.T("This key has no 'progression' permission. Create a new key that includes it.");
            return;
        }

        Notice = null;

        var view = await vault.GetAsync(ct);
        SeasonLine = view.SeasonLine;
        AcclaimText = view.AcclaimText;
        HasAcclaim = view.HasAcclaim;

        foreach (var section in view.Sections)
            Sections.Add(section);

        foreach (var section in view.Shop)
            Shop.Add(section);

        ShopNote = view.ShopNote;
    });
}
