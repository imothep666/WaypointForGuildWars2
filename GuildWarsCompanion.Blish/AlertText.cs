using System;
using System.Collections.Generic;
using System.Text;

namespace GuildWarsCompanion.Blish;

/// <summary>
/// An alert's words broken into lines that fit its box. Kept apart from Blish HUD, like
/// <see cref="CardText"/>, so the tests can compile it: the font is passed in as a way of
/// measuring a line, and nothing here draws.
/// </summary>
public static class AlertText
{
    /// <summary>
    /// Lines no wider than <paramref name="width"/>, broken between words. A word too long for a
    /// line on its own — a link, a run of characters with no space — is broken where it has to be,
    /// so nothing ever runs past the edge.
    /// </summary>
    public static IReadOnlyList<string> Wrap(string text, double width, Func<string, double> measure)
    {
        var lines = new List<string>();
        var line = new StringBuilder();

        foreach (var word in (text ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var longer = line.Length == 0 ? word : line + " " + word;

            if (measure(longer) <= width)
            {
                line.Clear().Append(longer);
                continue;
            }

            if (line.Length > 0) lines.Add(line.ToString());
            line.Clear();

            var rest = word;

            while (measure(rest) > width)
            {
                var fits = Fits(rest, width, measure);
                lines.Add(rest.Substring(0, fits));
                rest = rest.Substring(fits);
            }

            line.Append(rest);
        }

        if (line.Length > 0) lines.Add(line.ToString());

        return lines;
    }

    /// <summary>How many characters from the start of <paramref name="word"/> fit; at least one,
    /// so a line narrower than a single character still moves on.</summary>
    private static int Fits(string word, double width, Func<string, double> measure)
    {
        var fits = 1;

        while (fits < word.Length && measure(word.Substring(0, fits + 1)) <= width) fits++;

        return fits;
    }
}
