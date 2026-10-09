using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GuildWarsCompanion.Bridge.Protocol;

/// <summary>Why a request was or was not put on the clipboard.</summary>
public enum PasteVerdict
{
    Accepted,
    NotPaired,
    WrongCode,
    Empty,
    TooLong,
    NotUnderstood,

    /// <summary>
    /// Well-formed, correctly coded, and the clipboard still would not take it.
    ///
    /// Reached after the check rather than by it, which is why it is last: every other verdict
    /// is decided from the request alone, and this one is only known once the clipboard has been
    /// tried. It exists because the alternative is answering 204 for a paste that went nowhere.
    /// </summary>
    ClipboardRefused,

    /// <summary>
    /// An emote the Blish HUD module typed into the game, rather than put on the clipboard.
    /// Decided after the check, like <see cref="ClipboardRefused"/>: only the handler knows
    /// whether the module was there and willing.
    /// </summary>
    Performed
}

/// <summary>
/// One moment a phone was in touch, whatever it asked for.
/// </summary>
/// <param name="Path">
/// Which request it was. Worth keeping because the three say very different things: a
/// <c>/hello</c> is a phone working out where this PC is, a <c>/game</c> is the roster asking
/// who is logged in, and a <c>/paste</c> is somebody tapping an emote.
/// </param>
public sealed record PhoneContact(DateTimeOffset At, string From, string Path);

/// <summary>The head of an HTTP request: everything before the body.</summary>
public sealed record RequestHead(
    string Method, string Path, IReadOnlyDictionary<string, string> Headers)
{
    public int ContentLength =>
        Headers.TryGetValue("content-length", out var value) &&
        int.TryParse(value, out var length) && length >= 0
            ? length
            : 0;

    public string? Header(string name) =>
        Headers.TryGetValue(name.ToLowerInvariant(), out var value) ? value : null;

    /// <summary>
    /// One value out of the query string, or null.
    ///
    /// Hand-parsed rather than via <c>HttpUtility</c>, which is not available to every target
    /// this file compiles into. The only query this protocol has is a hex nonce, so escaping
    /// never comes up — a value that needed decoding would be a value this bridge does not use.
    /// </summary>
    public string? Query(string name)
    {
        var mark = Path.IndexOf('?');
        if (mark < 0) return null;

        foreach (var pair in Path[(mark + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var split = pair.IndexOf('=');
            if (split <= 0) continue;

            if (pair[..split].Equals(name, StringComparison.OrdinalIgnoreCase))
                return pair[(split + 1)..];
        }

        return null;
    }
}

/// <summary>
/// Who a bridge is, as <see cref="BridgeProtocol.HelloPath"/> answers it.
/// </summary>
/// <param name="Id">
/// Stable for the life of the install and not a secret. This is the thing that survives a DHCP
/// lease, and the whole reason the phone can stop treating an address as configuration.
/// </param>
/// <param name="Name">This PC's hostname by default, and editable — it is what the phone shows
/// when it says where a paste went.</param>
/// <param name="Proof">
/// Present only when the caller supplied a nonce <em>and</em> this bridge's secret is strong
/// enough to prove anything with. Null is not a failure: it means "identified, not
/// authenticated", which is exactly what a hand-typed pairing can honestly offer.
/// </param>
public sealed record BridgeIdentity(string Id, string Name, int Port, string? Proof = null);

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(BridgeIdentity))]
internal partial class ProtocolJson : JsonSerializerContext;

/// <summary>
/// Everything the phone needs to pair, in one QR code — and deliberately no address.
///
/// An address in here would be the bug this whole mechanism exists to remove: it is true at the
/// moment the code is drawn and false after the next lease. The phone finds the bridge by
/// <see cref="Id"/> instead.
/// </summary>
public sealed record BridgeCard(string Id, string Name, string Secret, int Port)
{
    private const string Scheme = "gw2bridge:v1?";

    public string Encode() =>
        $"{Scheme}id={Id}&name={Uri.EscapeDataString(Name)}&s={Secret}&port={Port}";

