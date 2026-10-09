using Android.Content;
using Android.Net;
using Android.OS;

using GuildWarsCompanion.Services;

using NetworkStream = System.IO.Stream;
using Socket = System.Net.Sockets.Socket;
using SocketType = System.Net.Sockets.SocketType;
using ProtocolType = System.Net.Sockets.ProtocolType;

namespace GuildWarsCompanion.Platforms.Android;

/// <summary>
/// Puts every socket the compass stream opens onto Wi-Fi, whatever the default network is.
///
/// <see cref="SocketsHttpHandler.ConnectCallback"/> is the only place .NET hands over a socket
/// before it is used, which makes it the only place a network binding can be applied. Everything
/// else about the problem is described on <see cref="WifiBridgeTransport"/>: a phone whose Wi-Fi
/// has no internet makes mobile data the default network, and Android then routes an unbound
/// socket addressed to 192.168.137.1 out of the mobile interface.
///
/// Binding to the interface's source address does <em>not</em> work and is worth saying out loud,
/// because it is the obvious thing to try. Android routes on the socket's fwmark; a plain
/// <c>bind()</c> to 192.168.137.191 fails exactly as an unbound socket does, measured. Only
/// <see cref="Network.BindSocket(Java.IO.FileDescriptor)"/>, which sets the mark, changes
/// anything.
/// </summary>
public sealed class WifiStreamBinding : IStreamBinding
{
    public HttpMessageHandler? Handler() =>
        new SocketsHttpHandler
        {
            ConnectCallback = ConnectAsync,

            // The stream is one long-lived connection to one PC. Pooling several and aging them
            // out would only create more sockets that have to be bound, for no gain.
            PooledConnectionLifetime = Timeout.InfiniteTimeSpan,
            MaxConnectionsPerServer = 2
        };

    private static async ValueTask<NetworkStream> ConnectAsync(
        SocketsHttpConnectionContext context, CancellationToken ct)
    {
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };

        try
        {
            BindToWifi(socket);

            await socket.ConnectAsync(context.DnsEndPoint, ct);

            return new System.Net.Sockets.NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Marks the socket as belonging to the Wi-Fi network, before it connects.
    ///
    /// <c>Network.BindSocket</c> wants a Java socket or a Java file descriptor, and a .NET
    /// <see cref="Socket"/> is neither. <c>ParcelFileDescriptor.FromFd</c> is the way across: it
    /// dups the descriptor into a Java one, the dup shares the underlying file description, and
    /// the mark Android sets lives there rather than on the descriptor — so it applies to the
    /// socket .NET goes on to use. Confirmed on a device, not assumed.
    ///
    /// Throws rather than falling back to an unbound connect. A silent fallback here would be a
    /// connection that works on the phone it was tested on and times out on the one it matters
    /// for, which is the failure this whole class exists to remove.
    /// </summary>
    private static void BindToWifi(Socket socket)
    {
        if (WifiNetwork.Current() is not { } wifi)
            throw new InvalidOperationException("No connected Wi-Fi network to bind to.");

        // Closing the ParcelFileDescriptor closes only the dup, never the socket .NET holds.
        using var descriptor = ParcelFileDescriptor.FromFd((int)socket.Handle);

        if (descriptor?.FileDescriptor is not { } fd)
            throw new InvalidOperationException("ParcelFileDescriptor gave no FileDescriptor.");

        wifi.BindSocket(fd);
    }
}
