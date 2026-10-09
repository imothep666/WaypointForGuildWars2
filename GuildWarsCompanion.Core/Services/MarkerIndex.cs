using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace GuildWarsCompanion.Services;

/// <summary>
/// One downloaded pack: where it came from, what the server said about it, and what it gave.
/// </summary>
/// <param name="Id">A stable name derived from the URL, used for this pack's own file.</param>
/// <param name="Tag">The server's <c>ETag</c>, or empty. Sent back on the next fetch.</param>
/// <param name="Modified">The server's <c>Last-Modified</c>, or empty. The fallback validator for
/// a host that sets no tag.</param>
/// <param name="Routes">
/// How many trails were kept from this pack. Zero for the many packs that carry none, and for
/// any line written before routes existed — such a line simply has no eighth field, which
/// <see cref="MarkerIndex.Read"/> treats as absent rather than as a broken file.
/// </param>
/// <param name="Imported">
/// Which version of this app's own import read the pack. Zero for one read before the number
/// existed. See <see cref="MarkerStore"/>: it is what stops a pack answering 304 forever with
/// data an older import never collected.
/// </param>
public sealed record MarkerPackRecord(
    string Id, string Url, string Name, string Tag, string Modified, int Count, int Maps,
    int Routes = 0, int Imported = 0);

/// <summary>
/// The list of packs on the device.
///
/// A pack per line, the same tab-separated shape as the markers themselves and for the same
/// reasons: this file is written and read here alone, it is small, and it can be looked at when
/// somebody asks why a chest is where it is.
///
/// <b>A pack gets its own file, keyed by <see cref="Name"/> of the URL.</b> Removing one pack then
/// costs deleting one file rather than re-reading and rewriting everything the others contributed,
/// and a failed download cannot corrupt a pack that was already working.
/// </summary>
public static class MarkerIndex
{
    public const string Version = "packs 1";

    /// <summary>
    /// A file name for a URL: eight bytes of SHA-256, in hex.
    ///
    /// Not the URL itself — it carries slashes, colons and query strings, none of which belong in
    /// a file name — and not a counter, because the same pack added twice has to land on the same
    /// file rather than accumulate copies. A hash is stable across runs and across devices, which
    /// a counter is not.
    /// </summary>
    public static string Name(string url)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(url.Trim()));

        return Convert.ToHexString(digest.AsSpan(0, 8)).ToLowerInvariant();
    }

    /// <summary>
    /// What to call a pack on screen: the last part of its address, without the extension.
    ///
    /// Packs do not publish a name anywhere this reads, and "Metal-Marker-Myriad" off the end of
    /// the URL is a better answer than a hash or the whole address.
    /// </summary>
    /// <param name="fileName">
    /// The name the server gave the file in <c>Content-Disposition</c>, which wins when there is
    /// one: Tekkit's address ends <c>download?download=1:tw-all-in-one</c> and its pack was called
    /// "download", while the server names the file <c>tw_ALL_IN_ONE.taco</c>.
    /// </param>
    public static string Title(string url, string? fileName = null)
    {
        if (fileName?.Trim().Trim('"') is { Length: > 0 } given) return WithoutExtension(Path.GetFileName(given));

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var address)) return url.Trim();

        var last = address.Segments.LastOrDefault()?.Trim('/') ?? string.Empty;

        return last.Length == 0 ? address.Host : WithoutExtension(last);
    }

    private static string WithoutExtension(string name)
    {
        var dot = name.LastIndexOf('.');

        return dot > 0 ? name[..dot] : name;
    }

    public static string Write(IEnumerable<MarkerPackRecord> packs)
    {
        var text = new StringBuilder().AppendLine(Version);

        foreach (var pack in packs)
            text.Append(Clean(pack.Id)).Append('\t')
                .Append(Clean(pack.Url)).Append('\t')
                .Append(Clean(pack.Name)).Append('\t')
                .Append(Clean(pack.Tag)).Append('\t')
                .Append(Clean(pack.Modified)).Append('\t')
                .Append(pack.Count.ToString(CultureInfo.InvariantCulture)).Append('\t')
                .Append(pack.Maps.ToString(CultureInfo.InvariantCulture)).Append('\t')
                .Append(pack.Routes.ToString(CultureInfo.InvariantCulture)).Append('\t')
                .AppendLine(pack.Imported.ToString(CultureInfo.InvariantCulture));

        return text.ToString();
    }

    public static IReadOnlyList<MarkerPackRecord> Read(string text)
    {
        var packs = new List<MarkerPackRecord>();

        using var lines = new StringReader(text);

        if (lines.ReadLine() is not { } header || header.Trim() != Version) return packs;

        while (lines.ReadLine() is { } line)
        {
            if (line.Length == 0) continue;

            var parts = line.Split('\t');

            if (parts.Length < 7) continue;

            if (!int.TryParse(parts[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) ||
                !int.TryParse(parts[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out var maps))
                continue;

            // Routes arrived after this file's first version, so a line written before then has
            // seven fields rather than eight. Read as absent rather than as a broken line: a pack
            // that works keeps working, and the count fills itself in on the next update.
            var routes = parts.Length > 7 &&
                         int.TryParse(parts[7], NumberStyles.Integer, CultureInfo.InvariantCulture,
                             out var parsed)
                ? parsed
                : 0;

            var imported = parts.Length > 8 &&
                           int.TryParse(parts[8], NumberStyles.Integer, CultureInfo.InvariantCulture,
                               out var read)
                ? read
                : 0;

            packs.Add(new MarkerPackRecord(
                parts[0], parts[1], parts[2], parts[3], parts[4], count, maps, routes, imported));
        }

        return packs;
    }

    /// <summary>A value with nothing in it that would break the line it is written on. An ETag is
    /// a quoted string from a stranger's server, so it gets the same treatment as a name.</summary>
    private static string Clean(string? value) =>
        value is null ? string.Empty : value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
}
