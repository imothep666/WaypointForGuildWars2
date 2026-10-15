using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace GuildWarsCompanion.Bridge.Protocol;

/// <summary>One thing that arrived, as the window lists it.</summary>
public sealed record Arrival(DateTimeOffset At, string From, string Text, PasteVerdict Verdict)
{
    public string TimeText => At.ToLocalTime().ToString("HH:mm:ss");

    /// <summary>Shown in full up to a point. The window is the audit trail — if something on
    /// the network is pushing at this clipboard, it has to be visible.</summary>
    public string Preview => Text.Length <= 90 ? Text : Text[..87] + "…";

    /// <summary>Drives the row's spine: brass for something that reached the clipboard or the
    /// game, ember for everything that was turned away.</summary>
    public bool IsAccepted => Verdict is PasteVerdict.Accepted or PasteVerdict.Performed;
}

/// <summary>
/// The listener.
///
/// A raw <see cref="TcpListener"/> rather than <c>HttpListener</c>, and that is not
/// stubbornness: on Windows HttpListener sits on http.sys, and binding anything other than
/// localhost needs a `netsh http add urlacl` registration or an elevated process. Asking
/// someone to run a command as administrator before their clipboard works is a worse first
/// five minutes than parsing one request line by hand. A socket behaves identically on both
/// platforms, and the surface here is one POST.
///
/// That argument is about <c>HttpListener</c> specifically and does <em>not</em> extend to
/// ASP.NET Core, which was never weighed when this was written. Kestrel is a managed socket
/// server, not an http.sys front end; it binds 0.0.0.0 from an ordinary account with no urlacl,
/// measured on 2026-09-14. What ASP.NET Core does cost here is deployment — a
/// framework-dependent publish would need the ASP.NET Core runtime installed rather than just
/// the desktop one, on Linux as well as Windows. These four endpoints are not worth that. The
/// compass stream is, which is why <c>Stream/GameHub</c> runs Kestrel beside this listener
/// instead of replacing it. That hub lives outside this folder on purpose: everything in
/// <c>Protocol/</c> is compiled into the phone app and the test project too, and neither can
/// take an ASP.NET Core dependency.
/// </summary>
/// <param name="config">
/// Read fresh on every request rather than captured, because the pairing code can change while
/// the bridge runs — that is the whole point of the button that changes it.
/// </param>
public sealed class BridgeServer(
    Func<BridgeConfig> config,
    Func<PasteResult, IPAddress?, Task<PasteResult>> onRequest,
    Action? onShow = null,
    Func<string>? gameJson = null,
    Action<PhoneContact>? onContact = null,
    Func<string?, Task<int>>? onGuide = null,
    Func<string?, Task<int>>? onNotify = null)
{
    private TcpListener? _listener;
    private CancellationTokenSource? _stopping;

    public int Port { get; private set; }

    public bool IsListening => _listener is not null;

    /// <summary>Starts listening on every interface. Throws if the port is taken.</summary>
    public void Start(int port)
    {
        Stop();

        _stopping = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start();

        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;

        _ = AcceptAsync(_listener, _stopping.Token);
    }

    public void Stop()
    {
        _stopping?.Cancel();
        _stopping?.Dispose();
        _stopping = null;

        _listener?.Stop();
        _listener = null;
    }

    private async Task AcceptAsync(TcpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(ct);
            }
            catch (Exception)
            {
                // Cancelled, or the listener was stopped underneath us.
                return;
            }

            // Deliberately not awaited: one slow or hostile connection must not stop the next
            // one being accepted.
            _ = HandleAsync(client, ct);
        }
    }

    private async Task HandleAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        {
            var from = (client.Client.RemoteEndPoint as IPEndPoint)?.Address;

            try
            {
                // A connection that opens and says nothing is a connection that would otherwise
                // sit here for ever.
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));

                await using var stream = client.GetStream();

                var (head, body) = await ReadAsync(stream, timeout.Token);

                // Reported before anything is decided about the request, because the useful
                // question the window answers with this is "is the phone reaching me at all" —
                // and a request that turns out to be badly formed or wrongly coded still proves
                // that it is. Loopback is skipped: that is this app talking to itself when a
                // second copy is launched, not a phone.
                if (head is not null && from is not null && !IPAddress.IsLoopback(from))
                    onContact?.Invoke(new PhoneContact(DateTimeOffset.Now, from.ToString(), head.Path));

                // Loopback only. This raises a window rather than moving data, so it needs no
                // pairing code — but it must not be reachable from the network, or anything on
                // the LAN could pop a window onto somebody's screen at will.
                if (head is not null && BridgeProtocol.PathIs(head.Path, BridgeProtocol.ShowPath))
                {
                    var local = from is not null && IPAddress.IsLoopback(from);

                    if (local) onShow?.Invoke();

                    await WriteAsync(stream, local ? 204 : 403, local ? string.Empty : "Loopback only", timeout.Token);
                    return;
                }

                if (head is not null && BridgeProtocol.PathIs(head.Path, BridgeProtocol.GamePath))
                {
                    // Code-protected, unlike /hello: this names the character who is logged in
                    // and where they are standing.
                    if (!BridgeProtocol.Accepts(
                            head.Header(BridgeProtocol.CodeHeader),
                            config().PairingCode,
                            config().Secret))
                    {
                        await WriteAsync(stream, 403, "Wrong pairing code", timeout.Token);
                        return;
                    }

                    await WriteJsonAsync(stream, gameJson?.Invoke() ?? "{}", timeout.Token);
                    return;
                }

                if (head is not null && head.Method == "GET" && BridgeProtocol.PathIs(head.Path, BridgeProtocol.HelloPath))
                {
                    // Unauthenticated on purpose. What it gives away is an id and a hostname,
                    // which is what the phone needs to recognise a PC that has moved address —
                    // and the id is not a secret, so nothing is spent by answering.
                    //
                    // A proof comes back only if the caller asked with a nonce, and only from a
                    // strong secret. That asymmetry is deliberate: see BridgeProtocol.Proof.
                    var asked = config().Identity(head.Query("nonce"));

                    await WriteJsonAsync(
                        stream, JsonSerializer.Serialize(asked, ProtocolJson.Default.BridgeIdentity),
                        timeout.Token);
                    return;
                }

                var current = config();

                // The trail for the Blish HUD module. Checked like a paste, but not one: its body
                // is a path, too long for the paste limit and meaningless on a clipboard.
                if (head is not null && onGuide is not null &&
                    BridgeProtocol.PathIs(head.Path, BridgeProtocol.GuidePath))
                {
                    var status = BridgeProtocol.RefuseGuide(head, current.PairingCode, current.Secret)
                                 ?? await onGuide(body);

                    await WriteAsync(stream, status, status == 204 ? string.Empty : "Not a trail", timeout.Token);
                    return;
                }

                // An alert for the Blish HUD module to show in the game. The same gate as the trail:
                // it is put on the player's screen, which is nobody else's to write on.
                if (head is not null && onNotify is not null &&
                    BridgeProtocol.PathIs(head.Path, BridgeProtocol.NotifyPath))
                {
                    var status = BridgeProtocol.RefuseGuide(head, current.PairingCode, current.Secret)
                                 ?? await onNotify(body);

                    await WriteAsync(stream, status, status == 204 ? string.Empty : "Not shown in the game", timeout.Token);
                    return;
                }

                var result = BridgeProtocol.Check(head, body, current.PairingCode, current.Secret);

                // The handler's answer, not the check's. The check knows whether the request
                // was worth acting on; only the handler knows whether acting on it worked, and
                // a paste that never reached the clipboard must not go back as 204.
                var final = await onRequest(result, from);

                await WriteAsync(stream, final.Status, final.Reason, timeout.Token);
            }
            catch (Exception)
            {
                // A malformed or abandoned connection is not an error worth surfacing.
            }
        }
    }

    /// <summary>
    /// Reads the head, then exactly as much body as the head declared.
    ///
    /// Capped twice over: the head cannot grow past the limit while being scanned for its
    /// terminator, and the body is only read up to what Content-Length claimed and what the cap
    /// allows. Without the first cap a sender that never sends a blank line reads for ever.
    /// </summary>
    private static async Task<(RequestHead? Head, string? Body)> ReadAsync(
        NetworkStream stream, CancellationToken ct)
    {
        var buffer = new byte[8192];
        var received = new MemoryStream();
        var headEnd = -1;

        while (headEnd < 0)
        {
            var read = await stream.ReadAsync(buffer, ct);
            if (read == 0) break;

            received.Write(buffer, 0, read);

            if (received.Length > BridgeProtocol.MaxRequestBytes) return (null, null);

            headEnd = Find(received.GetBuffer(), (int)received.Length);
        }

        if (headEnd < 0) return (null, null);

        var all = received.GetBuffer();
        var head = BridgeProtocol.ParseHead(Encoding.UTF8.GetString(all, 0, headEnd));
        if (head is null) return (null, null);

        var bodyStart = headEnd + 4;
        var have = (int)received.Length - bodyStart;
        var want = Math.Min(head.ContentLength, BridgeProtocol.MaxRequestBytes);

        var body = new MemoryStream();
        if (have > 0) body.Write(all, bodyStart, Math.Min(have, want));

        while (body.Length < want)
        {
            var read = await stream.ReadAsync(buffer, ct);
            if (read == 0) break;

            body.Write(buffer, 0, (int)Math.Min(read, want - body.Length));
        }

        return (head, Encoding.UTF8.GetString(body.GetBuffer(), 0, (int)body.Length));
    }

    /// <summary>The blank line that ends the head.</summary>
    private static int Find(byte[] data, int length)
    {
        for (var i = 0; i + 3 < length; i++)
            if (data[i] == '\r' && data[i + 1] == '\n' && data[i + 2] == '\r' && data[i + 3] == '\n')
                return i;

        return -1;
    }

    /// <summary>200 with a JSON body, for the one endpoint that answers with data.</summary>
    private static async Task WriteJsonAsync(NetworkStream stream, string json, CancellationToken ct)
    {
        var payload = Encoding.UTF8.GetBytes(json);

        var head = new StringBuilder()
            .Append("HTTP/1.1 200 OK\r\n")
            .Append("Content-Type: application/json; charset=utf-8\r\n")
            .Append("Content-Length: ").Append(payload.Length).Append("\r\n")
            .Append("Connection: close\r\n\r\n")
            .ToString();

        await stream.WriteAsync(Encoding.ASCII.GetBytes(head), ct);
        await stream.WriteAsync(payload, ct);
        await stream.FlushAsync(ct);
    }

    private static async Task WriteAsync(NetworkStream stream, int status, string body, CancellationToken ct)
    {
        var payload = Encoding.UTF8.GetBytes(body);

        var head = new StringBuilder()
            .Append("HTTP/1.1 ").Append(status).Append(' ').Append(Describe(status)).Append("\r\n")
            .Append("Content-Type: text/plain; charset=utf-8\r\n")
            .Append("Content-Length: ").Append(status == 204 ? 0 : payload.Length).Append("\r\n")
            .Append("Connection: close\r\n\r\n")
            .ToString();

        await stream.WriteAsync(Encoding.ASCII.GetBytes(head), ct);

        // 204 promises no body, and a client is entitled to believe it.
        if (status != 204) await stream.WriteAsync(payload, ct);

        await stream.FlushAsync(ct);
    }

    private static string Describe(int status) => status switch
    {
        200 => "OK",
        204 => "No Content",
        400 => "Bad Request",
        403 => "Forbidden",
        413 => "Payload Too Large",
        503 => "Service Unavailable",
        _ => "Error"
    };
}
