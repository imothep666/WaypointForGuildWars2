using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

using GuildWarsCompanion.Bridge.Protocol;

namespace GuildWarsCompanion.Services;

/// <summary>The bridge's own JSON, kept apart from the Guild Wars 2 API's context.</summary>
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(GameReport))]
internal partial class BridgeJson : JsonSerializerContext;

/// <summary>How a send to the PC turned out, in the order of how much it is the user's fault.</summary>
public enum BridgeOutcome
{
    Sent,
    NotSetUp,
    NoAnswer,
    WrongCode,
    Refused,

    /// <summary>
    /// The bridge took the request and this PC's clipboard would not have it. Distinct from
    /// <see cref="Refused"/> because nothing about the phone or the pairing is wrong — the
    /// answer is "try again" or "see what is holding the clipboard over there", and neither is
    /// reachable from "PC refused it".
    /// </summary>
    ClipboardRefused
}

/// <summary>
/// Talks to the bridge running on the player's PC.
///
/// Guild Wars 2 has no Android client, so a chat link on this phone's clipboard is a chat link
/// nowhere near the game. The bridge is a small desktop app that listens on the local network,
/// puts what it receives on the PC's clipboard, and — since it sits on the same machine as the
/// game — can answer what the game is doing.
///
/// The contract is <see cref="BridgeProtocol"/>, compiled into both this app and the bridge from
/// one file. Which network the request goes out of is <see cref="IBridgeTransport"/>'s problem,
/// and a real one: see the note there.
/// </summary>
/// <summary>How a send turned out, and which PC it turned out that way on.</summary>
/// <param name="Pc">
/// The PC's name, for a sentence a person can act on. With one bridge "sent to PC" was specific
/// enough; with three on a desk it is not, and a paste that went to the wrong machine has to be
/// visibly the wrong machine.
/// </param>
/// <param name="InGame">
/// The PC did it rather than copied it: an emote the Blish HUD module typed into the game. Only
/// ever true for <see cref="BridgeClient.EmoteAsync"/>, and only when the bridge answered 200.
/// </param>
public sealed record BridgeSend(BridgeOutcome Outcome, string? Pc = null, bool InGame = false)
{
    public bool IsSent => Outcome == BridgeOutcome.Sent;

    /// <summary>"done in game on Desktop", "sent to Desktop", or the plain reason when there is
    /// no PC to name.</summary>
    public string Text =>
        Outcome == BridgeOutcome.Sent && Pc is not null
            ? InGame ? L.F("done in game on {0}", Pc) : L.F("sent to {0}", Pc)
            : BridgeClient.Describe(Outcome);
}

