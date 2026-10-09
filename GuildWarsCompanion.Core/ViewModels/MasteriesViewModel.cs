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
    [ObservableProperty] private string? _summary;
    [ObservableProperty] private string? _footnote;

    public ObservableCollection<RegionPoints> Regions { get; } = [];

    public ObservableCollection<MasteryRow> Tracks { get; } = [];

    protected override string DefaultEmptyMessage => "Pull down to read your mastery tracks.";

    [RelayCommand]
    public Task RefreshAsync() => RunAsync(async ct =>
    {
        Regions.Clear();
        Tracks.Clear();

        await session.EnsureRestoredAsync(ct);

        if (!session.IsAuthenticated)
        {
            Notice = "Add your API key under Settings to see your masteries.";
            return;
        }

        Notice = null;

        var view = await masteries.GetAsync(ct);

        foreach (var region in view.Regions)
            Regions.Add(region);

        foreach (var track in view.Tracks)
            Tracks.Add(track);

        Summary = view.Summary;
        Footnote = view.Note;

        if (!view.HasAny) Notice = view.Note;
    });
}
