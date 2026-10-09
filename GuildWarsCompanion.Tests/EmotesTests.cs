using GuildWarsCompanion.Services;
using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// The emote catalogue.
///
/// The trap here is that /v2/account/emotes answers confidently about a small minority of the
/// game's emotes and is silent about the rest, so the obvious implementation — "unlocked if the
/// account lists it" — marks a player's own /breakdance as locked and every free emote too.
/// Most of what follows pins the difference between "no" and "no answer".
///
/// The rest guards the search, which is what the screen is for: filtering alphabetically is not
/// enough, because typing "dance" alphabetically lists three other dances before /dance.
/// </summary>
public class EmotesTests
{
    /// <summary>The complete /v2/emotes list, checked against the live endpoint.</summary>
    private static readonly string[] Published =
    [
        "Bless", "Heroic", "Paper", "Possessed", "Rock", "Scissors", "Shiverplus", "Stretch",
        "geargrind", "playdead", "rockout", "shiver", "shuffle", "step"
    ];

    private static IReadOnlySet<string> Unlocked(params string[] ids) =>
        new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);

    private static Emote Find(string command) =>
        Emotes.All.Single(emote => emote.Command == command);

    [Fact]
    public void The_catalogue_holds_every_emote_not_only_the_published_ones()
    {
        // 25 free on any account, 43 unlockable. The API knows 14 of the 43 and none of the 25,
        // which is why the table is hardcoded at all.
        Assert.Equal(68, Emotes.All.Count);
        Assert.Equal(25, Emotes.All.Count(emote => emote.IsFree));
        Assert.Equal(43, Emotes.All.Count(emote => !emote.IsFree));
    }

    [Fact]
    public void Only_published_emotes_carry_an_api_id()
    {
        var carried = Emotes.All.Where(emote => emote.ApiId is not null)
            .Select(emote => emote.ApiId!)
            .ToList();

        // An id the endpoint does not use would never match, leaving the emote permanently
        // locked on screen; an id left off one that does costs a verdict we could have given.
        Assert.Equal(Published.Order(), carried.Order());

        // Nothing free can be published — a free emote has no unlock item to be listed under.
        Assert.All(Emotes.All.Where(emote => emote.IsFree), emote => Assert.Null(emote.ApiId));
    }

    [Fact]
    public void Every_command_is_typeable_and_unique()
    {
        var all = Emotes.All.SelectMany(emote => emote.Commands).ToList();

        Assert.All(all, command => Assert.StartsWith("/", command));
        Assert.Equal(all.Count, all.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(Emotes.All, emote => Assert.NotEqual(string.Empty, emote.Does));
    }

    [Fact]
    public void A_free_emote_is_free_whatever_the_account_says()
    {
        Assert.Equal(EmoteState.Free, Emotes.StateOf(Find("/dance"), Unlocked()));
        Assert.Equal(EmoteState.Free, Emotes.StateOf(Find("/dance"), null));
    }

    [Fact]
    public void A_published_emote_is_unlocked_or_locked_by_the_account()
    {
        Assert.Equal(EmoteState.Unlocked, Emotes.StateOf(Find("/rock"), Unlocked("Rock")));
        Assert.Equal(EmoteState.Locked, Emotes.StateOf(Find("/rock"), Unlocked("Paper")));
    }

    [Fact]
    public void An_unpublished_unlockable_is_never_called_locked()
    {
        // /breakdance is bought from a tome and does not appear in /v2/emotes at all. Saying
        // "locked" here tells a player they lack an emote they may well own.
        var breakdance = Find("/breakdance");

        Assert.Null(breakdance.ApiId);
        Assert.Equal(EmoteState.Untracked, Emotes.StateOf(breakdance, Unlocked(Published)));
    }

    [Fact]
    public void An_unread_account_denies_nothing()
    {
        // No key, or a read that failed: null is not the same as an empty list. An empty list
        // is the account saying it has unlocked none of them, which is a real answer.
        Assert.Equal(EmoteState.Untracked, Emotes.StateOf(Find("/rock"), null));
        Assert.Equal(EmoteState.Locked, Emotes.StateOf(Find("/rock"), Unlocked()));
    }

    [Fact]
    public void An_exact_command_beats_the_alphabet()
    {
        // /bloodstoneboogie, /breakdance, /crabdance and /happydance all contain "dance" and all
        // sort before it.
        Assert.Equal("/dance", Emotes.Matching("dance")[0].Command);
        Assert.Equal("/sit", Emotes.Matching("sit")[0].Command);
    }

    [Fact]
    public void The_leading_slash_is_optional()
    {
        Assert.Equal(
            Emotes.Matching("wave").Select(emote => emote.Command),
            Emotes.Matching("/wave").Select(emote => emote.Command));
    }

    [Fact]
    public void An_alias_finds_its_emote()
    {
        // "/ty" is the one most likely to be typed in anger.
        Assert.Equal("/thank", Emotes.Matching("ty")[0].Command);
        Assert.Equal("/barbecue", Emotes.Matching("grill")[0].Command);
    }

    [Fact]
    public void What_an_emote_does_is_searchable_too()
    {
        // Nothing in the command says "coffee" or "petals"; the chat line does, and that is how
        // a half-remembered emote gets found.
        Assert.Contains(Emotes.Matching("coffee"), emote => emote.Command == "/sipcoffee");
        Assert.Contains(Emotes.Matching("hydrated"), emote => emote.Command == "/drink");
    }

    [Fact]
    public void An_empty_search_is_the_whole_list_and_a_bad_one_is_none()
    {
        Assert.Equal(Emotes.All.Count, Emotes.Matching(null).Count);
        Assert.Equal(Emotes.All.Count, Emotes.Matching("   ").Count);
        Assert.Empty(Emotes.Matching("gyroscope"));
    }

    [Fact]
    public void The_unsearched_list_leads_with_what_can_be_used()
    {
        var owned = Unlocked("Rock");
        var order = Emotes.Matching(null, owned)
            .Select(emote => Emotes.StateOf(emote, owned))
            .Select(state => state switch
            {
                EmoteState.Free or EmoteState.Unlocked => 0,
                EmoteState.Untracked => 1,
                _ => 2
            })
            .ToList();

        // Usable first, then the tomes nobody can check, then the ones positively denied — a
        // maybe must not rank below a no.
        Assert.Equal(order.Order(), order);
        Assert.Equal(26, order.Count(rank => rank == 0));
        Assert.Equal("/beckon", Emotes.Matching(null, owned)[0].Command);
    }

    [Fact]
    public void Each_band_of_the_unsearched_list_stays_alphabetical()
    {
        var owned = Unlocked("Rock");

        var free = Emotes.Matching(null, owned)
            .TakeWhile(emote => Emotes.StateOf(emote, owned) is EmoteState.Free or EmoteState.Unlocked)
            .Select(emote => emote.Command)
            .ToList();

        Assert.Equal(free.Order(StringComparer.Ordinal), free);
    }

    [Fact]
    public void A_search_ranks_by_relevance_not_by_ownership()
    {
        // Typing "rock" while /rock is locked still means /rock. Ownership sorts the list you
        // browse; it must not push away the thing you asked for by name.
        Assert.Equal("/rock", Emotes.Matching("rock", Unlocked())[0].Command);
    }

    [Fact]
    public void The_summary_states_the_unverifiable_third_separately()
    {
        var summary = Emotes.Summarise(Unlocked("Rock", "Paper", "Scissors"));

        // 25 free plus 3 confirmed; 11 of the published 14 confirmed absent; 29 with no verdict
        // available. Folding the last figure into either of the others is the whole hazard.
        Assert.Equal("28 you can use · 11 locked · 29 the API cannot see", summary);
    }

    [Fact]
    public void An_unread_account_is_summarised_without_a_claim()
    {
        Assert.Equal("25 free to every account · 43 unlockable", Emotes.Summarise(null));
    }
}
