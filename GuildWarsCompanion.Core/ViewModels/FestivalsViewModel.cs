using System.Collections.ObjectModel;
using GuildWarsCompanion.Services;

namespace GuildWarsCompanion.ViewModels;

/// <summary>
/// The year's festivals: the one on, then the rest by when each comes next; see
/// <see cref="Festivals.Calendar"/>. Drawn at once from the dates kept, and again when the wiki's
/// pages, read at most once a day, have brought news. Offline the kept dates are the page.
/// </summary>
public sealed class FestivalsViewModel(TestEvidence evidence, FestivalSource dates) : BaseViewModel
{
    public ObservableCollection<FestivalEntry> Entries { get; } = [];

    public async Task RefreshAsync()
    {
        var held = dates.Held();
        Show(held);

        var read = await dates.GetAsync();
        if (!ReferenceEquals(read, held)) Show(read);

        evidence.Saw("festival-calendar");
    }

    private void Show(IReadOnlyList<FestivalPeriod> periods)
    {
        Entries.Clear();

        foreach (var entry in Festivals.Calendar(periods, DateTimeOffset.UtcNow)) Entries.Add(entry);
    }
}
