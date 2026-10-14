using System.Globalization;
using System.Resources;
using GuildWarsCompanion.Localization;
using GuildWarsCompanion.Services;
using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// World bosses and raid encounters in German. A name falls back to English when it has no entry,
/// and so does one that reads the same in German, so the entries are checked for directly: a boss
/// added to the schedule, or a raid wing added to the table, must bring its German with it.
/// </summary>
public sealed class GameNamesTests
{
    private static readonly CultureInfo German = new("de-DE");

    private static readonly ResourceSet Translations =
        new ResourceManager("GuildWarsCompanion.Localization.Strings", typeof(L).Assembly)
            .GetResourceSet(German.Parent, createIfNotExists: true, tryParents: false)!;   // filed as "de"

    [Fact]
    public void Every_scheduled_boss_has_its_German()
    {
        var missing = EventSchedule.NextAppearances(DateTimeOffset.UnixEpoch)
            .Select(boss => $"boss|{boss.Name}")
            .Where(key => Translations.GetString(key) is null)
            .ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void Every_raid_encounter_has_its_German()
    {
        var missing = GameNames.Raids.Values
            .Select(name => $"raid|{name}")
            .Where(key => Translations.GetString(key) is null)
            .ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void Names_come_out_in_German_on_a_German_phone()
    {
        var was = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentUICulture = German;

            Assert.Equal("Klaue von Jormag", GameNames.BossById("claw_of_jormag"));
            Assert.Equal("Tal-Wächter", GameNames.Raid("vale_guardian"));

            // The ids that are not the name.
            Assert.Equal("Belagert die Festung", GameNames.Raid("escort"));
            Assert.Equal("Dhuum", GameNames.Raid("voice_in_the_void"));
        }
        finally
        {
            CultureInfo.CurrentUICulture = was;
        }
    }

    [Fact]
    public void English_stays_the_game_s_English()
    {
        Assert.Equal("Great Jungle Wurm", GameNames.BossById("great_jungle_wurm"));
        Assert.Equal("Siege the Stronghold", GameNames.Raid("escort"));

        // Not on the schedule, so the id's own words.
        Assert.Equal("Mists and Monsters Titans", GameNames.BossById("mists_and_monsters_titans"));
    }
}