    /// <summary>
    /// The reverse, or null for anything that is not one of these.
    ///
    /// Null rather than throwing because the scanner points at whatever the user framed: a
    /// wrong QR code is the normal case, not an error. Mirrors how the API key scanner leans on
    /// <c>Gw2ApiKey.LooksValid</c> to decide whether to keep looking.
    /// </summary>
    public static BridgeCard? TryParse(string? text)
    {
        var value = (text ?? string.Empty).Trim();

        if (!value.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase)) return null;

        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in value[Scheme.Length..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var split = pair.IndexOf('=');
            if (split > 0) fields[pair[..split]] = Uri.UnescapeDataString(pair[(split + 1)..]);
        }

        if (!fields.TryGetValue("id", out var id) || id.Length == 0) return null;
        if (!fields.TryGetValue("s", out var secret) || !BridgeProtocol.IsStrong(secret)) return null;

        var port = fields.TryGetValue("port", out var text2) && int.TryParse(text2, out var parsed)
            ? parsed
            : BridgeProtocol.DefaultPort;

        // A nameless card still pairs. The name is for the phone to show, and "PC" beats
        // refusing to pair over a cosmetic field.
        return new BridgeCard(id, fields.GetValueOrDefault("name", "PC"), secret, port);
    }
}

/// <summary>What the bridge decided about one request, and what to answer with.</summary>
/// <param name="IsEmote">Asked for on <see cref="BridgeProtocol.EmotePath"/>: the phone would
/// like this done in the game if it can be, and put on the clipboard if not.</param>
public sealed record PasteResult(PasteVerdict Verdict, string Text, bool IsEmote = false)
{
    public bool IsAccepted => Verdict == PasteVerdict.Accepted;

    /// <summary>
    /// The HTTP status. A wrong code is 403 rather than 401 on purpose: 401 invites a client
    /// to prompt for credentials and retry, and there is nothing here to prompt for — the
    /// pairing code is set up once, on the phone, deliberately.
    /// </summary>
    public int Status => Verdict switch
    {
        PasteVerdict.Accepted => 204,
        // 200 rather than 204, and that difference is the whole signal: the phone says "done in
        // game" for one and "copied to the PC" for the other.
        PasteVerdict.Performed => 200,
        PasteVerdict.NotPaired => 503,
        PasteVerdict.WrongCode => 403,
        PasteVerdict.TooLong => 413,
        PasteVerdict.NotUnderstood => 400,
        // The request was fine and this PC failed to carry it out, which is what 5xx is for.
        // Not 503, because that is already "no pairing code" and the phone has to tell them
        // apart to say anything useful.
        PasteVerdict.ClipboardRefused => 500,
        _ => 400
    };

    /// <summary>Said to the phone, and shown in the window. The same sentence either way.</summary>
    public string Reason => Verdict switch
    {
        PasteVerdict.Accepted => "Copied to this PC",
        PasteVerdict.Performed => "Done in game",
        PasteVerdict.NotPaired => "The bridge has no pairing code yet",
        PasteVerdict.WrongCode => "Wrong pairing code",
        PasteVerdict.Empty => "Nothing to copy",
        PasteVerdict.TooLong => $"Longer than {BridgeProtocol.MaxTextLength} characters",
        PasteVerdict.ClipboardRefused => "This PC's clipboard would not take it",
        _ => "Not a paste request"
    };
}

/// <summary>
/// The wire contract between the phone and this PC, worked out away from any socket.
///
/// It is deliberately tiny — one POST carrying one string — and deliberately ignorant. The
/// bridge does not know what a chat link is or that Guild Wars 2 exists; it receives text and
/// puts it on the clipboard. The one exception is <see cref="EmotePath"/>, and even there the
/// bridge only checks the text against the emote list before handing it on. That is what keeps it from ever needing an
/// update when the phone learns to send something new, and what keeps an API key off this
/// machine entirely.
/// </summary>
public static class BridgeProtocol
{
    /// <summary>Arbitrary, above the registered range, and unlikely to be in use.</summary>
    public const int DefaultPort = 47623;

