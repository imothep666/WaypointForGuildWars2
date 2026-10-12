using Android.Content;
using Android.Net;

using GuildWarsCompanion.Services;

using Java.Net;

using Stream = System.IO.Stream;

namespace GuildWarsCompanion.Platforms.Android;

/// <summary>
/// Sends the bridge's requests over Wi-Fi specifically, whatever the default network is.
///
/// <see cref="Network.OpenConnection(URL)"/> is Android's own answer to this and the reason this
/// class is not just an HttpClient with a socket option: it hands back a connection already bound
/// to the network you chose, so the routing table never gets a say. Nothing in
/// <c>System.Net.Http</c> can express that, which is why the bridge talks through
/// <see cref="Java.Net.HttpURLConnection"/> here and HttpClient everywhere else.
///
/// The payoff is the configuration the phone is otherwise bad at: Wi-Fi associated for the LAN,
/// mobile data carrying the internet. Both at once, deliberately.
/// </summary>
public sealed class WifiBridgeTransport(IBridgeTransport fallback) : IBridgeTransport
{
    /// <summary>
    /// Long enough to outlast the bridge's own clipboard budget, which is the number that
    /// matters rather than anything about the network.
    ///
    /// A paste is not answered until the PC has tried to put it on the clipboard, so this
    /// timeout is really a bet on how long that takes. At four seconds the bet was lost every
    /// time the clipboard was busy — measured at 6.9s against a refusing clipboard, where
    /// /hello came back in 4ms — and the phone reported "PC did not answer" for a PC that had
    /// answered perfectly well about a clipboard that had not. The bridge now bounds its own
    /// attempt; this leaves room for it to say so.
    /// </summary>
    private const int TimeoutMs = 8000;

    public Task<BridgeResponse> SendAsync(
        string url, HttpMethod method, string? body, string? code, CancellationToken ct,
        TimeSpan? timeout = null)
    {
        var wifi = WifiNetwork.Current();

        var wait = (int)(timeout?.TotalMilliseconds ?? TimeoutMs);

        // No Wi-Fi at all, or the platform would not say which. Fall back rather than fail:
        // where Wi-Fi is already the default network this changes nothing anyway.
        return wifi is null
            ? fallback.SendAsync(url, method, body, code, ct, timeout)
            : Task.Run(() => Send(wifi, url, method, body, code, wait), ct);
    }

    /// <summary>
    /// Blocking by nature — HttpURLConnection has no async form — so it runs on a pool thread.
    /// </summary>
    private static BridgeResponse Send(
        Network wifi, string url, HttpMethod method, string? body, string? code, int wait)
    {
        HttpURLConnection? connection = null;

        try
        {
            connection = (HttpURLConnection?)wifi.OpenConnection(new URL(url));
            if (connection is null) return BridgeResponse.Failed(L.T("Could not open a Wi-Fi connection."));

            connection.RequestMethod = method.Method;
            connection.ConnectTimeout = wait;
            connection.ReadTimeout = wait;
            connection.InstanceFollowRedirects = false;

            if (code is not null)
                connection.SetRequestProperty(Bridge.Protocol.BridgeProtocol.CodeHeader, code);

            if (body is not null)
            {
                connection.DoOutput = true;
                connection.SetRequestProperty("Content-Type", "text/plain; charset=utf-8");

                var payload = System.Text.Encoding.UTF8.GetBytes(body);
                connection.SetFixedLengthStreamingMode(payload.Length);

                using var output = connection.OutputStream;
                output?.Write(payload, 0, payload.Length);
            }

            var status = (int)connection.ResponseCode;

            // A refusal comes back on the error stream, not the input one, and the bridge's
            // reasons are worth reading — "Wrong pairing code" is the whole diagnosis.
            using var stream = status >= 400 ? connection.ErrorStream : connection.InputStream;

            return new BridgeResponse(status, ReadAll(stream), null);
        }
        catch (Exception ex)
        {
            return BridgeResponse.Failed(DefaultBridgeTransport.Innermost(ex).Message);
        }
        finally
        {
            connection?.Disconnect();
        }
    }

    private static string ReadAll(Stream? stream)
    {
        if (stream is null) return string.Empty;

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
