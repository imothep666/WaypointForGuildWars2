using GuildWarsCompanion.Services;

using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// The Here widget: what is left on the map someone is playing, on the home screen.
///
/// The way it can quietly lie is the one worth pinning: an evening-old "map chest waiting" or
/// "Shadow Behemoth in 25 min" shown as if it were about now.
/// </summary>
public class HereWidgetTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 18, 0, 0, TimeSpan.Zero);

    private static readonly HereReading Playing =
        new("Sweet Child O Night", "Crystal Oasis", "Here: map chest waiting · Casino Blitz in 25 min · Shadow Behemoth in 1h 05m · Tequatl the Sunless in 1h 50m");

    [Fact]
    public void A_fresh_reading_says_what_is_left_one_fact_a_line()
    {
        var panel = HereWidget.Panel(Playing, Now.AddMinutes(-3), Now);

        Assert.True(panel.IsLive);
        Assert.Equal("Crystal Oasis", panel.Map);
        Assert.Equal("Sweet Child O Night", panel.Who);
        Assert.Equal(["Map chest waiting", "Casino Blitz in 25 min", "Shadow Behemoth in 1h 05m"],
            panel.Lines.Select(fact => fact.Text));
    }

    [Fact]
    public void An_old_reading_says_where_they_were_and_nothing_about_now()
    {
        var panel = HereWidget.Panel(Playing, Now.AddHours(-3), Now);

        Assert.False(panel.IsLive);
        Assert.Equal("Crystal Oasis", panel.Map);
        Assert.Equal("Sweet Child O Night · last seen 3 hours ago", panel.Who);
        Assert.Empty(panel.Lines);
    }

    [Fact]
    public void A_reading_just_past_the_poll_still_counts_as_now()
    {
        // The poll runs every quarter hour; a character still playing must not flicker between.
        Assert.True(HereWidget.Panel(Playing, Now.AddMinutes(-17), Now).IsLive);
        Assert.False(HereWidget.Panel(Playing, Now.AddMinutes(-21), Now).IsLive);
    }

    [Fact]
    public void Nothing_ever_read_asks_for_the_bridge()
    {
        var panel = HereWidget.Panel(null, null, Now);

        Assert.False(panel.IsLive);
        Assert.Equal("Not in the game", panel.Map);
        Assert.Empty(panel.Lines);
    }

    [Fact]
    public void A_map_with_nothing_said_about_it_still_names_the_map()
    {
        var panel = HereWidget.Panel(new HereReading("Imosan", "Lion's Arch", null), Now, Now);

        Assert.True(panel.IsLive);
        Assert.Equal("Lion's Arch", panel.Map);
        Assert.Empty(panel.Lines);
    }

    /// <summary>The painting is filed under the English name, whatever the heading says: the wiki
    /// has "Crystal Oasis loading screen", not "Kristalloase".</summary>
    [Fact]
    public void The_painting_is_the_map_s_English_one()
    {
        var german = new HereReading("Sweet Child O Night", "Kristalloase", null, "Crystal Oasis");

        var panel = HereWidget.Panel(german, Now.AddMinutes(-3), Now);

        Assert.Equal("Kristalloase", panel.Map);
        Assert.Equal("Crystal Oasis", panel.Art);
    }

    /// <summary>Last seen there, so still painted there.</summary>
    [Fact]
    public void An_old_reading_keeps_its_painting() =>
        Assert.Equal("Crystal Oasis",
            HereWidget.Panel(Playing with { English = "Crystal Oasis" }, Now.AddHours(-3), Now).Art);

    /// <summary>A reading stored before the widget had paintings, and no reading at all, keep the wash.</summary>
    [Fact]
    public void Without_an_English_name_there_is_no_painting()
    {
        Assert.Null(HereWidget.Panel(Playing, Now, Now).Art);
        Assert.Null(HereWidget.Panel(null, null, Now).Art);
    }

    // --- what is true of the map today ---------------------------------------

    /// <summary>Challenges 17:45 to 18:05 UTC, then the Octovine at 18:30.</summary>
    private static readonly MetaMap Auric = new("ab", "hot", "Auric Basin",
        [new(1065, 20, "Challenges"), new(1085, 25, ""), new(1110, 20, "Octovine")]);

    private static readonly HereReading InAuric =
        new("Sweet Child O Night", "Goldene Senke", null, "Auric Basin", 1043, "Chronomancer", "spec-40");

    private static readonly HereReading InQueensdale =
        new("Imosan", "Queensdale", null, "Queensdale", 15);

    private static HereDay Day(MetaMap? meta = null, string[]? looted = null, string[]? killed = null) =>
        new(meta, ["auric_basin_heros_choice_chest", "queensdale_heros_choice_chest"],
            looted is null ? null : new HashSet<string>(looted),
            killed is null ? null : new HashSet<string>(killed));

    [Fact]
    public void A_meta_on_now_says_until_when_and_what_follows()
    {
        var meta = HereWidget.Panel(InAuric, Now, Now, Day(Auric)).Meta!;

        Assert.Equal("Challenges", meta.Name);
        Assert.True(meta.IsOn);
        Assert.Equal($"until {Now.AddMinutes(5).ToLocalTime():HH:mm} · then Octovine", meta.Note);
    }

    [Fact]
    public void A_meta_that_repeats_says_when_it_comes_round_again()
    {
        var desert = new MetaMap("dh", "pof", "Desert Highlands",
            [new(1065, 20, "Buried Treasure"), new(1085, 40, ""), new(1125, 20, "Buried Treasure")]);

        var meta = HereWidget.Panel(InAuric with { English = "Desert Highlands" }, Now, Now, Day(desert)).Meta!;

        Assert.Equal(
            $"until {Now.AddMinutes(5).ToLocalTime():HH:mm} · again at {Now.AddMinutes(45).ToLocalTime():HH:mm}",
            meta.Note);
    }

    [Fact]
    public void A_meta_to_come_is_counted_down_to()
    {
        var at = Now.AddHours(-1);
        var meta = HereWidget.Panel(InAuric, at, at, Day(Auric)).Meta!;

        Assert.Equal("Challenges", meta.Name);
        Assert.Equal(at.AddMinutes(45), meta.StartsAt);
        Assert.Equal($"starts at {at.AddMinutes(45).ToLocalTime():HH:mm}", meta.Note);
    }

    /// <summary>The point of working the map out at draw time: hours after the game closed, the
    /// timetable and today's chest are still about that map.</summary>
    [Fact]
    public void An_old_reading_still_says_what_is_true_of_the_map_today()
    {
        var panel = HereWidget.Panel(InAuric, Now.AddHours(-3), Now, Day(Auric, looted: []));

        Assert.False(panel.IsLive);
        Assert.Equal("Challenges", panel.Meta?.Name);
        Assert.Equal([new HereFact("Map chest waiting", HereTone.Waiting)], panel.Lines);
    }

    [Fact]
    public void A_looted_chest_says_so_in_grey() =>
        Assert.Equal([new HereFact("Map chest looted", HereTone.Done)],
            HereWidget.Panel(InAuric, Now, Now, Day(looted: ["auric_basin_heros_choice_chest"])).Lines);

    /// <summary>Not read since the reset is not known, so nothing is said about the chest.</summary>
    [Fact]
    public void A_chest_not_read_today_is_left_out() =>
        Assert.Empty(HereWidget.Panel(InAuric, Now, Now, Day()).Lines);

    [Fact]
    public void A_map_s_world_boss_says_when_or_that_it_is_done()
    {
        var next = EventSchedule.NextAppearances(Now).Single(boss => boss.ApiId == "shadow_behemoth").Next;

        Assert.Equal(
            [new HereFact($"Shadow Behemoth at {next.ToLocalTime():HH:mm}")],
            HereWidget.Panel(InQueensdale, Now, Now, Day()).Lines);

        Assert.Equal(
            [new HereFact("Map chest waiting", HereTone.Waiting), new HereFact("Shadow Behemoth · done today", HereTone.Done)],
            HereWidget.Panel(InQueensdale, Now, Now, Day(looted: [], killed: ["shadow_behemoth"])).Lines);
    }

    [Fact]
    public void The_calling_is_named_while_playing_and_its_emblem_kept_after()
    {
        var live = HereWidget.Panel(InAuric, Now, Now, Day());
        var old = HereWidget.Panel(InAuric, Now.AddHours(-3), Now, Day());

        Assert.Equal("Sweet Child O Night · Chronomancer", live.Who);
        Assert.Equal("Sweet Child O Night · last seen 3 hours ago", old.Who);
        Assert.Equal("spec-40", old.Emblem);
    }

    [Fact]
    public void A_map_without_a_meta_has_no_meta_line() =>
        Assert.Null(HereWidget.Panel(InQueensdale, Now, Now, Day()).Meta);

    [Theory]
    [InlineData(1, "Guardian")]
    [InlineData(9, "Revenant")]
    public void MumbleLink_s_professions_are_the_API_s(int mumble, string api) =>
        Assert.Equal(api, Callings.ProfessionFor(mumble));

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    public void An_unknown_profession_is_none(int mumble) =>
        Assert.Null(Callings.ProfessionFor(mumble));

    [Theory]
    [InlineData("Here: nothing left", "Nothing left")]
    [InlineData("Here: Battle in Tarir (Octovine) on now", "Battle in Tarir (Octovine) on now")]
    public void One_fact_is_one_line(string line, string expected) =>
        Assert.Equal([expected], HereWidget.Facts(line));
}
