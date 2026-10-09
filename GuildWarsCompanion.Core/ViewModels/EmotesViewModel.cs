using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GuildWarsCompanion.Host;
using GuildWarsCompanion.Services;

namespace GuildWarsCompanion.ViewModels;

/// <summary>
/// Every emote command, searchable, with the account's unlocks marked where the API knows them.
///
/// Built to be used mid-game with one hand: the search runs against the local catalogue on every
/// keystroke, and a tap puts the command on a clipboard — the PC's where there is a bridge, this
/// phone's otherwise — rather than opening anything.
/// </summary>
public sealed partial class EmotesViewModel(EmoteService emotes, ApiKeySession session, BridgeClient bridge, ISystemActions system)
    : BaseViewModel
{
    [ObservableProperty] private string _search = string.Empty;
    [ObservableProperty] private string? _summary;
    [ObservableProperty] private string? _footnote;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCopied))]
    private string? _copied;

    public bool HasCopied => !string.IsNullOrEmpty(Copied);

    /// <summary>
    /// Forgets where the last emote went, on the way back into the page.
    ///
    /// This view model is a singleton and the page is transient, so the sentence outlived the
    /// screen that earned it: opening Emotes showed "Sent /bless to GKS-0432" from a previous
    /// visit, naming a paste that was no longer on any clipboard. Deliberately unlike
    /// <see cref="SettingsViewModel.CatalogueOutcome"/>, which is kept precisely because it
    /// reports work that finished and persisted — this reports a clipboard, and a clipboard is
    /// the one thing here that somebody else can change without telling the app.
    /// </summary>
    public void ClearOutcome() => Copied = null;

    public ObservableCollection<EmoteRow> Emotes { get; } = [];

    protected override string DefaultEmptyMessage => "No emote goes by that name.";

    /// <summary>Filtering is local, so it happens as the letters arrive.</summary>
    partial void OnSearchChanged(string value) => Show();

    [RelayCommand]
    public Task RefreshAsync() => RunAsync(async ct =>
    {
        await session.EnsureRestoredAsync(ct);

        await emotes.RefreshAsync(ct);

        Show();

        // Not a blocker: the list is complete and correct without a key. Only the 14 verdicts
        // are missing, and the summary already says so.
        if (!session.IsAuthenticated)
            Notice = "Add your API key under Settings to see which emotes you have unlocked.";
    });

    private void Show()
    {
        var view = emotes.View(Search);

        Emotes.Clear();
        foreach (var row in view.Emotes)
            Emotes.Add(row);

        Summary = view.Summary;
        Footnote = view.Note;

        if (view.FetchedAt is { } taken)
            Notice = Stale.Describe("your unlocks", taken, DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// One tap, and the command lands where it can actually be used.
    ///
    /// Where a bridge is set up that is the PC's clipboard and only the PC's. The game is over
    /// there, so also writing this phone's clipboard would spend a shared resource — whatever
    /// the user had on it — on a value nobody is going to paste here, and Android pops its own
    /// clipboard overlay for every write, which makes it the second piece of feedback for one
    /// tap as well.
    ///
    /// This phone's clipboard is the fallback rather than the guarantee it used to be. With no
    /// bridge it is the only destination there is and the behaviour is exactly what it always
    /// was, which is what keeps the bridge additive: someone who never sets one up sees no
    /// change. With a bridge that did not answer it beats the tap having done nothing.
    ///
    /// Still deliberately one gesture. The cost is that sharing a command out of this phone —
    /// to a chat, say — no longer happens by accident once a bridge is paired.
    /// </summary>
    [RelayCommand]
    public async Task CopyAsync(EmoteRow? row)
    {
        if (row is null) return;

        // Asked rather than read, because the very first call after an upgrade is what moves an
        // older single pairing into the list — and a pairing that has not been noticed yet reads
        // exactly like no pairing at all.
        if (!await bridge.IsSetUpAsync())
        {
            await system.CopyAsync(row.Command);

            Copied = $"Copied {row.Command}";
            return;
        }

        // Said before the network is asked, so the row never looks unresponsive while a PC that
        // is asleep runs out its timeout.
        Copied = $"Sending {row.Command}…";

        // Done in the game when the PC has the Blish HUD module running, copied there when not;
        // the bridge decides and the answer says which.
        var sent = await bridge.EmoteAsync(row.Command);

        if (sent.IsSent)
        {
            // Named, not "the PC". With several paired, a paste that went to the laptop when
            // the game is on the desktop has to be visibly the wrong machine.
            Copied = sent.InGame
                ? $"{row.Command} · done in game on {sent.Pc}"
                : $"Sent {row.Command} to {sent.Pc}";
            return;
        }

        // No PC could take it, so this phone is better than nowhere. Which clipboard it ended
        // up on is said outright, because that is the one thing the user cannot see.
        await system.CopyAsync(row.Command);

        Copied = $"{row.Command} · {sent.Text} · copied here";
    }
}
