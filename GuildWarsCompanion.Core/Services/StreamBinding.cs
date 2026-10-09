namespace GuildWarsCompanion.Services;

/// <summary>
/// How the compass stream's sockets are put on the right network.
///
/// The same problem <see cref="IBridgeTransport"/> exists for, and a different answer, because
/// SignalR will not accept the answer that one uses. <c>WifiBridgeTransport</c> works by handing
/// back a <c>Java.Net.HttpURLConnection</c> from <c>Network.OpenConnection</c>; SignalR is built
/// on <c>HttpClient</c> and has no way to be given one. What it will accept is an
/// <see cref="HttpMessageHandler"/>, so the binding has to happen one level down — inside the
/// handler's own connect.
///
/// Measured on 2026-09-14: this is why the transport is server-sent events rather than
/// WebSockets. <c>ClientWebSocket</c> does not go through the supplied handler at all, so a
/// WebSocket connection cannot be bound this way and never reaches the PC. See ROADMAP.md,
/// "SignalR for the compass, HTTP for everything else".
/// </summary>
public interface IStreamBinding
{
    /// <summary>
    /// A handler whose connections land on the network the bridge is on, or null to let SignalR
    /// use its own.
    ///
    /// Null is the ordinary answer on any platform or configuration where the routing table
    /// would have got it right anyway, and it is not a failure.
    /// </summary>
    HttpMessageHandler? Handler();
}

/// <summary>
/// Lets SignalR do whatever it would have done.
///
/// Correct wherever the bridge is on the default network, which is most places, and the only
/// possible answer off Android.
/// </summary>
public sealed class DefaultStreamBinding : IStreamBinding
{
    public HttpMessageHandler? Handler() => null;
}
