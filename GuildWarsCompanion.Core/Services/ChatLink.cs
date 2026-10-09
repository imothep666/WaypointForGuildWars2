namespace GuildWarsCompanion.Services;

/// <summary>
/// The code the game turns into a clickable item in chat: <c>[&amp;AgEJTQAA]</c> is one Glob of
/// Ectoplasm.
///
/// Built here rather than read from the API's <c>chat_link</c>, because the API only ever writes
/// a quantity of one, and because six bytes are not worth a request or a cache column.
///
/// The layout, per the wiki's "Chat link format": a header byte of 2 for an item, the quantity as
/// one byte, then the item id as three bytes, least significant first, and one byte of flags for
/// skins and upgrades, which a plain item leaves at zero. Base64, inside <c>[&amp;</c> and
/// <c>]</c>.
/// </summary>
public static class ChatLink
{
    /// <summary>A full stack. The game will not link more than it can stack.</summary>
    public const int MostItems = 250;

    /// <summary>The highest id three bytes can carry.</summary>
    private const int MostId = 0xFFFFFF;

    public static string Item(int id, int count = 1)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(id, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(id, MostId);
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, MostItems);

        byte[] code = [2, (byte)count, (byte)id, (byte)(id >> 8), (byte)(id >> 16), 0];

        return $"[&{Convert.ToBase64String(code)}]";
    }
}
