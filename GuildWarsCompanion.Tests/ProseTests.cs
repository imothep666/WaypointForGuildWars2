using GuildWarsCompanion.Services;
using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// The API embeds raw markup in some prose. These cases come from what it actually returned:
/// a backstory journal entry arrived wrapped in literal &lt;br&gt; tags and rendered them.
/// </summary>
public class ProseTests
{
    [Fact]
    public void Strips_the_markup_the_api_actually_sent()
    {
        const string asReturned =
            "<br><br>A necromancer's closest companion is death. In acknowledgement of this, " +
            "I mark my face with the symbol of a trickster demon.<br><br>This is my story.<br>";

        var cleaned = Prose.Clean(asReturned);

        Assert.NotNull(cleaned);
        Assert.DoesNotContain("<br>", cleaned);
        Assert.DoesNotContain("<", cleaned);
        Assert.StartsWith("A necromancer's", cleaned);
        Assert.EndsWith("This is my story.", cleaned);
    }

    [Theory]
    [InlineData("<br/>")]
    [InlineData("<br />")]
    [InlineData("<BR>")]
    [InlineData("<Br  />")]
    public void Handles_every_spelling_of_a_line_break(string tag)
    {
        var cleaned = Prose.Clean($"first{tag}second");

        Assert.Equal("first\nsecond", cleaned);
    }

    [Fact]
    public void Keeps_paragraph_structure_but_collapses_runs()
    {
        var cleaned = Prose.Clean("one<br><br><br><br>two");

        Assert.Equal("one\n\ntwo", cleaned);
    }

    [Fact]
    public void Decodes_the_entities_that_come_with_markup()
    {
        var cleaned = Prose.Clean("Grenth&#39;s chosen &amp; the &quot;faithful&quot;&nbsp;stand");

        Assert.Equal("Grenth's chosen & the \"faithful\" stand", cleaned);
    }

    [Fact]
    public void Removes_tags_other_than_breaks()
    {
        Assert.Equal("bold and plain", Prose.Clean("<b>bold</b> and <i>plain</i>"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Passes_nothing_through_unchanged(string? input) =>
        Assert.Equal(input, Prose.Clean(input));

    [Fact]
    public void Leaves_clean_text_alone()
    {
        const string clean = "Though trouble may follow me, I overcome it with dignity.";

        Assert.Equal(clean, Prose.Clean(clean));
    }
}
