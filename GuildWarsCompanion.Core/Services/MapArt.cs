using System.Text.Json;

using GuildWarsCompanion.Host;

namespace GuildWarsCompanion.Services;

/// <summary>
/// A map's loading screen from the Guild Wars 2 Wiki, kept as a file in the cache directory.
///
/// In Core rather than beside the app's other wiki art so the Here widget can have it too: the
/// widget is drawn by Android from a file, often while no screen of the app has been opened, and
/// the band over Today and the map reads the same file. Fetched once per map and kept, so what was
/// seen before still shows with no connection. ArenaNet's painting, used as their Content Terms of
/// Use allow fan projects to: fetched when first needed rather than shipped, and credited in
/// Settings.
/// </summary>
public sealed class MapArt(AppEnvironment environment)
{
    private static readonly HttpClient Http = CreateClient();

    /// <summary>Where a map's painting is kept, whether or not it is there yet. The wiki files
    /// loading screens under English map names, so <paramref name="map"/> is one.</summary>
    public string FileFor(string map) =>
        Path.Combine(environment.CacheDirectory, "mapart", string.Concat(map.Split(Path.GetInvalidFileNameChars())) + ".png");

    /// <summary>The kept painting, or null without touching the network.</summary>
    public string? Held(string map) => File.Exists(FileFor(map)) ? FileFor(map) : null;

    /// <summary>
    /// The painting's file, fetched first when it is not kept yet. Null where the wiki has no
    /// loading screen by the usual name, and when it cannot be reached: it is decoration, and the
    /// next look asks again.
    /// </summary>
    public async Task<string?> FetchAsync(string map, CancellationToken ct = default)
    {
        if (Held(map) is { } held) return held;

        try
        {
            if (await DownloadAsync(map, ct) is not { } bytes) return null;

            var file = FileFor(map);

            Directory.CreateDirectory(Path.GetDirectoryName(file)!);

            // Written beside and moved, so a widget drawn mid-download never reads half a picture.
            var part = file + ".part";
            await File.WriteAllBytesAsync(part, bytes, ct);
            File.Move(part, file, overwrite: true);

            return file;
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <summary>The wiki's 900-pixel copy of "&lt;map&gt; loading screen", JPEG or PNG as it keeps it.</summary>
    private static async Task<byte[]?> DownloadAsync(string map, CancellationToken ct)
    {
        string[] titles = [$"File:{map} loading screen.jpg", $"File:{map} loading screen.png"];

        var query = "https://wiki.guildwars2.com/api.php?action=query&format=json&prop=imageinfo" +
                    $"&iiprop=url&iiurlwidth=900&titles={Uri.EscapeDataString(string.Join("|", titles))}";

        using var answer = JsonDocument.Parse(await Http.GetStringAsync(query, ct));

        foreach (var page in answer.RootElement.GetProperty("query").GetProperty("pages").EnumerateObject())
        {
            if (!page.Value.TryGetProperty("imageinfo", out var info) || info.GetArrayLength() == 0) continue;

            var first = info[0];
            var url = first.TryGetProperty("thumburl", out var thumb) ? thumb.GetString() : first.GetProperty("url").GetString();

            if (url is not null) return await Http.GetByteArrayAsync(url, ct);
        }

        return null;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        // The wiki asks every automated client to say who it is.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Waypoint/1.0 (unofficial Guild Wars 2 fan app)");

        return client;
    }
}
