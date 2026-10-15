using System.Globalization;

using GuildWarsCompanion.Blish;

using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>The numbers on the Blish HUD module's objective card.</summary>
public class CardTextTests
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-GB");
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    [Theory]
    [InlineData(0, "0 m")]
    [InlineData(37.4, "37 m")]
    [InlineData(999.4, "999 m")]
    [InlineData(999.6, "1.0 km")]
    [InlineData(1234, "1.2 km")]
    [InlineData(-2, "0 m")]
    public void Distance_reads_in_metres_and_then_kilometres(double metres, string expected) =>
        Assert.Equal(expected, CardText.Distance(metres, English));

    [Fact]
    public void Kilometres_are_written_the_way_the_pc_writes_numbers() =>
        Assert.Equal("1,2 km", CardText.Distance(1234, German));

    [Theory]
    [InlineData(12.4, 12)]
    [InlineData(-8.6, -9)]
    [InlineData(3, 3)]
    public void A_real_climb_is_said_in_whole_metres(double up, int expected) =>
        Assert.Equal(expected, CardText.Climb(up, heightKnown: true));

    [Theory]
    [InlineData(2.9)]
    [InlineData(-2.9)]
    [InlineData(0)]
    public void About_level_says_nothing(double up) => Assert.Null(CardText.Climb(up, heightKnown: true));

    [Fact]
    public void A_height_nobody_knows_says_nothing() => Assert.Null(CardText.Climb(40, heightKnown: false));

    [Fact]
    public void The_climb_beside_the_arrow_has_no_sign() => Assert.Equal("9 m", CardText.ClimbText(-9, English));
}
