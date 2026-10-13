using GuildWarsCompanion.Gw2;
using GuildWarsCompanion.Services;
using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// What the map picker offers, and what a typed name finds.
///
/// The picker is the whole of "use the map without a PC", and both of its decisions are here:
/// a map nobody can stand in must not be in the list, and a name typed halfway through must
/// still find the map somebody meant.
/// </summary>
public class MapPicksTests
{
    private static MapInfo Map(
        int id, string name, string? type = "Public", bool placed = true,
        string? region = "Shiverpeak Mountains", string? continent = "Tyria") =>
        new(id, name, 2, 1, 1,
            placed ? [[0, 0], [100, 100]] : null,
            null, type, region, continent);

    [Fact]
    public void Offers_a_public_map_with_a_footprint()
    {
        Assert.True(MapPicks.Offerable(Map(15, "Queensdale")));
    }

    [Fact]
    public void Offers_an_instance_because_a_dungeon_is_a_place_too()
    {
        Assert.True(MapPicks.Offerable(Map(36, "Ascalonian Catacombs", type: "Instance")));
    }

    /// <summary>
    /// A map with no continent_rect cannot be drawn: there is nothing to fit the viewport to and
    /// no coordinate to put a marker at. Offering it would be offering a blank screen.
    /// </summary>
    [Fact]
    public void Refuses_a_map_the_api_never_placed()
    {
        Assert.False(MapPicks.Offerable(Map(95, "Nowhere", placed: false)));
    }

    [Theory]
    [InlineData("Pvp")]
    [InlineData("Center")]
    [InlineData("BlueHome")]
    [InlineData("Tutorial")]
    public void Refuses_the_types_nobody_walks_around_in(string type)
    {
        Assert.False(MapPicks.Offerable(Map(38, "Eternal Battlegrounds", type: type)));
    }

    /// <summary>
    /// A row stored before the type column existed has no type, and dropping those would empty
    /// the picker on exactly the phones that had it working yesterday.
    /// </summary>
    [Fact]
    public void Keeps_a_map_whose_type_is_missing()
    {
        Assert.True(MapPicks.Offerable(Map(15, "Queensdale", type: null)));
    }

    [Fact]
    public void Sorts_choices_by_name_and_drops_what_cannot_be_drawn()
    {
        var choices = MapPicks.Choices([
            Map(2, "Wayfarer Foothills"),
            Map(3, "Eternal Battlegrounds", type: "Center"),
            Map(1, "Frostgorge Sound"),
            Map(4, "Nowhere", placed: false)
        ]);

        Assert.Equal(["Frostgorge Sound", "Wayfarer Foothills"], choices.Select(c => c.Name));
    }

    [Fact]
    public void Names_the_region_when_the_api_gives_none()
    {
        var choice = Assert.Single(MapPicks.Choices([Map(1, "Frostgorge Sound", region: null)]));

        Assert.Equal("Elsewhere", choice.Region);
    }

    /// <summary>The line under the name, which has to read as a sentence with either half
    /// missing.</summary>
    [Fact]
    public void Says_where_a_map_is_without_repeating_an_empty_continent()
    {
        var choice = Assert.Single(MapPicks.Choices([Map(1, "Frostgorge Sound", continent: null)]));

        Assert.Equal("Shiverpeak Mountains", choice.Where);
        Assert.Equal(
            "Shiverpeak Mountains · Tyria",
            MapPicks.Choices([Map(1, "Frostgorge Sound")]).Single().Where);
    }

    /// <summary>
    /// The case the whole search exists for: "gorge" is how somebody looks for Frostgorge Sound,
    /// and a prefix search answers nothing at all for it.
    /// </summary>
    [Fact]
    public void Finds_a_name_by_its_middle()
    {
        var all = MapPicks.Choices([Map(1, "Frostgorge Sound"), Map(2, "Queensdale")]);

        var found = MapPicks.Search(all, "gorge");

        Assert.Equal("Frostgorge Sound", Assert.Single(found).Name);
    }

    [Fact]
    public void Puts_a_name_that_starts_with_the_term_first()
    {
        var all = MapPicks.Choices([
            Map(1, "Frostgorge Sound"),
            Map(2, "Gorge of the Lost"),
            Map(3, "Snowden Drifts")
        ]);

        var found = MapPicks.Search(all, "gorge");

        Assert.Equal(["Gorge of the Lost", "Frostgorge Sound"], found.Select(c => c.Name));
    }

