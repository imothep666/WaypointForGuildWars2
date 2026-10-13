using System.Collections.Concurrent;
using System.Text.Json;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

using GuildWarsCompanion.Host;
using GuildWarsCompanion.Services;

using Microsoft.Extensions.DependencyInjection;

using SkiaSharp;

namespace GuildWarsCompanion.Ava.Ui;

/// <summary>
/// ArenaNet's paintings, as the Guild Wars 2 Wiki keeps them, for the mastheads.
///
/// An elite specialization's splash — "Spec image Deadeye.jpg" — is painted on a white ground.
/// White does not sit on this app's obsidian, and simply making it transparent leaves the ink
/// behind: a dark figure on a dark page, gone. So each pixel keeps its own colour and only its
/// lightness decides how much of it shows: paper vanishes, the grey splatter reads as smoke, and
/// the figure stays as painted.
///
/// A map's loading screen — "Crystal Oasis loading screen.jpg" — is a full painting, kept as it is.
///
/// Each is fetched once and kept in the cache directory, so what was seen before still shows with
/// no connection. ArenaNet's content, used as their Content Terms of Use allow fan projects to:
/// fetched from the wiki when first needed rather than shipped, and credited in Settings.
/// </summary>
public static class WikiArt
{
    /// <summary>How strongly darkness becomes cover. Gentle: the art stands behind text.</summary>
    private const float Cover = 2.2f;

    private static readonly HttpClient Http = CreateClient();
    private static readonly ConcurrentDictionary<string, Task<Bitmap?>> Loaded = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The painting of an elite specialization, lifted off its paper; null where there is none.</summary>
    public static Task<Bitmap?> ForEliteAsync(string elite) =>
        Load(new Kind("specart", elite, [$"Spec image {elite}.jpg", .. Concept(elite)], Lift: true));

    /// <summary>
    /// The End of Dragons specializations have no "Spec image" on the wiki; their splash is filed
    /// as concept art, numbered. These are the finished paintings on white, not the silhouette
    /// studies the series also holds.
    /// </summary>
    private static string[] Concept(string elite) => elite switch
    {
        "Bladesworn" => ["Bladesworn concept art 2.jpg"],
        "Catalyst" => ["Catalyst concept art 2.jpg"],
        "Harbinger" => ["Harbinger concept art 2.jpg"],
        "Mechanist" => ["Mechanist concept art 2.jpg"],
        "Specter" => ["Specter concept art 3.jpg"],
        "Untamed" => ["Untamed concept art 2.jpg"],
        "Vindicator" => ["Vindicator concept art 2.jpg"],
        "Virtuoso" => ["Virtuoso concept art 3.jpg"],
        "Willbender" => ["Willbender concept art 2.jpg"],
        _ => []
    };

    /// <summary>
    /// A core profession's painting, lifted off its paper, for a character with no elite
    /// specialization. The wiki has no one series for all nine, so each is the profession's
    /// reveal art that reads best at the top of a page: Guardian, Warrior, Ranger and Thief
    /// share one set; the others are the nearest in spirit.
    /// </summary>
    public static Task<Bitmap?> ForProfessionAsync(string profession) =>
        ProfessionArt.TryGetValue(profession, out var title)
            ? Load(new Kind("professionart", profession, [title], Lift: true))
            : Task.FromResult<Bitmap?>(null);

