using System.Linq;

using GuildWarsCompanion.Blish;

using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>How the Blish HUD module breaks an alert into lines that fit its box.</summary>
public class AlertTextTests
{
    /// <summary>Every character ten pixels wide: easy to reason about.</summary>
    private static double Measure(string s) => s.Length * 10;

    [Fact]
    public void Short_text_stays_on_one_line() =>
        Assert.Equal(new[] { "Mystic Coin" }, AlertText.Wrap("Mystic Coin", 200, Measure));

    [Fact]
    public void Long_text_breaks_between_words()
    {
        var lines = AlertText.Wrap("Mystic Coin is at 1g 20s on the trading post", 120, Measure);

        Assert.Equal(new[] { "Mystic Coin", "is at 1g 20s", "on the", "trading post" }, lines);
    }

    [Fact]
    public void No_line_is_wider_than_the_box()
    {
        var text = string.Join(" ", Enumerable.Repeat("Tequatl the Sunless rises in five minutes", 8));

        Assert.All(AlertText.Wrap(text, 300, Measure), line => Assert.True(Measure(line) <= 300, line));
    }

    [Fact]
    public void A_word_longer_than_a_line_is_broken_where_it_has_to_be() =>
        Assert.Equal(new[] { "see", "abcdefghij", "klmno" }, AlertText.Wrap("see abcdefghijklmno", 100, Measure));

    [Fact]
    public void Runs_of_spaces_are_one_space() =>
        Assert.Equal(new[] { "a b" }, AlertText.Wrap("  a    b ", 100, Measure));

    [Fact]
    public void Nothing_to_say_is_no_lines()
    {
        Assert.Empty(AlertText.Wrap("", 100, Measure));
        Assert.Empty(AlertText.Wrap("   ", 100, Measure));
    }

    [Fact]
    public void A_box_narrower_than_a_character_still_ends() =>
        Assert.Equal(new[] { "a", "b" }, AlertText.Wrap("ab", 5, Measure));
}
