using Avalonia.Controls;
using Avalonia.Platform.Storage;

using GuildWarsCompanion.Ava.Shell;
using GuildWarsCompanion.Host;

using SkiaSharp;

using ZXing;
using ZXing.Common;

namespace GuildWarsCompanion.Ava.Host;

/// <summary>
/// What a scan is for: the line to show while looking, which payloads count, and what to say
/// about a code that was read but is not the right kind.
/// </summary>
/// <param name="Prompt">What to do with the camera, shown while it looks.</param>
/// <param name="Accept">Decides whether a decoded payload is the thing asked for. A code that
/// fails it does not end the scan — the user probably framed the wrong one.</param>
/// <param name="Picture">The same request for a picture chosen from disk, as the file dialog's
/// title: telling someone at a desktop to point a camera is wrong advice.</param>
public sealed record ScanRequest(string Prompt, Func<string, bool> Accept, string Rejected, string Picture);

/// <summary>
/// Reads one 2D code. On a phone that is the camera; on a desktop, a picture of one.
/// </summary>
public interface IQrScanner
{
    /// <summary>A live camera rather than a picture chosen from disk: decides what the buttons
    /// that start a scan say.</summary>
    bool UsesCamera { get; }

    /// <summary>The accepted payload, or null when the user backed out.</summary>
    Task<string?> ScanAsync(ScanRequest request);
}

/// <summary>Decoding shared by every scanner: any 2D format, not QR alone, as MAUI's reader does.</summary>
public static class Codes
{
    private static readonly DecodingOptions Options = new()
    {
        PossibleFormats = [BarcodeFormat.QR_CODE, BarcodeFormat.DATA_MATRIX, BarcodeFormat.AZTEC, BarcodeFormat.PDF_417],
        TryHarder = true
    };

    /// <summary>
    /// The payload of the first code in the picture, or null.
    ///
    /// No AutoRotate: QR codes read at any angle already, and turning a picture that holds no
    /// code three more ways made each miss four times slower — on the phone's camera that was
    /// most frames.
    /// </summary>
    public static string? Read(LuminanceSource source) =>
        new BarcodeReaderGeneric { Options = Options }.Decode(source)?.Text;
}

/// <summary>
/// The desktop's scanner: a picture of the code, chosen from disk. A webcam would be the literal
/// equivalent of the phone's camera, but the key is usually already on this screen — a
/// screenshot of the QR code is the shorter path.
/// </summary>
public sealed class ImageQrScanner(Shells shells, INavigator navigator) : IQrScanner
{
    public bool UsesCamera => false;

    public async Task<string?> ScanAsync(ScanRequest request)
    {
        if (TopLevel.GetTopLevel(shells.Current)?.StorageProvider is not { CanOpen: true } storage) return null;

        var picked = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = request.Picture,
            AllowMultiple = false,
            FileTypeFilter = [FilePickerFileTypes.ImageAll]
        });

        if (picked.Count == 0) return null;

        await using var stream = await picked[0].OpenReadAsync();
        var payload = Decode(stream);

        if (payload is null)
        {
            await navigator.AlertAsync(L.T("No code found"), L.T("That picture holds no code this app can read."), L.T("Close"));
            return null;
        }

        if (!request.Accept(payload))
        {
            await navigator.AlertAsync(L.T("Not the right code"), request.Rejected, L.T("Close"));
            return null;
        }

        return payload;
    }

    private static string? Decode(Stream stream)
    {
        using var decoded = SKBitmap.Decode(stream);
        if (decoded is null) return null;

        using var bitmap = decoded.ColorType == SKColorType.Bgra8888
            ? decoded.Copy()
            : decoded.Copy(SKColorType.Bgra8888);

        var source = new RGBLuminanceSource(bitmap.Bytes, bitmap.Width, bitmap.Height,
            RGBLuminanceSource.BitmapFormat.BGRA32);

        return Codes.Read(source);
    }
}