    public const string CodeHeader = "x-bridge-code";

    public const string PastePath = "/paste";

    /// <summary>
    /// An emote, to be done in the game when this PC can and copied when it cannot.
    ///
    /// Its own path rather than a flag on /paste, so a bridge from before it answers 400 and the
    /// phone knows to fall back to /paste — the old bridge still gets the emote onto the clipboard.
    /// </summary>
    public const string EmotePath = "/emote";

    /// <summary>
    /// The trail to the objective, for the Blish HUD module to draw in the world. The body is
    /// <see cref="GuideWire"/>; a bridge from before it answers 400 and the phone simply carries on
    /// without — nothing here is anything the phone's own screens depend on.
    /// </summary>
    public const string GuidePath = "/guide";

    public const string HelloPath = "/hello";

    /// <summary>
    /// Asks a running bridge to put its window back on screen. Loopback only, and carries
    /// nothing — it exists so that launching the app twice raises the first copy instead of
    /// starting a second, which is the way back when a desktop has no tray to click.
    /// </summary>
    public const string ShowPath = "/show";

    /// <summary>
    /// What the game is doing, read from MumbleLink on this PC. Needs the pairing code: it says
    /// which character is logged in and where they are standing, which is nobody else's business.
    /// </summary>
    public const string GamePath = "/game";

    /// <summary>
    /// Generous for a chat link, mean enough that nothing on the network can use this to push
    /// a novel into the clipboard.
    /// </summary>
    public const int MaxTextLength = 4096;

    /// <summary>Head plus body; a paste that cannot fit in this never gets read.</summary>
    public const int MaxRequestBytes = 16 * 1024;

    /// <summary>
    /// Where a bridge listens for "is anyone there?", so the phone never has to be told an
    /// address.
    ///
    /// UDP, and one past the TCP port so the two read as a pair in a firewall dialog. Broadcast
    /// rather than mDNS on purpose: symmetric, no dependency on either side, and it works on a
    /// Windows mobile hotspot — where <c>adb mdns services</c>, for one, never has.
    /// </summary>
    public const int DiscoveryPort = 47624;

    /// <summary>
    /// What the phone shouts and a bridge listens for. Fixed text, because a probe that has to
    /// be parsed is a parser reachable from anything on the network.
    /// </summary>
    public const string DiscoveryProbe = "gw2bridge?";

    /// <summary>How long a bridge's answer may be, so a reply can never be a payload.</summary>
    public const int MaxDiscoveryBytes = 512;

    /// <summary>
    /// The SignalR hub carrying what the game is doing, frame by frame, for the compass screen.
    ///
    /// A second server on a second port rather than a path on the existing one: this is Kestrel
    /// and the rest is a hand-rolled <c>TcpListener</c>, and they cannot share a socket. See
    /// ROADMAP.md, "SignalR for the compass, HTTP for everything else".
    /// </summary>
    public const string HubPath = "/gamestream";

    /// <summary>
    /// Where that hub listens, given the port the bridge proper is on.
    ///
    /// Two past it, so the trio reads as a block in a firewall dialog: TCP for requests, UDP for
    /// discovery, TCP for the stream. Derived rather than configured because a second port in
    /// the settings is a second thing to get wrong, and the phone has to be able to work it out
    /// from what /hello already tells it.
    /// </summary>
    public static int HubPortFor(int port) => port + 2;

