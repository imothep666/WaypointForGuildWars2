namespace GuildWarsCompanion.Host;

/// <summary>
/// Turns PNG or JPEG bytes into something the map and compass can draw.
///
/// The drawing is written against <see cref="IImage"/>, but only a platform knows how to make one:
/// MAUI decodes into a native bitmap, a Skia-based front end into a Skia one.
/// </summary>
public interface IImageDecoder
{
    /// <summary>The decoded image, or null if the bytes are not one.</summary>
    IImage? Decode(Stream stream);
}
