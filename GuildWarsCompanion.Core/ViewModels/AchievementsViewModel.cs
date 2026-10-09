using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GuildWarsCompanion.Gw2;
using GuildWarsCompanion.Host;
using GuildWarsCompanion.Services;

namespace GuildWarsCompanion.ViewModels;

public sealed partial class AchievementsViewModel(
    AchievementService achievements, ApiKeySession session,
    INavigator navigator) : BaseViewModel
{
    [ObservableProperty] private string? _summary;

    public ObservableCollection<AchievementRow> Rows { get; } = [];

    protected override string DefaultEmptyMessage => "Pull down to see what you are closest to.";

    [RelayCommand]
    public Task RefreshAsync() => RunAsync(async ct =>
    {
        Rows.Clear();
        Summary = null;

        await session.EnsureRestoredAsync(ct);

        if (!session.IsAuthenticated)
        {
            Notice = "Add your API key under Settings to see your achievements.";
            return;
        }

        if (!session.HasPermission(Gw2Permissions.Progression))
        {
            Notice = "This key has no 'progression' permission. Create a new key that includes it.";
            return;
        }

        Notice = null;

        var view = await achievements.GetAsync(ct);

        foreach (var row in view.Rows)
            Rows.Add(row);

        Summary = view.Summary;

        // The summary is the whole answer when there is nothing part-finished to list.
        if (!view.HasAny) Notice = view.Summary;
    });

    /// <summary>
    /// Opens the checklist for a collection. Achievements that only count something have
    /// nothing to list, so those rows stay put rather than opening an empty screen.
    /// </summary>
    [RelayCommand]
    public Task OpenAsync(AchievementRow? row) =>
        row is not { HasChecklist: true }
            ? Task.CompletedTask
            : navigator.GoToAsync($"collection?id={row.Id}");
}