    private static readonly Dictionary<string, string> ProfessionArt = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Guardian"] = "Guardian 01 concept art.png",
        ["Warrior"] = "Warrior 01 concept art.png",
        ["Engineer"] = "Engineer 02 concept art.jpg",
        ["Ranger"] = "Ranger 01 concept art.png",
        ["Thief"] = "Thief 01 concept art.png",
        ["Elementalist"] = "Elementalist 01 concept art.jpg",
        ["Mesmer"] = "Mesmer 02 concept art.png",
        ["Necromancer"] = "Necromancer 01 concept art.jpg",
        ["Revenant"] = "Revenant 01 concept art.jpg"
    };

    /// <summary>A map's loading screen; null where the wiki has none by the usual name. Fetched and
    /// kept by <see cref="MapArt"/> in Core, whose file the Here widget draws from too.</summary>
    public static Task<Bitmap?> ForMapAsync(string map) =>
        Loaded.GetOrAdd($"mapart/{map}", _ => LoadMapAsync(map));

    private static async Task<Bitmap?> LoadMapAsync(string map)
    {
        try
        {
            if (await App.Services.GetRequiredService<MapArt>().FetchAsync(map) is { } file) return new Bitmap(file);
        }
        catch (Exception)
        {
            // As any other painting: the page stays on its wash.
        }

        Loaded.TryRemove($"mapart/{map}", out _);
        return null;
    }

    /// <summary>A Wizard's Vault reward's banner painting, whole: it is framed already, and opaque it
    /// hides the icon beneath. Null where there is none.</summary>
    public static Task<Bitmap?> ForBannerAsync(string reward) =>
        Load(new Kind("vaultbanner", reward, BannerTitles(reward), Lift: false));

    /// <summary>The wiki files a skin's banner under the armour piece's name: "Ward Hunter's Coat
    /// banner.png" for the reward the game calls "Ward Hunter's Coat Skin".</summary>
    private static string[] BannerTitles(string reward)
    {
        var bare = reward.EndsWith(" Skin", StringComparison.Ordinal) ? reward[..^5] : reward;

        return bare == reward
            ? [$"{reward} banner.png", $"{reward} banner.jpg"]
            : [$"{reward} banner.png", $"{bare} banner.png", $"{bare} banner.jpg"];
    }

    /// <summary>
    /// <c>ui:WikiArt.Banner="{Binding BannerName}"</c>: an image that shows a reward's banner once
    /// it has arrived, and nothing until then or where there is none — so it can sit over the
    /// reward's own icon and cover it only when there is something better.
    /// </summary>
    public static readonly AttachedProperty<string?> BannerProperty =
        AvaloniaProperty.RegisterAttached<Image, string?>("Banner", typeof(WikiArt));

    static WikiArt() => BannerProperty.Changed.AddClassHandler<Image>(OnBannerChanged);

    public static string? GetBanner(Image image) => image.GetValue(BannerProperty);

    public static void SetBanner(Image image, string? value) => image.SetValue(BannerProperty, value);

    private static async void OnBannerChanged(Image image, AvaloniaPropertyChangedEventArgs e)
    {
        image.Source = null;

        if (e.NewValue is not string { Length: > 0 } reward) return;

        var banner = await ForBannerAsync(reward);

        // A recycled row may have moved on to another reward while this one was loading.
        if (GetBanner(image) == reward) await Dispatcher.UIThread.InvokeAsync(() => image.Source = banner);
    }

    private sealed record Kind(string Folder, string Name, string[] Titles, bool Lift)
    {
        public string Key => $"{Folder}/{Name}";
    }

    private static Task<Bitmap?> Load(Kind kind) => Loaded.GetOrAdd(kind.Key, _ => LoadAsync(kind));

    private static async Task<Bitmap?> LoadAsync(Kind kind)
    {
        try
        {
            var folder = Path.Combine(App.Services.GetRequiredService<AppEnvironment>().CacheDirectory, kind.Folder);
            var file = Path.Combine(folder, string.Concat(kind.Name.Split(Path.GetInvalidFileNameChars())) + ".png");

            if (!File.Exists(file))
            {
                if (await FetchAsync(kind) is not { } painted) return null;

                Directory.CreateDirectory(folder);
                await File.WriteAllBytesAsync(file, painted);
            }

            return new Bitmap(file);
        }
        catch (Exception)
        {
            // Decoration: a failed fetch leaves the page on its wash, as an offline first visit
            // does, and the next visit asks again.
            Loaded.TryRemove(kind.Key, out _);
            return null;
        }
    }

    /// <summary>Asks the wiki where its 900-pixel copy of the first of the files it has is, and
    /// fetches it, lifted off its paper where asked. PNG bytes, or null when it has none.</summary>
    private static async Task<byte[]?> FetchAsync(Kind kind)
    {
        var titles = Uri.EscapeDataString(string.Join("|", kind.Titles.Select(title => $"File:{title}")));
        var query = "https://wiki.guildwars2.com/api.php?action=query&format=json&prop=imageinfo" +
                    $"&iiprop=url&iiurlwidth=900&titles={titles}";

        using var answer = JsonDocument.Parse(await Http.GetStringAsync(query));

        string? url = null;

        foreach (var page in answer.RootElement.GetProperty("query").GetProperty("pages").EnumerateObject())
        {
            if (!page.Value.TryGetProperty("imageinfo", out var info) || info.GetArrayLength() == 0) continue;

            var first = info[0];
            url ??= first.TryGetProperty("thumburl", out var thumb) ? thumb.GetString() : first.GetProperty("url").GetString();
        }

        if (url is null) return null;

        using var source = SKBitmap.Decode(await Http.GetByteArrayAsync(url));
        if (source is null) return null;

        if (!kind.Lift) return Encode(source);

        using var lifted = new SKBitmap(new SKImageInfo(source.Width, source.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));

        for (var y = 0; y < source.Height; y++)
        for (var x = 0; x < source.Width; x++)
        {
            var c = source.GetPixel(x, y);
            var lightness = 0.299f * c.Red + 0.587f * c.Green + 0.114f * c.Blue;
            var alpha = (byte)Math.Clamp((255 - lightness) * Cover, 0, 255);

            lifted.SetPixel(x, y, new SKColor(c.Red, c.Green, c.Blue, alpha));
        }

        return Encode(lifted);
    }

    private static byte[] Encode(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);

        return png.ToArray();
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        // The wiki asks every automated client to say who it is.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Waypoint/1.0 (unofficial Guild Wars 2 fan app)");

        return client;
    }
}
