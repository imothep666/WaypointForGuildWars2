using GuildWarsCompanion.Services;
using GuildWarsCompanion.Services.Alerts;
using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// Which alerts the phone keeps quiet about while a character is in the game.
///
/// The same bias as quiet hours: withholding is invisible, so only a definite answer from the
/// bridge holds anything back, and only what the game says itself.
/// </summary>
public class WhilePlayingTests
{
    private static readonly AccountAlert Sale =
        new("tp:sells:1", "Sold", "Mithril Ore") { GameSaysItToo = true };

    private static readonly AccountAlert Vault =
        new("vault:daily:2026-10-05:7", "Daily objective complete", "Gather") { GameSaysItToo = true };

    private static readonly AccountAlert Boss = new("boss:tequatl", "Tequatl in 10 minutes", "Sparkfly Fen");

    private static readonly AccountAlert Price = new("price:19700", "Mithril Ore under 1s", "Buy");

    private static readonly IReadOnlyList<AccountAlert> All = [Sale, Boss, Vault, Price];

    [Fact]
    public void In_the_game_what_the_game_says_is_held_back()
    {
        Assert.Equal([Boss, Price], WhilePlaying.Keep(All, LiveReading.Playing("Imothep")));
    }

    [Fact]
    public void Nobody_in_the_game_lets_everything_through()
    {
        // The game is closed or on the login screen: nothing is telling the player anything.
        Assert.Equal(All, WhilePlaying.Keep(All, LiveReading.NobodyPlaying));
    }

    [Fact]
    public void An_unreachable_bridge_lets_everything_through()
    {
        // Ignorance, not an answer: a PC that is asleep says nothing about who is playing.
        Assert.Equal(All, WhilePlaying.Keep(All, LiveReading.Unknown));
    }

    [Fact]
    public void The_bridge_is_asked_only_when_its_answer_could_hold_something_back()
    {
        Assert.False(WhilePlaying.NeedsAsking([Boss, Price]));
        Assert.False(WhilePlaying.NeedsAsking([]));
        Assert.True(WhilePlaying.NeedsAsking([Boss, Sale]));
    }

    [Fact]
    public void An_alert_says_nothing_about_the_game_unless_marked()
    {
        // The flag is copied from the watcher by the service; a watcher's own alert starts false,
        // so a new watcher that forgets about it errs towards being shown.
        Assert.False(new AccountAlert("k", "t", "b").GameSaysItToo);
    }
}