    [Fact]
    public void Ignores_case_and_surrounding_space()
    {
        var all = MapPicks.Choices([Map(1, "Frostgorge Sound")]);

        Assert.Single(MapPicks.Search(all, "  FROSTGORGE "));
    }

    /// <summary>
    /// An empty box means the open world rather than everything: 919 of the game's maps are
    /// story steps, and an alphabetical list of those is not a list anybody reads.
    /// </summary>
    [Fact]
    public void Browses_the_open_world_before_anybody_types()
    {
        var all = MapPicks.Choices([
            Map(1, "Frostgorge Sound"),
            Map(2, "A Bold Strategy: Alliance Camp", type: "Instance")
        ]);

        Assert.Equal("Frostgorge Sound", Assert.Single(MapPicks.Search(all, string.Empty)).Name);
        Assert.Single(MapPicks.Search(all, "   "));
    }

    /// <summary>
    /// And one letter reaches the instances, or Ascalonian Catacombs would be unfindable — which
    /// is the reason they are in the list at all rather than filtered away.
    /// </summary>
    [Fact]
    public void Finds_an_instance_once_a_name_is_typed()
    {
        var all = MapPicks.Choices([
            Map(1, "Frostgorge Sound"),
            Map(36, "Ascalonian Catacombs", type: "Instance")
        ]);

        Assert.Equal(
            "Ascalonian Catacombs", Assert.Single(MapPicks.Search(all, "catacombs")).Name);
    }

    /// <summary>
    /// The live API publishes five maps called <c>???</c> or nothing at all. A row nobody can
    /// read is a row nobody can choose on purpose.
    /// </summary>
    [Theory]
    [InlineData("???")]
    [InlineData("")]
    [InlineData("   ")]
    public void Refuses_a_map_with_no_readable_name(string name)
    {
        Assert.False(MapPicks.Offerable(Map(1, name)));
    }

    [Fact]
    public void Answers_nothing_for_a_name_no_map_has()
    {
        var all = MapPicks.Choices([Map(1, "Frostgorge Sound")]);

        Assert.Empty(MapPicks.Search(all, "Divinity"));
    }

    private static readonly Dictionary<int, string> English = new()
    {
        [1203] = "Siren's Landing", [24] = "Gendarran Fields", [1195] = "Draconis Mons"
    };

    private static IReadOnlyList<MapChoice> German() => MapPicks.Choices(
        [Map(1203, "Zaubersängerlände", region: "Ruinen von Orr"),
         Map(24, "Gendarran-Felder", region: "Kryta"),
         Map(1195, "Draconis Mons", region: "Ruinen von Orr")],
        English);

    /// <summary>The name every guide uses finds the map on a German phone.</summary>
    [Fact]
    public void Finds_a_map_by_its_English_name()
    {
        Assert.Equal("Zaubersängerlände", Assert.Single(MapPicks.Search(German(), "siren")).Name);
    }

    [Fact]
    public void Still_finds_a_map_by_the_phones_own_name()
    {
        Assert.Equal("Gendarran-Felder", Assert.Single(MapPicks.Search(German(), "felder")).Name);
    }

    /// <summary>The English name goes on the line under the German one, and only where it differs.</summary>
    [Fact]
    public void Shows_the_English_name_only_where_it_differs()
    {
        var all = German();

        Assert.Equal("Siren's Landing · Ruinen von Orr · Tyria", all.Single(c => c.Id == 1203).Under);
        Assert.Equal("Ruinen von Orr · Tyria", all.Single(c => c.Id == 1195).Under);
    }

    [Fact]
    public void An_English_name_that_starts_with_the_term_ranks_first()
    {
        // All three contain an "s" somewhere; only Siren's Landing starts with one.
        var found = MapPicks.Search(German(), "s");

        Assert.Equal(["Zaubersängerlände", "Draconis Mons", "Gendarran-Felder"], found.Select(c => c.Name));
    }

    /// <summary>No English names stored and no connection: one language, as before.</summary>
    [Fact]
    public void Without_English_names_a_row_has_one_name()
    {
        var choice = Assert.Single(MapPicks.Choices([Map(1203, "Zaubersängerlände")]));

        Assert.Null(choice.English);
        Assert.Equal(choice.Where, choice.Under);
    }
}