public sealed class BridgeClient(
    IBridgeTransport transport, BridgeFinder finder, Journal journal)
{
    /// <summary>
    /// Whether any PC is paired.
    ///
    /// Not whether one can be reached — that is <see cref="BridgeFinder.ResolveAsync"/>'s
    /// business and it needs the network. This answers the far cheaper question a tap needs
    /// before it decides whether to look at all.
    ///
    /// Async because the first call after an upgrade has to move the old single pairing into the
    /// list, and the secret goes to the keystore, which cannot be written synchronously. Getting
    /// this wrong is not subtle: a phone that had a PC paired reads as having none, so an emote
    /// tap quietly stops leaving the phone and the settings page says "no PC paired yet".
    /// </summary>
    public Task<bool> IsSetUpAsync() => finder.IsSetUpAsync();

    /// <summary>The PCs this phone knows, newest contact first.</summary>
    public IReadOnlyList<KnownBridge> Known => finder.Known.All();

    /// <summary>A known PC seen somewhere new, waiting on one confirmation. Usually null.</summary>
    public BridgeMove? Move => finder.Move;

    public void Allow(BridgeMove move) => finder.Allow(move);

    public void Forget(string id)
    {
        journal.Write("pair", $"forgot {id}");

        finder.Known.Forget(id);
        finder.Reset();
    }

    public async Task<KnownBridge> PairAsync(BridgeCard card)
    {
        journal.Write("pair", $"scanned {card.Name}, id {card.Id} — strong, so its address " +
                              "can be found rather than told");

        var paired = await finder.Known.PairAsync(card);

        finder.Reset();

        return paired;
    }

    public async Task<KnownBridge> PairAsync(string address, string code)
    {
        journal.Write("pair", $"typed {address} — weak, so it is trusted on first use and " +
                              "asked about if it moves");

        var paired = await finder.Known.PairAsync(address, code);

        finder.Reset();

        return paired;
    }

    /// <summary>
    /// Why the last attempt failed, when the platform had something to say about it.
    ///
    /// Kept because "the PC did not answer" is a guess, and a wrong one for the whole class of
    /// failures where the request never left the phone. Android blocks cleartext HTTP by default,
    /// and routes LAN addresses out of the mobile interface when Wi-Fi is not the default network
    /// — neither of which looks anything like a firewall, which is where the guess sends people.
    /// </summary>
    public string? LastError { get; private set; }

    /// <summary>
    /// The most useful thing known about the last failure: what the transport said if a request
    /// was actually made, and otherwise why no PC could be found to make one to.
    /// </summary>
    public string? Trouble => LastError ?? finder.Problem;

    /// <summary>
    /// Finds a PC, and forgets what the last request said while doing it.
    ///
    /// The clearing is the point. <see cref="LastError"/> outlives the request that set it and
    /// <see cref="Trouble"/> prefers it over the search's own account, so a socket error from
    /// half an hour ago used to be reported in place of the reason this attempt failed — which
    /// is now the sentence that actually names the fix.
    /// </summary>
    private Task<ResolvedBridge?> FindAsync(CancellationToken ct, bool sweep = true)
    {
        LastError = null;

        return finder.ResolveAsync(ct, sweep);
    }

    /// <summary>
    /// Whether any paired PC can be found and proves itself, without pasting anything.
    ///
    /// No longer takes an address, because there is no longer an address to take. A failure here
    /// means "none of your PCs answered", which is a different sentence from the old "nothing is
    /// listening at the address you typed".
    /// </summary>
    public async Task<BridgeSend> TestAsync(CancellationToken ct = default)
    {
        finder.Reset();

        if (!await finder.IsSetUpAsync()) return new BridgeSend(BridgeOutcome.NotSetUp);

        return await FindAsync(ct) is { } found
            ? new BridgeSend(BridgeOutcome.Sent, found.Bridge.Label)
            : new BridgeSend(BridgeOutcome.NoAnswer);
    }

    public async Task<BridgeSend> SendAsync(string text, CancellationToken ct = default)
    {
        if (!await finder.IsSetUpAsync()) return new BridgeSend(BridgeOutcome.NotSetUp);

        if (await FindAsync(ct) is not { } found)
            return new BridgeSend(BridgeOutcome.NoAnswer);

        var answer = await Ask(found, BridgeProtocol.PastePath, HttpMethod.Post, text, ct);

        var outcome = Judge(answer);

        // The length, never the text: what travels over this is whatever the user copied.
        journal.Write("send", $"{text.Length} chars to {found.Bridge.Label} " +
                              $"at {found.Address} — {outcome}" +
                              (answer.Error is { } why ? $" ({why})" : string.Empty));

        return new BridgeSend(outcome, found.Bridge.Label);
    }

    /// <summary>
    /// An emote, done in the game if the PC can and copied to its clipboard if not.
    ///
    /// The bridge decides which, and says so: 200 for done in game, 204 for copied — the same
    /// 204 a paste gets. A bridge from before /emote answers 400 because it does not know the
    /// path, and is simply asked again the old way, so an old bridge still gets the emote onto
    /// its clipboard.
    /// </summary>
    public async Task<BridgeSend> EmoteAsync(string command, CancellationToken ct = default)
    {
        if (!await finder.IsSetUpAsync()) return new BridgeSend(BridgeOutcome.NotSetUp);

        if (await FindAsync(ct) is not { } found)
            return new BridgeSend(BridgeOutcome.NoAnswer);

        var answer = await Ask(found, BridgeProtocol.EmotePath, HttpMethod.Post, command, ct);

        if (answer.Status is (int)HttpStatusCode.BadRequest or (int)HttpStatusCode.NotFound)
            return await SendAsync(command, ct);

        var outcome = Judge(answer);
        var inGame = answer.Status == (int)HttpStatusCode.OK;

        // The command itself is fine to log, unlike a paste: it comes from a fixed list.
        journal.Write("emote", $"{command} to {found.Bridge.Label} at {found.Address} — " +
                               (inGame ? "done in game" : outcome.ToString()) +
                               (answer.Error is { } why ? $" ({why})" : string.Empty));

        return new BridgeSend(outcome, found.Bridge.Label, inGame && outcome == BridgeOutcome.Sent);
    }

    /// <summary>
    /// Hands the PC the trail to draw in the game, or takes it away with null. Quiet about failure:
    /// the phone's own screens do not depend on it, a bridge from before it answers 400, and a PC
    /// that is asleep is not the phone's problem. The journal says which.
    /// </summary>
    public async Task GuideAsync(GuidedTrail? trail, CancellationToken ct = default)
    {
        if (!await finder.IsSetUpAsync()) return;

        if (await FindAsync(ct) is not { } found) return;

        var answer = await Ask(found, BridgeProtocol.GuidePath, HttpMethod.Post, GuideWire.Write(trail), ct);

        // A bridge from before the objective's name refuses the whole trail for it; the line in
        // the world matters more than the name on the card.
        if (answer.Status == 400 && trail?.Name is not null)
        {
            trail = trail.Named(null);
            answer = await Ask(found, BridgeProtocol.GuidePath, HttpMethod.Post, GuideWire.Write(trail), ct);
        }

        journal.Write("guide", (trail is null ? "cleared" : $"{trail.Points.Count} points on map {trail.MapId}") +
                               $" to {found.Bridge.Label} — {answer.Status}" +
                               (answer.Error is { } why ? $" ({why})" : string.Empty));
    }

    /// <summary>
    /// What the game on the PC is doing, or null when that cannot be established.
    ///
    /// Null is ignorance, and every caller has to treat it as such: no bridge, a phone that
    /// cannot reach it, a PC that is asleep. None of those mean nobody is playing.
    /// </summary>
    /// <param name="sweep">See <see cref="BridgeFinder.ResolveAsync"/>: false for a caller that
    /// asks on a schedule rather than behind a tap.</param>
    public async Task<GameReport?> GameAsync(CancellationToken ct = default, bool sweep = true)
    {
        if (!await finder.IsSetUpAsync()) return null;

        if (await FindAsync(ct, sweep) is not { } found) return null;

        var answer = await Ask(found, BridgeProtocol.GamePath, HttpMethod.Get, null, ct);

        if (!answer.Ok) return null;

        try
        {
            return JsonSerializer.Deserialize(answer.Body, BridgeJson.Default.GameReport);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// What the game on the PC says about who is playing: a character, nobody, or nothing at all.
    ///
    /// The three answers are kept apart on purpose. A report that is not live still names the
    /// character who logged out last night — MumbleLink keeps its last values, and since the
    /// bridge now creates the mapping itself it keeps them for as long as the bridge runs — so
    /// the name is dropped. What is <em>not</em> dropped is the fact that the bridge answered:
    /// that is <see cref="LiveReading.NobodyPlaying"/>, and it is what stops the roster's
    /// playtime guess from putting "playing now" beside a game that has closed. Only a bridge
    /// that could not be reached at all returns <see cref="LiveReading.Unknown"/>.
    /// </summary>
    public async Task<LiveReading> PlayingNowAsync(CancellationToken ct = default, bool sweep = true) =>
        await GameAsync(ct, sweep) switch
        {
            null => LiveReading.Unknown,
            { IsLive: true, State.CharacterName.Length: > 0 } report =>
                LiveReading.Playing(report.State.CharacterName),
            _ => LiveReading.NobodyPlaying
        };

    /// <summary>
    /// Asks the PC to show an alert in the game, through the Blish HUD module. True only when the
    /// module took it; false for anything else — no bridge, no module, alerts switched off in it,
    /// a bridge from before alerts — so the caller shows it on the phone instead.
    /// </summary>
    /// <param name="sweep">False from the background poll; see <see cref="BridgeFinder.ResolveAsync"/>.</param>
    public async Task<bool> NotifyAsync(string title, string detail, CancellationToken ct = default, bool sweep = false)
    {
        if (!await finder.IsSetUpAsync()) return false;

        if (await FindAsync(ct, sweep) is not { } found) return false;

        var answer = await Ask(found, BridgeProtocol.NotifyPath, HttpMethod.Post, BridgeProtocol.NotifyBody(title, detail), ct);
        var shown = answer.Status == (int)HttpStatusCode.NoContent;

        journal.Write("alert", $"{title} to {found.Bridge.Label} — " +
                               (shown ? "shown in the game" : $"not shown there ({answer.Status})"));

        return shown;
    }

    private async Task<BridgeResponse> Ask(
        ResolvedBridge found, string path, HttpMethod method, string? body, CancellationToken ct)
    {
        var answer = await transport.SendAsync(
            BridgeFinder.Url(found.Address, path), method, body, found.Secret, ct);

        LastError = answer.Error;

        return answer;
    }

    private static BridgeOutcome Judge(BridgeResponse answer) => answer.Status switch
    {
        (int)HttpStatusCode.NoContent or (int)HttpStatusCode.OK => BridgeOutcome.Sent,
        (int)HttpStatusCode.Forbidden => BridgeOutcome.WrongCode,
        (int)HttpStatusCode.InternalServerError => BridgeOutcome.ClipboardRefused,
        0 => BridgeOutcome.NoAnswer,
        _ => BridgeOutcome.Refused
    };

    /// <summary>One sentence, in the vocabulary of the person who has to fix it.</summary>
    public static string Describe(BridgeOutcome outcome) => outcome switch
    {
        BridgeOutcome.Sent => L.T("sent to PC"),
        BridgeOutcome.NotSetUp => L.T("no PC set up"),
        BridgeOutcome.NoAnswer => L.T("no PC answered"),
        BridgeOutcome.WrongCode => L.T("PC rejected the code"),
        BridgeOutcome.ClipboardRefused => L.T("PC's clipboard would not take it"),
        _ => L.T("PC refused it")
    };
}
