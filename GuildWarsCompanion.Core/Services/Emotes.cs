namespace GuildWarsCompanion.Services;

/// <summary>
/// The two things about an emote worth knowing before you type it.
/// </summary>
[Flags]
public enum EmoteTraits
{
    None = 0,

    /// <summary>Holds until you move, rather than playing once. What you want for a screenshot.</summary>
    Loops = 1,

    /// <summary>Usable while sitting in an environmental chair. Novelty chairs support none of
    /// them except Victory Rock, which supports all.</summary>
    Chairs = 2
}

/// <summary>
/// Whether this account can use an emote.
///
/// <see cref="Untracked"/> is the honest fourth answer, not a rounding of "locked": the API
/// publishes 14 of the 43 unlockable emotes and says nothing whatever about the rest, so an
/// emote tome bought last week is indistinguishable from one never bought.
/// </summary>
public enum EmoteState
{
    Free,
    Unlocked,
    Locked,
    Untracked
}

/// <summary>One emote, as the game accepts it and as the chat log describes it.</summary>
/// <param name="Command">The command to type. The one to show, and the one to copy.</param>
/// <param name="Aliases">Other spellings the game accepts — searchable, but not shown first.</param>
/// <param name="Does">The emote channel's own wording, with "[Character]" dropped.</param>
/// <param name="Unlock">The item that grants it, or null when every account has it.</param>
/// <param name="ApiId">Its id in /v2/emotes, or null for the 29 that endpoint omits.</param>
public sealed record Emote(
    string Command,
    string[] Aliases,
    string Does,
    EmoteTraits Traits,
    string? Unlock,
    string? ApiId = null)
{
    public bool Loops => Traits.HasFlag(EmoteTraits.Loops);

    public bool InChairs => Traits.HasFlag(EmoteTraits.Chairs);

    /// <summary>Free on every account, so there is no unlock to ask the API about.</summary>
    public bool IsFree => Unlock is null;

    public IEnumerable<string> Commands => [Command, .. Aliases];
}

/// <summary>
/// Every emote, what it does, and whether this account has it.
///
/// The catalogue is hardcoded for the same reason <see cref="EventSchedule"/> is: the API does
/// not publish one. /v2/emotes lists 14 emotes — the rock-paper-scissors set, the shiver pair,
/// the three How to Dance tomes and a handful more — and knows nothing of the other 54. The 25
/// every account is born with have no unlock item and so no reason to appear there at all, and
/// the 29 gem-store tomes are simply missing. A screen built on /v2/emotes alone would list a
/// fifth of the emotes in the game and omit /dance.
///
/// So the table below carries all 68, and <see cref="StateOf"/> answers for each of them
/// separately rather than treating silence as a "no" — which is the whole difficulty here: the
/// obvious implementation shows a player's own /breakdance as locked.
///
/// Sourced from the wiki's emote table, which agrees with the game's /emotelist. A new tome
/// means a new row.
/// </summary>
public static class Emotes
{
    // Named so the table reads as a table. Every row states its traits, including the absence
    // of any, because a blank column is easy to misread as an oversight.
    private const EmoteTraits Plain = EmoteTraits.None;
    private const EmoteTraits Loop = EmoteTraits.Loops;
    private const EmoteTraits Chair = EmoteTraits.Chairs;

