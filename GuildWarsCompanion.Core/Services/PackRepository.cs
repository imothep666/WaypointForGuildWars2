using System.Globalization;
using System.Text.Json;
using GuildWarsCompanion.Host;

namespace GuildWarsCompanion.Services;

/// <summary>One marker pack a repository offers. Size and date are null where the list does not say.</summary>
public sealed record PackListing(
    string Name,
    string Download,
    string? Description,
    string? Author,
    IReadOnlyList<string> Categories,
    double? SizeMb,
    DateTimeOffset? Updated,
    int Maps);

/// <summary>What a repository offered, and what of it was left out.</summary>
/// <param name="Skipped">Listings with no usable https address; the app never fetches plain http.</param>
public sealed record PackCatalogue(IReadOnlyList<PackListing> Packs, int Skipped);

/// <summary>
/// The list of marker packs to choose from, instead of an address typed on a phone.
///
/// <b>An index, not packs.</b> The repository names each pack's own download address; adding one
/// still fetches it from its author through <see cref="MarkerStore.AddAsync"/>, so nothing about the
/// "downloaded, not shipped" rule changes.
///
/// Blish HUD's repository is the default, because it says what a choice needs: a description,
/// categories, the size in megabytes, the last update and the maps covered. TacO's own
/// <c>MarkerPacks.json</c> is read too, for anyone who points the setting at it; it carries names
/// and addresses only. The address is a setting so a list that moves does not need a new build.
///
/// Fetched at most once a day (about 37 KB), kept on the phone, and the last copy used offline.
/// Only a copy that parses replaces the kept one.
/// </summary>
public sealed class PackRepository(AppEnvironment environment, ISettings settings)
{
    public const string DefaultAddress = "https://mp-repo.blishhud.com/repo.json";

    private const string AddressKey = "markers.repository";

    /// <summary>A day: authors release often enough that a week would hide a new pack.</summary>
    private static readonly TimeSpan Fresh = TimeSpan.FromDays(1);

    private static readonly HttpClient Http = Client();

    private string FilePath => Path.Combine(environment.DataDirectory, "packrepo.json");

    public string Address
    {
        get => settings.Get(AddressKey, DefaultAddress);
        set
        {
            if (string.IsNullOrWhiteSpace(value) || value.Trim() == DefaultAddress) settings.Remove(AddressKey);
            else settings.Set(AddressKey, value.Trim());

            // A different list: the kept copy belongs to the old one.
            if (File.Exists(FilePath)) File.Delete(FilePath);
        }
    }

    public bool IsDefault => Address == DefaultAddress;

    /// <summary>Why there is no list, or why it may be old; null when it is fresh.</summary>
    public string? Problem { get; private set; }

    public async Task<PackCatalogue> GetAsync(bool force = false, CancellationToken ct = default)
    {
        Problem = null;

        var stored = File.Exists(FilePath) ? new FileInfo(FilePath) : null;

        if (force || stored is null || DateTimeOffset.UtcNow - stored.LastWriteTimeUtc > Fresh)
            await DownloadAsync(ct);

        if (!File.Exists(FilePath)) return new PackCatalogue([], 0);

        try
        {
            return Parse(await File.ReadAllTextAsync(FilePath, ct));
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            Problem = "The kept copy of the list could not be read.";
            return new PackCatalogue([], 0);
        }
    }

    private async Task DownloadAsync(CancellationToken ct)
    {
        if (!Uri.TryCreate(Address, UriKind.Absolute, out var address) || address.Scheme != Uri.UriSchemeHttps)
        {
            Problem = "The list's address is not an https address.";
            return;
        }

        try
        {
            using var leash = CancellationTokenSource.CreateLinkedTokenSource(ct);
            leash.CancelAfter(TimeSpan.FromSeconds(20));

            var json = await Http.GetStringAsync(address, leash.Token);

            if (Parse(json).Packs.Count == 0)
            {
                Problem = "That address answered, but not with a list of marker packs.";
                return;
            }

            await File.WriteAllTextAsync(FilePath, json, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException
                                       or InvalidOperationException)
        {
            if (ct.IsCancellationRequested) throw;

            Problem = File.Exists(FilePath)
                ? "The list could not be fetched just now; this is the last copy."
                : $"The list could not be fetched: {ex.Message}";
        }
    }

    /// <summary>
    /// Either format: Blish HUD's array of listings, or TacO's object with a <c>markerpacks</c> array.
    /// Read through <see cref="JsonDocument"/>, which the trimmer leaves alone, and field by field,
    /// so one odd entry costs that entry rather than the list.
    /// </summary>
    public static PackCatalogue Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var entries = root.ValueKind switch
        {
            JsonValueKind.Array => root,
            JsonValueKind.Object when root.TryGetProperty("markerpacks", out var taco)
                                      && taco.ValueKind == JsonValueKind.Array => taco,
            _ => default
        };

        if (entries.ValueKind != JsonValueKind.Array) return new PackCatalogue([], 0);

        var packs = new List<PackListing>();
        var skipped = 0;

        foreach (var entry in entries.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object) continue;

            var name = Text(entry, "Name") ?? Text(entry, "name");
            var download = Text(entry, "Download") ?? Text(entry, "downloadurl");

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(download)) continue;

            if (!Uri.TryCreate(download, UriKind.Absolute, out var address) || address.Scheme != Uri.UriSchemeHttps)
            {
                skipped++;
                continue;
            }

            packs.Add(new PackListing(
                name.Trim(),
                download.Trim(),
                Plain(Text(entry, "Description")),
                Text(entry, "AuthorName"),
                (Text(entry, "Categories") ?? "")
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                // ReActif's listing says 0 MB and the year 1: that is "unknown", not an empty pack.
                Number(entry, "Size") is > 0 and var size ? size : null,
                Date(entry, "LastUpdate") is { Year: > 2000 } date ? date : null,
                entry.TryGetProperty("MapIds", out var maps) && maps.ValueKind == JsonValueKind.Array
                    ? maps.GetArrayLength()
                    : 0));
        }

        // In the list's own order: Blish HUD's is alphabetical, TacO's is curated.
        return new PackCatalogue(packs, skipped);
    }

    /// <summary>
    /// A description as one run of text. Authors write blank lines and Discord links on lines of
    /// their own, which spent the three lines a row shows on nothing; and uL Vias writes
    /// non-breaking hyphens, which Barlow has no glyph for, so they drew as a fallback font's
    /// underscore on a taller line.
    /// </summary>
    private static string? Plain(string? text) =>
        text is null
            ? null
            : string.Join(' ', text.Replace('‑', '-').Replace('‐', '-')
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string? Text(JsonElement entry, string name) =>
        entry.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() is { Length: > 0 } text ? text : null
            : null;

    private static double? Number(JsonElement entry, string name) =>
        entry.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;

    /// <summary>Dates come with and without a zone; one without is taken as UTC.</summary>
    private static DateTimeOffset? Date(JsonElement entry, string name) =>
        Text(entry, name) is { } text && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var date)
            ? date
            : null;

    private static HttpClient Client()
    {
        var http = new HttpClient();

        http.DefaultRequestHeaders.UserAgent.ParseAdd("GuildWarsCompanion/1.0");

        return http;
    }
}
