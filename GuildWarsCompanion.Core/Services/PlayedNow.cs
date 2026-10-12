namespace GuildWarsCompanion.Services;

/// <summary>
/// What the PC bridge can say about who is playing, which is one of three things and not two.
///
/// The middle state is the whole reason this is a type rather than a string. "No name" used to
/// carry both "no bridge, no network, PC asleep" and "the bridge looked and nobody is playing",
/// and collapsing those let the playtime guess speak for a game that had demonstrably closed:
/// the brass bead went out, correctly, while the row underneath it still read "playing now".
/// </summary>
/// <param name="Answered">
/// Whether the bridge managed to look at all. False is ignorance and lets the weaker sources
/// through; true is an answer, and outranks them even when the answer is nobody.
/// </param>
/// <param name="Name">The character the game is rendering, when there is one.</param>
public readonly record struct LiveReading(bool Answered, string? Name)
{
    /// <summary>
    /// Nothing could be established. Deliberately the default value, so anyone with no bridge
    /// in the picture gets the right answer without having to know this type exists.
    /// </summary>
    public static readonly LiveReading Unknown = default;

    /// <summary>
    /// The bridge looked and no character is being played — the game is closed, or it is
    /// sitting on the login screen. A statement of absence, not a failure to answer, and that
    /// is the difference that keeps a stale guess quiet.
    /// </summary>
    public static readonly LiveReading NobodyPlaying = new(true, null);

    public static LiveReading Playing(string name) => new(true, name);
}

/// <summary>
/// Which character is being played, and how sure the app is about it.
///
/// There are three sources here and they are not equal, which is the whole reason this is its
/// own file rather than a line inside the roster:
///
/// <list type="number">
/// <item>The PC bridge reading MumbleLink. The game names the character it is rendering, so
/// this is a <em>fact</em>, and it is available the instant somebody logs in.</item>
/// <item>A playtime figure that grew between two reads of the API. An <em>inference</em>, and a
/// slow one — it needs two refreshes far enough apart for the number to move.</item>
/// <item>A stored detection from some earlier session. History.</item>
/// </list>
///
/// They are tried in that order, and only that order. The trap is the middle one outliving the
/// first: playtime keeps growing for a minute or two after somebody logs out, so an inference
/// that overrode a live reading would keep insisting a character is being played by a person who
/// has closed the game.
/// </summary>
public static class PlayedNow
{
    /// <summary>
    /// How long a stored detection still counts as "just now" rather than a duration.
    ///
    /// Forty-five minutes, carried over unchanged from the inference this replaced. It is
    /// generous because the thing it is smoothing over is coarse: a playtime figure only moves
    /// between API reads, so the moment of detection is already approximate.
    /// </summary>
    public static readonly TimeSpan Recent = TimeSpan.FromMinutes(45);

    /// <summary>
    /// The phrase for one character's row, or null when there is nothing to say about it.
    /// </summary>
    /// <param name="live">
    /// What the game says, via the bridge. <see cref="LiveReading.Unknown"/> — no bridge, not on
    /// the same network, PC asleep — is ignorance and falls through to the guess; it never means
    /// "this character is not being played". <see cref="LiveReading.NobodyPlaying"/> does mean
    /// that, about everybody, and is the one thing the guess is not allowed to talk over.
    /// </param>
    /// <param name="grew">The character whose playtime rose during this refresh, if any.</param>
    /// <param name="detected">The stored detection: who, and when it was noticed.</param>
    public static string? Describe(
        string name,
        LiveReading live,
        string? grew,
        (string Name, DateTimeOffset At)? detected,
        DateTimeOffset now)
    {
        // The game itself said so. Nothing below can outrank that, and nothing below is allowed
        // to contradict it either — see the next branch.
        if (Matches(name, live.Name)) return L.T("playing now");

        // The game has been asked, so the guess has nothing to add and every way to be wrong.
        // Either somebody else is demonstrably at the keyboard, in which case no other row may
        // claim to be live, or nobody is — and the character logged out ninety seconds ago still
        // has playtime arriving from the API, which is exactly the reading that would otherwise
        // put "playing now" beside a closed game.
        if (live.Answered) return Stored(name, detected, now);

        if (Matches(name, grew)) return L.T("playing now");

        return Stored(name, detected, now);
    }

    private static string? Stored(string name, (string Name, DateTimeOffset At)? detected, DateTimeOffset now)
    {
        if (detected is not { } was || !Matches(name, was.Name)) return null;

        var elapsed = now - was.At;

        return elapsed < Recent
            ? L.T("played just now")
            : elapsed < TimeSpan.FromHours(24)
                ? L.F("last played {0}h ago", (int)elapsed.TotalHours)
                : L.F("last played {0}d ago", (int)elapsed.TotalDays);
    }

    /// <summary>
    /// Character names are unique per account and the API returns them exactly as typed, so an
    /// ordinal comparison is right — but MumbleLink's copy has been through a JSON round trip
    /// and a fixed-width buffer, so it is trimmed before being trusted.
    /// </summary>
    public static bool Matches(string name, string? candidate) =>
        candidate is not null &&
        string.Equals(name, candidate.Trim(), StringComparison.Ordinal);
}