    private static readonly Emote[] Catalogue =
    [
        new("/barbecue", ["/bbq", "/grill"], "Cooks on their grill.", Loop, "\"/Barbecue\" Emote Tome"),
        new("/beckon", [], "Beckons.", Plain, null),
        new("/bless", [], "Is blessed.", Plain, "\"/bless\" Emote Tome", "Bless"),
        new("/bloodstoneboogie", ["/boogie"], "Dances like no one is watching.", Loop, "\"/BloodstoneBoogie\" Emote Tome"),
        new("/blowkiss", [], "Blows a kiss.", Plain, "\"/BlowKiss\" Emote Tome"),
        new("/bow", [], "Bows.", Plain, null),
        new("/breakdance", [], "Breakdances.", Loop, "\"/Breakdance\" Emote Tome"),
        new("/channel", [], "Channels the fire spirits.", Loop, "\"/Channel\" Emote Tome"),
        new("/cheer", [], "Cheers.", Chair, null),
        new("/cower", [], "Cowers.", Plain, null),
        new("/crabdance", [], "Dances like a crab.", Loop, "\"/CrabDance\" Emote Tome"),
        new("/crossarms", [], "Crosses their arms.", Loop | Chair, null),
        new("/cry", [], "Is crying.", Loop, null),
        new("/dance", [], "Is busting out some moves, some sweet dance moves.", Loop, null),
        new("/drink", [], "Stays hydrated.", Plain, "\"/drink\" Emote Tome"),
        new("/facepalm", ["/upset"], "Is upset.", Loop, null),
        new("/geargrind", [], "Does the Gear Grind.", Loop, "How to Dance, Volume 1", "geargrind"),
        new("/happydance", [], "Dances happily.", Loop, "\"/HappyDance\" Emote Tome"),
        new("/heroic", [], "Strikes a heroic pose.", Loop, "\"/Heroic\" Emote Tome", "Heroic"),
        new("/hiss", [], "Hisses in a vaguely threatening manner.", Loop, "\"/Hiss\" Emote Tome"),
        new("/kneel", [], "Kneels.", Loop, null),
        new("/laugh", [], "Laughs.", Chair, null),
        new("/magicjuggle", [], "Juggles with magical lights.", Loop, "\"/MagicJuggle\" Emote Tome"),
        new("/magictrick", [], "Performs a magic trick.", Plain, "\"/MagicTrick\" Emote Tome"),
        new("/masterpiece", [], "Feels that this is a masterpiece.", Plain, "\"/Masterpiece\" Emote Tome"),
        new("/no", [], "Disagrees.", Plain, null),
        new("/paper", [], "Shows paper.", Plain, "\"/Paper\" Emote Tome", "Paper"),
        new("/petalthrow", [], "Throws flower petals.", Loop, "\"/PetalThrow\" Emote Tome"),
        new("/playdead", [], "Is probably dead.", Loop, "\"/Playdead\" Emote Tome", "playdead"),
        new("/point", [], "Points.", Plain, null),
        new("/ponder", [], "Ponders.", Plain, null),
        new("/posecover", [], "Hits a covered pose.", Loop, "\"/PoseCover\" Emote Tome"),
        new("/poseheart", [], "Poses with a heart.", Loop, "\"/PoseHeart\" Emote Tome"),
        new("/posehigh", [], "Hits a high pose.", Loop, "\"/PoseHigh\" Emote Tome"),
        new("/poselow", [], "Hits a low pose.", Loop, "\"/PoseLow\" Emote Tome"),
        new("/posepeace", [], "Strikes a peace pose.", Loop, "\"/PosePeace\" Emote Tome"),
        new("/posesassy", [], "Strikes a sassy pose.", Loop, "\"/PoseSassy\" Emote Tome"),
        new("/poseshy", [], "Strikes a shy pose.", Loop, "\"/PoseShy\" Emote Tome"),
        new("/posetwist", [], "Hits a twisted pose.", Loop, "\"/PoseTwist\" Emote Tome"),
        new("/possessed", [], "Is acting strangely.", Plain, "\"/Possessed\" Emote Tome", "Possessed"),

        // The one emote that is not really one: it plays /cheer, writes nothing to chat, and
        // is rate-limited by the game itself.
        new("/rank", [], "Cheers silently — once a minute, never in PvP or WvW.", Chair, null),

        new("/readbook", [], "Reads a book.", Loop, "\"/Readbook\" Emote Tome"),
        new("/rock", [], "Shows rock.", Plain, "\"/Rock\" Emote Tome", "Rock"),
        new("/rockout", [], "Is rocking out!", Loop, "\"/Rockout\" Emote Tome", "rockout"),
        new("/sad", [], "Is sad.", Loop, null),
        new("/salute", [], "Salutes.", Plain, null),
        new("/scissors", ["/scis"], "Shows scissors.", Plain, "\"/Scissors\" Emote Tome", "Scissors"),
        new("/serve", [], "Serves a hot drink.", Loop, "\"/Serve\" Emote Tome"),
        new("/shiver", [], "Is shivering.", Loop, "\"/Shiver\" Emote Tome", "shiver"),
        new("/shiverplus", [], "Is shivering, harder.", Loop, "\"/Shiverplus\" Emote Tome", "Shiverplus"),
        new("/shocked", [], "Is shocked.", Plain, "\"/Shocked\" Emote Tome"),
        new("/shrug", [], "Shrugs.", Chair, null),
        new("/shuffle", [], "Does the Inventory Shuffle.", Loop, "How to Dance, Volume 1", "shuffle"),
        new("/sipcoffee", [], "Sips some hot coffee.", Loop, "\"/sipcoffee\" Emote Tome"),
        new("/sit", [], "Sits.", Loop, null),
        new("/sleep", [], "Goes to sleep.", Loop, null),
        new("/step", [], "Does the Dodge Step.", Loop, "How to Dance, Volume 1", "step"),
        new("/stretch", [], "Is stretching.", Plain, "\"/Stretch\" Emote Tome", "Stretch"),
        new("/surprised", [], "Is surprised.", Plain, null),
        new("/talk", [], "Is talking.", Loop, null),
        new("/thank", ["/thanks", "/thx", "/ty"], "Is grateful.", Chair, null),
        new("/threaten", [], "Is threatening.", Plain, null),
        new("/thumbsdown", [], "Gives a thumbs-down.", Loop, "\"/ThumbsDown\" Emote Tome"),
        new("/thumbsup", [], "Gives a thumbs-up.", Loop, "\"/ThumbsUp\" Emote Tome"),
        new("/transform", [], "Transforms.", Loop, "\"/Transform\" Emote Tome"),
        new("/unleash", [], "Unleashes their full power.", Loop, "\"/Unleash\" Emote Tome"),
        new("/wave", [], "Waves.", Chair, null),
        new("/yes", [], "Agrees.", Plain, null)
    ];

