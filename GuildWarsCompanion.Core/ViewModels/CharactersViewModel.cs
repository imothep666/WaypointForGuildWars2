using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using GuildWarsCompanion.Gw2;
using GuildWarsCompanion.Host;
using GuildWarsCompanion.Services;

namespace GuildWarsCompanion.ViewModels;

public sealed partial class CharactersViewModel(
    CharacterService characters, ApiKeySession session, BridgeClient bridge,
    INavigator navigator)
    : BaseViewModel
{
    public ObservableCollection<CharacterRow> Characters { get; } = [];

    protected override string DefaultEmptyMessage => L.T("Pull down to load your characters.");

    [RelayCommand]
    public Task RefreshAsync() => RunAsync(async ct =>
    {
        Characters.Clear();

        // A stored key is only known to be good once the restore has answered.
        await session.EnsureRestoredAsync(ct);

        if (!session.IsAuthenticated)
        {
            Notice = L.T("Add your API key under Settings to see your characters.");
            return;
        }

        if (!session.HasPermission(Gw2Permissions.Characters))
        {
            Notice = L.T("This key has no 'characters' permission. Create a new key that includes it.");
            return;
        }

        Notice = null;

        // Asked before the roster so the answer is in hand when the rows are built. A bridge
        // that is unreachable answers Unknown and the roster falls back to its own inference;
        // one that answers "nobody" is believed, which is not the same thing at all.
        var live = await bridge.PlayingNowAsync(ct);

        var roster = await characters.GetRosterAsync(live, ct);

        foreach (var row in roster.Value)
            Characters.Add(row);

        if (roster.FetchedAt is { } taken)
            Notice = Stale.Describe(L.T("your characters"), taken, DateTimeOffset.UtcNow);
    });

    /// <summary>Opens one character. The name is the API's own identifier for it.</summary>
    [RelayCommand]
    public Task OpenAsync(CharacterRow? row) =>
        row is null
            ? Task.CompletedTask
            : navigator.GoToAsync($"character?name={Uri.EscapeDataString(row.Name)}");
}
