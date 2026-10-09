using GuildWarsCompanion.Gw2;

namespace GuildWarsCompanion.Services;

/// <summary>One emote as the screen shows it.</summary>
public sealed record EmoteRow(Emote Emote, EmoteState State)
{
    public string Command => Emote.Command;

    public string Does => Emote.Does;

    /// <summary>
    /// Everything second-order about the emote on one line: how it behaves, and where it comes
    /// from if it did not come free. "Loops · Works in chairs · \"/Rock\" Emote Tome".
    /// </summary>
    public string Detail
    {
        get
        {
            var parts = new List<string>();

            if (Emote.Loops) parts.Add("Loops");
            if (Emote.InChairs) parts.Add("Works in chairs");
            if (Emote.Aliases.Length > 0) parts.Add(string.Join(" ", Emote.Aliases));
            if (Emote.Unlock is { } unlock) parts.Add(unlock);

            return string.Join(" · ", parts);
        }
    }

    /// <summary>
    /// Said only where there is something to say. A free emote needs no label — the absence of
    /// one is the point, since every account has it — and an unverifiable one says so in words
    /// rather than borrowing "locked".
    /// </summary>
    public string StateText => State switch
    {
        EmoteState.Unlocked => "unlocked",
        EmoteState.Locked => "locked",
        EmoteState.Untracked => "not tracked",
        _ => string.Empty
    };

    /// <summary>Brass for what can be typed right now; quiet for the rest, with the darkest
    /// edge on the rows nothing is known about.</summary>
    public Color EdgeColor => State switch
    {
        EmoteState.Free or EmoteState.Unlocked => Theme.Brass,
        EmoteState.Locked => Theme.Banked,
        _ => Theme.EmptySlot
    };
}

/// <summary>What the emotes screen shows.</summary>
public sealed record EmoteView(
    string Summary, IReadOnlyList<EmoteRow> Emotes, string Note, DateTimeOffset? FetchedAt);

/// <summary>
/// Every emote in the game, marked with what this account can do with it.
///
/// One request, and the screen is worth opening without it: the catalogue is local, so the
/// commands, what they do and which of them loop are all readable with no key and no
/// connection. The account read only ever adds verdicts to 14 rows — see
/// <see cref="Emotes"/> for why it cannot add more.
///
/// Read once, filtered many times, the same shape <see cref="StorageService"/> has: searching
/// is the thing this screen is for, and putting a network call behind a keystroke would make
/// typing the slowest part of finding /shrug.
/// </summary>
public sealed class EmoteService(Gw2Client client, ApiKeySession session, OfflineReader offline)
{
    private HashSet<string>? _owned;
    private DateTimeOffset? _fetchedAt;

    /// <summary>Reads the account's unlocks. Everything else this screen shows is local.</summary>
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        // Only "account" is needed, which every key carries by definition — but a key that has
        // not been added yet still leaves the whole list readable, just unmarked.
        if (!session.IsAuthenticated)
        {
            _owned = null;
            _fetchedAt = null;
            return;
        }

        // Wrapped rather than passed as a method group: the endpoint never answers null, and
        // the reader's signature reserves null for "there was nothing to read".
        var unlocked = await offline.ReadOptionalAsync<List<string>>(
            "account/emotes", Gw2JsonContext.Default.ListString,
            async token => await client.GetAccountEmotesAsync(token), ct);

        // An account that has unlocked nothing answers with an empty list, which is a verdict.
        // Only a read that never happened leaves the set null.
        _owned = unlocked.Value is null
            ? null
            : new HashSet<string>(unlocked.Value, StringComparer.OrdinalIgnoreCase);

        _fetchedAt = unlocked.FetchedAt;
    }

    public EmoteView View(string? search = null) =>
        new(Emotes.Summarise(_owned),
            Emotes.Matching(search, _owned)
                .Select(emote => new EmoteRow(emote, Emotes.StateOf(emote, _owned)))
                .ToList(),
            Emotes.Note,
            _fetchedAt);
}
