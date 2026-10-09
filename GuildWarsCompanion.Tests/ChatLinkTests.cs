using GuildWarsCompanion.Services;

using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// Item chat links, against codes the API itself wrote.
///
/// Each expected value is the <c>chat_link</c> /v2/items returned for that id, not a second
/// encoding of the same reasoning: a link that is wrong by one byte is still a valid-looking code,
/// and the game shows it as a different item or none.
/// </summary>
public class ChatLinkTests
{
    [Theory]
    // Glob of Ectoplasm: the wiki's own worked example.
    [InlineData(19721, "[&AgEJTQAA]")]
    // Mystic Coin.
    [InlineData(19976, "[&AgEITgAA]")]
    // Shard of Glory: an id past 65535, so the third id byte is not zero.
    [InlineData(70820, "[&AgGkFAEA]")]
    public void A_single_item_matches_the_api(int id, string expected)
    {
        Assert.Equal(expected, ChatLink.Item(id));
    }

    /// <summary>What the API cannot write: the quantity byte, here a full stack of ectoplasm.</summary>
    [Fact]
    public void A_stack_carries_its_quantity()
    {
        Assert.Equal("[&AvoJTQAA]", ChatLink.Item(19721, 250));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(19721, 0)]
    [InlineData(19721, 251)]
    [InlineData(0x1000000, 1)]
    public void Nothing_outside_what_the_game_links_is_made(int id, int count)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ChatLink.Item(id, count));
    }
}