    public static IReadOnlyList<Emote> All => Catalogue;

    /// <summary>
    /// Whether this is exactly an emote command — "/wave", or an alias the game accepts — and
    /// nothing more.
    ///
    /// The bridge's gate for doing it in the game rather than copying it. Exact and whole on
    /// purpose: "/wave" is an emote, "/wave hello" is a chat line, and "/gg" forfeits a PvP match.
    /// Only the first is ever typed on anybody's behalf.
    /// </summary>
    public static bool IsCommand(string? text) =>
        text is not null &&
        Catalogue.Any(emote => emote.Commands.Contains(text, StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// What the account can do with this emote.
    /// </summary>
    /// <param name="unlocked">
    /// Ids from /v2/account/emotes, or null when the account has not been read — no key, or a
    /// read that failed. Null gives <see cref="EmoteState.Untracked"/> rather than
    /// <see cref="EmoteState.Locked"/>, because an unread account has not denied anything.
    /// </param>
    public static EmoteState StateOf(Emote emote, IReadOnlySet<string>? unlocked)
    {
        if (emote.IsFree) return EmoteState.Free;

        if (emote.ApiId is null || unlocked is null) return EmoteState.Untracked;

        return unlocked.Contains(emote.ApiId) ? EmoteState.Unlocked : EmoteState.Locked;
    }

    /// <summary>
    /// The emotes matching what has been typed, best first.
    ///
    /// Ranked rather than merely filtered, because the alphabet is actively unhelpful here:
    /// "dance" filtered alphabetically puts /bloodstoneboogie, /breakdance and /crabdance above
    /// /dance itself. An exact command wins, then a command that starts with the term, then one
    /// that contains it, and only then a description — so "grill" still finds /barbecue.
    ///
    /// The leading slash is optional in both directions: someone typing "/wave" and someone
    /// typing "wave" are asking the same thing.
    ///
    /// With nothing typed there is no relevance to rank by, so the list leads with what can
    /// actually be used — see <see cref="Precedence"/>. A search is left alone: someone typing
    /// "rock" wants /rock at the top whether or not they own it.
    /// </summary>
    public static IReadOnlyList<Emote> Matching(string? search, IReadOnlySet<string>? unlocked = null)
    {
        var term = (search ?? string.Empty).Trim().TrimStart('/');

        if (term.Length == 0)
            return Catalogue
                .OrderBy(emote => Precedence(StateOf(emote, unlocked)))
                .ThenBy(emote => emote.Command, StringComparer.Ordinal)
                .ToList();

        return Catalogue
            .Select(emote => (emote, rank: Rank(emote, term)))
            .Where(match => match.rank < NoMatch)
            .OrderBy(match => match.rank)
            .ThenBy(match => match.emote.Command, StringComparer.Ordinal)
            .Select(match => match.emote)
            .ToList();
    }

    /// <summary>
    /// Where a state sits when the list is not being searched: what you can type now, then what
    /// you might already own, then what you demonstrably do not.
    ///
    /// The untracked 29 go in the middle rather than the bottom because their tomes may well be
    /// bought — they are unknown, not absent, and burying them under the emotes the API has
    /// positively denied would rank a maybe below a no.
    /// </summary>
    private static int Precedence(EmoteState state) => state switch
    {
        EmoteState.Free or EmoteState.Unlocked => 0,
        EmoteState.Untracked => 1,
        _ => 2
    };

    private const int NoMatch = 4;

    private static int Rank(Emote emote, string term)
    {
        var best = NoMatch;

        foreach (var command in emote.Commands)
        {
            var word = command.TrimStart('/');

            if (word.Equals(term, StringComparison.OrdinalIgnoreCase)) return 0;

            if (word.StartsWith(term, StringComparison.OrdinalIgnoreCase)) best = Math.Min(best, 1);
            else if (word.Contains(term, StringComparison.OrdinalIgnoreCase)) best = Math.Min(best, 2);
        }

        if (best == NoMatch && emote.Does.Contains(term, StringComparison.OrdinalIgnoreCase))
            best = 3;

        return best;
    }

    /// <summary>
    /// The header line: what you can use, what you cannot, and what cannot be established.
    ///
    /// The third figure is stated rather than folded into either of the others. Adding the 29
    /// to "you can use" claims tomes that may never have been bought; adding them to "locked"
    /// tells a player they lack an emote they own.
    /// </summary>
    public static string Summarise(IReadOnlySet<string>? unlocked)
    {
        if (unlocked is null)
            return $"{Catalogue.Count(emote => emote.IsFree)} free to every account · " +
                   $"{Catalogue.Count(emote => !emote.IsFree)} unlockable";

        var states = Catalogue.Select(emote => StateOf(emote, unlocked)).ToList();

        var usable = states.Count(state => state is EmoteState.Free or EmoteState.Unlocked);
        var locked = states.Count(state => state is EmoteState.Locked);
        var untracked = states.Count(state => state is EmoteState.Untracked);

        var parts = new List<string> { $"{usable} you can use" };

        if (locked > 0) parts.Add($"{locked} locked");
        if (untracked > 0) parts.Add($"{untracked} the API cannot see");

        return string.Join(" · ", parts);
    }

    /// <summary>Why a third of the screen has no verdict on it.</summary>
    public const string Note =
        "Guild Wars 2 publishes only 14 of its 43 unlockable emotes, so the account endpoint " +
        "can answer for those and no others. The rest — every gem-store emote tome — are " +
        "listed without a verdict rather than guessed at; type /emotelist in game for the " +
        "account's own list. Emotes cannot be used underwater, and appending @ aims one at " +
        "your target.";
}
