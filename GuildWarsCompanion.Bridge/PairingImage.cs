using Avalonia.Media.Imaging;

using GuildWarsCompanion.Bridge.Protocol;

using QRCoder;

namespace GuildWarsCompanion.Bridge;

/// <summary>
/// Draws a pairing card as a QR code for the phone's camera.
///
/// <see cref="PngByteQRCode"/> rather than QRCoder's bitmap renderers on purpose: those pull in
/// System.Drawing, which is Windows-only, and this app has to draw the same window on Linux.
/// PNG bytes are the one output every Avalonia backend can load.
///
/// The image is worth more than the typing it saves. A code short enough to read across a room
/// is short enough to brute-force offline from a single challenge, so a scanned pairing is the
/// only one that can prove which PC answered — which is what lets the phone trust an address
/// nobody typed. See <see cref="BridgeProtocol.IsStrong"/>.
/// </summary>
public static class PairingImage
{
    /// <summary>
    /// The card as a bitmap, or null when there is nothing to draw.
    ///
    /// Null when this install predates identities and has no secret to offer — the window then
    /// shows the typed code alone, which is exactly what it always showed.
    /// </summary>
    public static Bitmap? For(BridgeCard? card)
    {
        if (card is null) return null;

        try
        {
            using var data = QRCodeGenerator.GenerateQrCode(
                card.Encode(), QRCodeGenerator.ECCLevel.M);

            // Eight pixels a module keeps a 260-bit payload readable by a phone held at arm's
            // length without the window having to be resized around it.
            var png = new PngByteQRCode(data).GetGraphic(8);

            return new Bitmap(new MemoryStream(png));
        }
        catch (Exception)
        {
            // A window that cannot draw the code still pairs by hand. Losing the picture is not
            // worth losing the window over.
            return null;
        }
    }
}
