using GuildWarsCompanion.Host;

namespace GuildWarsCompanion.Services;

/// <summary>
/// The nearest the app can come to opening the trading post on an item.
///
/// The game has no way in from outside, and a module that pressed the keys for the player would be
/// a macro; but the trading post's search takes a chat link and finds exactly that item. So the link
/// goes where the game can paste it: the paired PC's clipboard, or this phone's when none is paired
/// or none answers. The same route an item page's COPY CHAT LINK takes, worded for the search.
///
/// Shared by every place that has an item the player is about to go to the trading post for: a
/// watch, an order the market has moved past, and the notification that a watched price arrived.
/// </summary>
public sealed class TradingPostLink(BridgeClient bridge, ISystemActions system)
{
    /// <summary>
    /// Sends the link and says where it went, in a sentence for a status line or a notification.
    /// </summary>
    /// <param name="sending">Told before the network is asked, so a button never looks dead while
    /// a PC that is asleep runs out its timeout.</param>
    public async Task<string> SendAsync(
        int itemId, string name, Action<string>? sending = null, CancellationToken ct = default)
    {
        var link = ChatLink.Item(itemId);

        if (!await bridge.IsSetUpAsync())
        {
            await system.CopyAsync(link);

            return L.F("Copied the link for {0}. Paste it into the trading post's search.", name);
        }

        sending?.Invoke(L.F("Sending {0}…", link));

        BridgeSend sent;
        try
        {
            sent = await bridge.SendAsync(link, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // A caller with a deadline, the notification's button: what the bridge did not
            // manage in time is the same as no PC answering.
            sent = new BridgeSend(BridgeOutcome.NoAnswer);
        }

        // Named, not "the PC": with several paired, the wrong machine has to be visible.
        if (sent.IsSent)
            return L.F("Sent the link for {0} to {1}. Paste it into the trading post's search.", name, sent.Pc);

        await system.CopyAsync(link);

        return L.F("{0} · {1} · copied here", link, sent.Text);
    }
}
