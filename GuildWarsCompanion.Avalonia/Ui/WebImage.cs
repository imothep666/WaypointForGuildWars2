using System.Collections.Concurrent;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace GuildWarsCompanion.Ava.Ui;

/// <summary>
/// An image from a URL, which MAUI's Image does by itself and Avalonia's does not:
/// <c>ui:WebImage.Url="{Binding IconUrl}"</c>.
///
/// Every icon the API names comes from render.guildwars2.com and is the same file for every
/// account, so each URL is fetched once per run and shared. A failed fetch leaves the image
/// empty, the way MAUI's does, rather than failing the row.
/// </summary>
public static class WebImage
{
    public static readonly AttachedProperty<string?> UrlProperty =
        AvaloniaProperty.RegisterAttached<Image, string?>("Url", typeof(WebImage));

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private static readonly ConcurrentDictionary<string, Task<Bitmap?>> Loaded = new();

    static WebImage() => UrlProperty.Changed.AddClassHandler<Image>(OnUrlChanged);

    public static string? GetUrl(Image image) => image.GetValue(UrlProperty);

    public static void SetUrl(Image image, string? value) => image.SetValue(UrlProperty, value);

    private static async void OnUrlChanged(Image image, AvaloniaPropertyChangedEventArgs e)
    {
        image.Source = null;

        if (e.NewValue is not string { Length: > 0 } url) return;

        var bitmap = await Loaded.GetOrAdd(url, Fetch);

        // A recycled row may have moved on to another URL while this one was loading.
        if (GetUrl(image) == url) await Dispatcher.UIThread.InvokeAsync(() => image.Source = bitmap);
    }

    private static async Task<Bitmap?> Fetch(string url)
    {
        try
        {
            var bytes = await Http.GetByteArrayAsync(url);

            return new Bitmap(new MemoryStream(bytes));
        }
        catch (Exception)
        {
            return null;
        }
    }
}
