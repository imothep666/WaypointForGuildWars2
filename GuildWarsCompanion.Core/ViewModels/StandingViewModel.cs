using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GuildWarsCompanion.Gw2;
using GuildWarsCompanion.Services;

namespace GuildWarsCompanion.ViewModels;

public sealed partial class StandingViewModel(
    StandingService standing, HomeService home, ApiKeySession session)
    : BaseViewModel
{
    [ObservableProperty] private string? _footnote;

    /// <summary>
    /// Base magic find from luck, in percent: the one figure on this screen a player works
    /// towards, so it leads it. Null when the key cannot read luck.
    /// </summary>
    [ObservableProperty] private int? _magicFind;

    public ObservableCollection<StandingRow> Rows { get; } = [];

    /// <summary>What the account owns, which the account read has always carried.</summary>
    public ObservableCollection<Owned> Expansions { get; } = [];

    [ObservableProperty] private string? _owned;

    /// <summary>The home instance: what is in it, and what is still to get.</summary>
    public ObservableCollection<HomeRow> Nodes { get; } = [];

    public ObservableCollection<HomeRow> Cats { get; } = [];

    [ObservableProperty] private string? _nodeSummary;
    [ObservableProperty] private string? _catSummary;
    [ObservableProperty] private string? _homeNote;

    public bool HasHome => Nodes.Count > 0;

    public bool HasExpansions => Expansions.Count > 0;

    protected override string DefaultEmptyMessage => L.T("Pull down to read your account.");

    [RelayCommand]
    public Task RefreshAsync() => RunAsync(async ct =>
    {
        Rows.Clear();
        Expansions.Clear();
        Nodes.Clear();
        Cats.Clear();
        MagicFind = null;

        await session.EnsureRestoredAsync(ct);

        if (!session.IsAuthenticated)
        {
            Notice = L.T("Add your API key under Settings to see your account.");
            return;
        }

        if (!session.HasPermission(Gw2Permissions.Account))
        {
            Notice = L.T("This key has no 'account' permission. Create a new key that includes it.");
            return;
        }

        Notice = null;

        var view = await standing.GetAsync(ct);

        foreach (var row in view.Rows)
            Rows.Add(row);

        foreach (var expansion in view.Expansions)
            Expansions.Add(expansion);

        Owned = view.OwnedSummary;
        MagicFind = view.MagicFind;
        OnPropertyChanged(nameof(HasExpansions));

        await LoadHomeAsync(ct);

        Footnote = view.HasAny ? view.Note : null;

        // The account still has something to say even with nothing to report.
        if (!view.HasAny)
            Notice = L.T("Nothing to report: no luck consumed, no fractals, no WvW rank.");
    });

    /// <summary>
    /// The home instance, loaded alongside. Kept from taking the screen down with it: the
    /// standing numbers above are the reason people open this page.
    /// </summary>
    private async Task LoadHomeAsync(CancellationToken ct)
    {
        try
        {
            var view = await home.GetAsync(ct);

            foreach (var node in view.Nodes)
                Nodes.Add(node);

            foreach (var cat in view.Cats)
                Cats.Add(cat);

            NodeSummary = view.NodeSummary;
            CatSummary = view.CatSummary;
            HomeNote = view.Note;
        }
        catch (Gw2ApiException)
        {
            // No home instance section rather than no page.
        }
        finally
        {
            OnPropertyChanged(nameof(HasHome));
        }
    }
}
