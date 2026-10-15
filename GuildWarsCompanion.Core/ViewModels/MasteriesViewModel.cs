using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GuildWarsCompanion.Gw2;
using GuildWarsCompanion.Services;

namespace GuildWarsCompanion.ViewModels;

/// <summary>
/// What the account's mastery points can still buy.
/// </summary>
public sealed partial class MasteriesViewModel(MasteryService masteries, ApiKeySession session)
    : BaseViewModel
{
    /// <summary>Mastery points earned and not yet spent, across every region; null until read.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(SpendableLabel))] private int? _spendable;

    [ObservableProperty] private string? _footnote;

    public ObservableCollection<RegionPoints> Regions { get; } = [];

    public ObservableCollection<MasteryRow> Tracks { get; } = [];

    /// <summary>What the number in the masthead counts.</summary>
    public string SpendableLabel => Spendable == 1
        ? L.T("mastery point to spend")
        : L.T("mastery points to spend");

    protected override string DefaultEmptyMessage => L.T("Pull down to read your mastery tracks.");

    [RelayCommand]
    public Task RefreshAsync() => RunAsync(async ct =>
    {
        Regions.Clear();
        Tracks.Clear();
        Spendable = null;

        await session.EnsureRestoredAsync(ct);

        if (!session.IsAuthenticated)
        {
            Notice = L.T("Add your API key under Settings to see your masteries.");
            return;
        }

        Notice = null;

        var view = await masteries.GetAsync(ct);

        foreach (var region in view.Regions)
            Regions.Add(region);

        foreach (var track in view.Tracks)
            Tracks.Add(track);

        Spendable = view.HasAny ? view.Regions.Sum(region => region.Available) : null;
        Footnote = view.Note;

        if (!view.HasAny) Notice = view.Note;
    });
}
