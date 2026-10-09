using System.Net;
using System.Text;

namespace GuildWarsCompanion.Services;

/// <summary>One answer from the bridge, or the reason there wasn't one.</summary>
public readonly record struct BridgeResponse(int Status, string Body, string? Error)
{
    public bool Ok => Status is >= 200 and < 300;

    public static BridgeResponse Failed(string error) => new(0, string.Empty, error);
}

/// <summary>
/// How a request reaches the bridge.
///
/// This exists because of a routing problem that has no fix inside the app's normal HTTP stack.
/// A phone whose Wi-Fi has no internet — a PC hotspot on a network that blocks NAT, say — will
/// make mobile data the default network, and Android then routes <em>every</em> unbound socket
/// there, including one addressed to 192.168.137.1. The LAN is one hop away over an associated
/// Wi-Fi interface and completely unreachable:
///
/// <code>192.168.137.1 via 192.0.0.1 dev rmnet_data1</code>
///
/// The only way out is to bind the request to a specific network rather than letting the routing
/// table choose, which is a platform capability and not something HttpClient exposes.
/// </summary>
public interface IBridgeTransport
{
    /// <param name="timeout">
    /// How long to wait, or null for the transport's own default.
    ///
    /// Overridable because one caller has completely different needs from the rest: sweeping a
    /// subnet means two hundred probes of which at most one will answer, so the default wait —
    /// sized to outlast a PC's clipboard — would turn a last resort into a minute of staring.
    /// </param>
    Task<BridgeResponse> SendAsync(
        string url, HttpMethod method, string? body, string? code, CancellationToken ct,
        TimeSpan? timeout = null);
}

/// <summary>
/// The ordinary one: whatever network the OS would have picked anyway.
///
/// Correct everywhere the bridge is on the default network, which is most places — a phone on
/// home Wi-Fi with working internet needs nothing cleverer.
/// </summary>
public sealed class DefaultBridgeTransport : IBridgeTransport
{
    /// <summary>
    /// Short on purpose. This runs behind a tap on a list row; a phone whose PC is asleep has to
    /// fail fast enough that the answer still feels like feedback.
    ///
    /// Eight rather than four, though, because "fast" has to still be longer than the bridge
    /// takes to answer. A paste is not answered until the PC has tried the clipboard, and four
    /// seconds cut that off mid-attempt: the phone called a slow clipboard a dead PC. Feedback
    /// that arrives promptly and says the wrong thing is worse than feedback that waits.
    /// </summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);

    private readonly HttpClient _http = new() { Timeout = Timeout };

    public async Task<BridgeResponse> SendAsync(
        string url, HttpMethod method, string? body, string? code, CancellationToken ct,
        TimeSpan? timeout = null)
    {
        try
        {
            using var request = new HttpRequestMessage(method, url);

            if (body is not null) request.Content = new StringContent(body, Encoding.UTF8, "text/plain");
            if (code is not null) request.Headers.TryAddWithoutValidation(Bridge.Protocol.BridgeProtocol.CodeHeader, code);

            // A shorter deadline than the client's own is applied by cancelling, since one
            // HttpClient is shared and its Timeout cannot vary per request.
            using var deadline = timeout is { } shorter
                ? CancellationTokenSource.CreateLinkedTokenSource(ct)
                : null;

            deadline?.CancelAfter(timeout!.Value);

            using var response = await _http.SendAsync(request, deadline?.Token ?? ct);

            return new BridgeResponse(
                (int)response.StatusCode, await response.Content.ReadAsStringAsync(ct), null);
        }
        catch (Exception ex)
        {
            return BridgeResponse.Failed(Innermost(ex).Message);
        }
    }

    /// <summary>HttpRequestException wraps whatever actually went wrong; that is the useful one.</summary>
    public static Exception Innermost(Exception ex)
    {
        while (ex.InnerException is { } inner) ex = inner;

        return ex;
    }
}
