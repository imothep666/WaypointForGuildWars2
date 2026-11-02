using System.Collections.ObjectModel;
using GuildWarsCompanion.Services;

namespace GuildWarsCompanion.ViewModels;

/// <summary>
/// The year's festivals: the one on, then the rest by when each comes next. Nothing is read from
/// the network, so the page is the same offline; see <see cref="Festivals.Calendar"/>.
/// </summary>
public sealed class FestivalsViewModel(TestEvidence evidence) : BaseViewModel
{
    public ObservableCollection<FestivalEntry> Entries { get; } = [];

    public void Refresh()
    {
        Entries.Clear();

        foreach (var entry in Festivals.Calendar(Festivals.Announced, DateTimeOffset.UtcNow)) Entries.Add(entry);

        evidence.Saw("festival-calendar");
    }
}
