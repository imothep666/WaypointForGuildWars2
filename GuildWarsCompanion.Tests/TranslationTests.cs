using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;

using GuildWarsCompanion.Localization;

using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// The app's own words in German, and the translations keeping faith with the code that uses them.
/// </summary>
public sealed partial class TranslationTests
{
    private static readonly CultureInfo German = new("de-DE");

    [Fact]
    public void German_is_used_on_a_German_phone()
    {
        var was = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = German;

            Assert.Equal("PREISBEOBACHTUNG", L.T("PRICE WATCH"));
        }
        finally
        {
            CultureInfo.CurrentUICulture = was;
        }
    }

    /// <summary>
    /// What the bridge sends is English and is translated on the phone by its text, which no
    /// literal in the phone's code carries, so the coverage check below cannot see it.
    /// </summary>
    [Theory]
    [InlineData("Reading Guild Wars 2 now.")]
    [InlineData("Guild Wars 2 is not running — this is what it left behind when it closed.")]
    [InlineData("MumbleLink is not readable.")]
    [InlineData("Looking for Guild Wars 2…")]
    [InlineData("Guardian")]
    [InlineData("Revenant")]
    public void What_the_bridge_says_has_its_German(string english)
    {
        var was = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = German;

            Assert.NotEqual(english, L.T(english));
        }
        finally
        {
            CultureInfo.CurrentUICulture = was;
        }
    }

    [Fact]
    public void A_heading_in_capitals_shares_its_German_with_the_label()
    {
        var was = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = German;

            Assert.Equal("Heute", L.T("Today"));
            Assert.Equal("HEUTE", L.T("TODAY"));
        }
        finally
        {
            CultureInfo.CurrentUICulture = was;
        }
    }

    [Fact]
    public void English_is_the_text_as_written() =>
        Assert.Equal("PRICE WATCH", L.T("PRICE WATCH"));

    [Fact]
    public void A_sentence_nobody_translated_stays_English()
    {
        var was = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = German;

            Assert.Equal("Nobody has translated this", L.T("Nobody has translated this"));
        }
        finally
        {
            CultureInfo.CurrentUICulture = was;
        }
    }

    /// <summary>
    /// "{0} days left" must not become "noch Tage": a lost placeholder drops the number, and an
    /// invented one throws a FormatException on the screen that shows it.
    /// </summary>
    [Fact]
    public void Every_translation_keeps_the_placeholders_of_its_English()
    {
        var wrong = Entries()
            .Where(entry => !Placeholders(entry.English).SetEquals(Placeholders(entry.German)))
            .Select(entry => $"{entry.English} => {entry.German}")
            .ToList();

        Assert.Empty(wrong);
    }

    [Fact]
    public void No_translation_is_left_empty() =>
        Assert.DoesNotContain(Entries(), entry => string.IsNullOrWhiteSpace(entry.German));

    /// <summary>
    /// Every sentence the code hands to L, and every one a view hands to ui:T, has its German.
    ///
    /// Read from the source rather than trusted: a key that differs from the code's by one
    /// character, a straight quote for a curly one, is a sentence that silently stays English.
    /// Only literals are checked; a key built at run time cannot be, and should not exist.
    /// </summary>
    [Fact]
    public void Every_sentence_in_the_code_has_its_German()
    {
        // As L looks them up: "TODAY" is found under "Today".
        var german = Entries().Select(entry => entry.English).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var root = RepositoryRoot();

        var missing = Directory
            .EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".cs") || path.EndsWith(".axaml"))
            .Where(path => Path.GetRelativePath(root, path)
                .Split(Path.DirectorySeparatorChar)
                .All(part => part is not ("obj" or "bin" or "GuildWarsCompanion.Tests") && !part.StartsWith('.')))
            .SelectMany(path => Keys(WithoutComments(File.ReadAllText(path))).Select(key => (path, key)))
            .Where(found => !german.Contains(found.key))
            .Select(found => $"{Path.GetRelativePath(root, found.path)}: {found.key}")
            .Distinct()
            .ToList();

        // Listed whole: Assert.Empty cuts each entry to thirty characters.
        Assert.True(missing.Count == 0, "No German for:\n" + string.Join("\n", missing));
    }

    /// <summary>Comments show how to call L, and their examples are not the app's text.</summary>
    private static string WithoutComments(string source) =>
        Comment().Replace(source, string.Empty);

    [GeneratedRegex(@"^[ \t]*//[^\n]*|<!--.*?-->", RegexOptions.Multiline | RegexOptions.Singleline)]
    private static partial Regex Comment();

    private static IEnumerable<string> Keys(string source)
    {
        // A long sentence is often split over lines with +; the key is the whole of it.
        foreach (Match match in CodeKey().Matches(source))
            yield return string.Concat(Piece().Matches(match.Groups[1].Value)
                .Select(piece => Regex.Unescape(piece.Groups[1].Value)));

        foreach (Match match in ContextKey().Matches(source))
            yield return $"{Regex.Unescape(match.Groups[1].Value)}|{Regex.Unescape(match.Groups[2].Value)}";

        foreach (Match match in ViewKey().Matches(source))
            yield return match.Groups[1].Value.Replace(@"\'", "'");
    }

    private static string RepositoryRoot()
    {
        var at = new DirectoryInfo(AppContext.BaseDirectory);
        while (at is not null && !File.Exists(Path.Combine(at.FullName, "GuildWarsCompanion.sln")))
            at = at.Parent;

        return at?.FullName ?? throw new InvalidOperationException("The repository was not found.");
    }

    /// <summary>L.T("…") or L.F("…", …): plain C# literals with their escapes, joined by +.</summary>
    [GeneratedRegex(@"\bL\.[TF]\(\s*(""(?:[^""\\]|\\.)*""(?:\s*\+\s*""(?:[^""\\]|\\.)*"")*)")]
    private static partial Regex CodeKey();

    /// <summary>One literal of those.</summary>
    [GeneratedRegex(@"""((?:[^""\\]|\\.)*)""")]
    private static partial Regex Piece();

    /// <summary>L.FIn("context", "…", …): filed as "context|…".</summary>
    [GeneratedRegex(@"\bL\.[TF]In\(\s*""((?:[^""\\]|\\.)*)""\s*,\s*""((?:[^""\\]|\\.)*)""")]
    private static partial Regex ContextKey();

    /// <summary>{ui:T '…'} in a view, with \' for an apostrophe.</summary>
    [GeneratedRegex(@"\{ui:T\s+'((?:[^'\\]|\\.)*)'\s*\}")]
    private static partial Regex ViewKey();

    private static IEnumerable<(string English, string German)> Entries()
    {
        var strings = new ResourceManager("GuildWarsCompanion.Localization.Strings", typeof(L).Assembly);
        var set = strings.GetResourceSet(new CultureInfo("de"), createIfNotExists: true, tryParents: false)
                  ?? throw new InvalidOperationException("No German resources were built.");

        foreach (DictionaryEntry entry in set)
            yield return ((string)entry.Key, (string)entry.Value!);
    }

    private static HashSet<string> Placeholders(string text) =>
        Placeholder().Matches(text).Select(match => match.Groups[1].Value).ToHashSet();

    [GeneratedRegex(@"\{(\d+)(?:[,:][^}]*)?\}")]
    private static partial Regex Placeholder();
}