    /// <summary>
    /// Crockford's alphabet: no I, L, O or U, so nothing in a printed code can be misread as
    /// something else — this gets typed on a phone keyboard, once, from a screen across a room.
    /// </summary>
    private const string CodeAlphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    /// <summary>
    /// A fresh pairing code, grouped for reading aloud.
    ///
    /// Eight symbols of a 32-symbol alphabet is 40 bits. That is far more than a home network
    /// needs against guessing and still short enough to type, which is the actual constraint.
    /// </summary>
    public static string NewPairingCode()
    {
        var symbols = new char[9];

        for (var i = 0; i < 4; i++) symbols[i] = CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)];
        symbols[4] = '-';
        for (var i = 5; i < 9; i++) symbols[i] = CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)];

        return new string(symbols);
    }

    /// <summary>
    /// A fresh high-entropy secret, for a pairing that travels by QR code rather than by eye.
    ///
    /// Fifty-two Crockford symbols, so 260 bits. The length is the point: a bridge that answers
    /// a challenge hands out an <see cref="Proof"/> over a nonce, and anything on the network can
    /// keep that pair and attack it offline at its leisure. Eight symbols — <see
    /// cref="NewPairingCode"/>'s 40 bits — falls to that in hours. This does not fall to it at
    /// all, and nobody ever has to read it aloud.
    ///
    /// Crockford rather than base64url so that <see cref="Normalise"/> is safe to run over it:
    /// that strips anything not a letter or digit, which would quietly eat the '-' and '_' of a
    /// base64url secret and leave two sides comparing different things.
    /// </summary>
    public static string NewPairingSecret()
    {
        var symbols = new char[52];

        for (var i = 0; i < symbols.Length; i++)
            symbols[i] = CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)];

        return new string(symbols);
    }

    /// <summary>
    /// A stable name for one bridge install. Not a secret — it travels in the clear in every
    /// discovery reply, which is what makes it usable as the thing an address is looked up by.
    /// </summary>
    public static string NewBridgeId()
    {
        var symbols = new char[26];

        for (var i = 0; i < symbols.Length; i++)
            symbols[i] = CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)];

        return new string(symbols);
    }

    /// <summary>
    /// Whether a secret has the entropy to be proved over the network.
    ///
    /// The dividing line between the two kinds of pairing this protocol allows. Above it a
    /// bridge can answer a challenge and the phone can trust an address it was never told;
    /// below it — a code somebody typed — the phone identifies the bridge and trusts it on
    /// first use, which is no weaker than the address having been typed too.
    /// </summary>
    public static bool IsStrong(string? secret) => Normalise(secret).Length >= 32;

    /// <summary>
    /// A fresh nonce for the phone to challenge a bridge with.
    ///
    /// Hex, so it survives being a query parameter without escaping, and fresh every time: a
    /// reused nonce lets a bridge that once answered be replayed by anything that overheard it.
    /// </summary>
    public static string NewNonce() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

    /// <summary>
    /// The bridge's answer to a challenge: that it holds the same secret, without sending it.
    ///
    /// Only ever computed over a strong secret — see <see cref="IsStrong"/>. Returns null
    /// otherwise rather than a weak proof, because a weak one is worse than none: it looks like
    /// authentication and is a 40-bit offline puzzle.
    /// </summary>
    public static string? Proof(string? secret, string? nonce)
    {
        if (!IsStrong(secret) || string.IsNullOrWhiteSpace(nonce)) return null;

        var key = Encoding.UTF8.GetBytes(Normalise(secret));

        return Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(nonce.Trim())));
    }

    /// <summary>
    /// Whether a bridge's proof is the one expected, compared without leaking how far it got.
    /// A missing proof on either side is a no, never a pass.
    /// </summary>
    public static bool ProofsMatch(string? offered, string? expected)
    {
        if (string.IsNullOrWhiteSpace(offered) || string.IsNullOrWhiteSpace(expected)) return false;

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(offered.Trim().ToUpperInvariant()),
            Encoding.UTF8.GetBytes(expected.Trim().ToUpperInvariant()));
    }

    /// <summary>
    /// Compares two codes without leaking how far they matched.
    ///
    /// Timing-safe because it costs nothing to be: the alternative is an attacker on the same
    /// network recovering the code one symbol at a time. Case and grouping dashes are ignored,
    /// so a code read off the screen and typed in lower case still works.
    /// </summary>
    public static bool CodesMatch(string? offered, string? expected)
    {
        if (string.IsNullOrWhiteSpace(expected)) return false;

        var a = Encoding.UTF8.GetBytes(Normalise(offered));
        var b = Encoding.UTF8.GetBytes(Normalise(expected));

        return CryptographicOperations.FixedTimeEquals(a, b);
    }

    /// <summary>
    /// Whether an offered credential is either of the two a bridge will answer to.
    ///
    /// Two, because the same bridge can be paired both ways at once: an old phone that typed the
    /// short code and a new one that scanned the QR are both legitimately holding "the"
    /// credential, and neither should have to be re-paired because the other exists.
    /// </summary>
    public static bool Accepts(string? offered, string? code, string? secret) =>
        CodesMatch(offered, code) || CodesMatch(offered, secret);

    /// <summary>Upper case, no dashes, no spaces — how the code is compared, never displayed.</summary>
    public static string Normalise(string? code) =>
        new((code ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    /// <summary>
    /// Reads the request line and headers. Header names are lower-cased, because HTTP says they
    /// are case-insensitive and nothing downstream should have to remember that.
    /// </summary>
    public static RequestHead? ParseHead(string head)
    {
        var lines = head.Split('\n');
        if (lines.Length == 0) return null;

        var request = lines[0].TrimEnd('\r').Split(' ');
        if (request.Length < 2) return null;

        var headers = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var line in lines.Skip(1))
        {
            var trimmed = line.TrimEnd('\r');
            if (trimmed.Length == 0) break;

            var colon = trimmed.IndexOf(':');
            if (colon <= 0) continue;

            headers[trimmed[..colon].Trim().ToLowerInvariant()] = trimmed[(colon + 1)..].Trim();
        }

        return new RequestHead(request[0].ToUpperInvariant(), request[1], headers);
    }

    /// <summary>
    /// Whether this request should reach the clipboard.
    /// </summary>
    /// <param name="expected">
    /// The code this bridge is paired with, or null/empty if it has none — which is refused
    /// rather than waved through, so a bridge that has somehow lost its code is closed, not open.
    /// </param>
    public static PasteResult Check(
        RequestHead? head, string? body, string? expected, string? secret = null)
    {
        var emote = head is not null && PathIs(head.Path, EmotePath);

        if (head is null || head.Method != "POST" || !(emote || PathIs(head.Path, PastePath)))
            return new PasteResult(PasteVerdict.NotUnderstood, string.Empty);

        if (string.IsNullOrWhiteSpace(expected) && string.IsNullOrWhiteSpace(secret))
            return new PasteResult(PasteVerdict.NotPaired, string.Empty);

        if (!Accepts(head.Header(CodeHeader), expected, secret))
            return new PasteResult(PasteVerdict.WrongCode, string.Empty);

        var text = body ?? string.Empty;

        if (text.Length > MaxTextLength) return new PasteResult(PasteVerdict.TooLong, string.Empty);

        // Trimmed, because a trailing newline from the sender would otherwise become a stray
        // return in the chat box.
        text = text.Trim();

        return text.Length == 0
            ? new PasteResult(PasteVerdict.Empty, string.Empty, emote)
            : new PasteResult(PasteVerdict.Accepted, text, emote);
    }

    /// <summary>
    /// Whether a request to <see cref="GuidePath"/> may be acted on: the status to answer with, or
    /// null to go ahead. The same pairing rule as a paste — the line is drawn in the player's game,
    /// which is nobody else's to draw in.
    /// </summary>
    public static int? RefuseGuide(RequestHead head, string? expected, string? secret)
    {
        if (head.Method != "POST") return 400;

        if (string.IsNullOrWhiteSpace(expected) && string.IsNullOrWhiteSpace(secret)) return 503;

        return Accepts(head.Header(CodeHeader), expected, secret) ? null : 403;
    }

    /// <summary>Path comparison that ignores a query string and a trailing slash.</summary>
    public static bool PathIs(string? path, string expected)
    {
        var value = (path ?? string.Empty).Split('?')[0].TrimEnd('/');

        return value.Equals(expected.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
    }
}
