using System.Diagnostics;
using System.Text.Json;

using GuildWarsCompanion.Bridge.Protocol;

namespace GuildWarsCompanion.Services;

/// <summary>A bridge, where it is right now, and the credential to talk to it with.</summary>
public sealed record ResolvedBridge(KnownBridge Bridge, string Address, string Secret);

/// <summary>
/// A known PC seen at an address the phone has not agreed to trust yet.
///
/// Only ever raised for a pairing that was typed rather than scanned. A typed code cannot prove
/// anything — see <see cref="BridgeProtocol.IsStrong"/> — so an id heard on the network is not
/// enough to start sending to a new address on: the id travels in the clear in every discovery
/// reply, and anything on the LAN could repeat it. The user gets asked once instead.
///
/// Raised only when nothing else worked. A confirmation is a question put to somebody who was
/// trying to do something else, and asking it while another paired PC is sitting there answering
/// is asking for no reason.
/// </summary>
public sealed record BridgeMove(KnownBridge Bridge, string? From, string To);

/// <summary>
/// Works out which PC to talk to, and where it is.
///
/// The ordering is the whole design, and it exists because an address is not configuration.
/// A cached address is tried first because it is nearly always still right and costs one
/// round trip against a <c>/hello</c> that answers in about 4ms. Broadcast discovery follows,
/// because that is what survives the lease renewal that used to look like a broken pairing.
/// Nothing here ever asks the user for an address.
///
/// <b>Every answer is a candidate, not a verdict.</b> Each step gathers everything that
/// answered, ranks it, and works down the list until one PC proves out. It used to pick a
/// single winner per step and give up when that one failed, which is fine with one paired PC
/// and wrong with two: a stale entry that answered — or a PC whose bridge had been reinstalled
/// and whose secret no longer matched — took the whole step down with it, and the PC answering
/// correctly two feet away was never tried. That is the bug this class was restructured around,
/// and it is why <see cref="BridgePlan.Rank"/> returns a list.
///
/// <b>Discovery traffic is on a short clock.</b> A <c>/hello</c> answers in milliseconds, so
/// none of the probing here is allowed the transport's default wait — that budget exists for a
/// paste, which is not answered until the PC has wrestled with its clipboard. One powered-off
/// PC in the list used to cost eight seconds before the broadcast was even sent.
/// </summary>
public sealed class BridgeFinder(
    IBridgeTransport transport, IBridgeDiscovery discovery, KnownBridges known, Journal journal)
{
    /// <summary>
    /// How long a resolution is reused before the network is asked again.
    ///
    /// Short, because the thing it caches is exactly the thing that moves. Long enough that
    /// tapping five emotes in a row does not broadcast five times.
    /// </summary>
    private static readonly TimeSpan Fresh = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Patience for anything that is only establishing who is out there.
    ///
    /// Sized against what a bridge actually takes to answer <c>/hello</c> — about 4ms on the
    /// wire — rather than against a paste. The transport's default is eight seconds because a
    /// clipboard can be slow; spending that on a PC that is switched off, once per stale entry,
    /// meant a phone with two paired PCs and one of them asleep waited eight seconds before
    /// discovery began and fourteen before it gave up. A tap cannot be that.
    /// </summary>
    private static readonly TimeSpan AskWait = TimeSpan.FromSeconds(2);

    /// <summary>
    /// How long the other probes get once one of them has answered.
    ///
    /// Generous against what it is waiting for — a second PC on the same LAN answers in single
    /// digit milliseconds — and small enough that a switched-off PC in the list stops being
    /// something the happy path pays for. See <see cref="HarvestAsync{T}"/>.
    /// </summary>
    private static readonly TimeSpan Grace = TimeSpan.FromMilliseconds(250);

    /// <summary>One probe at a known address, so it can afford to be patient.</summary>
    private static readonly TimeSpan GatewayWait = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Per-host patience while sweeping, which is the number that decides whether this is a
    /// last resort or an eternity: 254 hosts of which 253 will not answer at all.
    /// </summary>
    private static readonly TimeSpan SweepWait = TimeSpan.FromMilliseconds(400);

    /// <summary>How many hosts are probed at once. Enough to be quick, few enough that a phone's
    /// networking stack is not asked to hold two hundred sockets open.</summary>
    private const int Batch = 32;

    /// <summary>
    /// One search at a time.
    ///
    /// Two are the normal case, not the exotic one — the roster asks what the game is doing
    /// while a tap sends an emote — and two at once meant two subnet sweeps, five hundred
    /// sockets, and two racing read-modify-write cycles over the paired list. The second caller
    /// waits and then finds the first one's answer already cached, which is the answer it wanted.
    /// </summary>
    private readonly SemaphoreSlim _searching = new(1, 1);

    private readonly List<string> _notes = [];
    private readonly List<BridgeMove> _moves = [];

    /// <summary>
    /// Addresses already refused in this search, so no step asks twice.
    ///
    /// Four steps can each turn up the same PC at the same address — the gateway and the first
    /// sweep chunk both knock on <c>x.x.x.1</c> — and on the phone that meant the same failed
    /// challenge three times over: three wasted round trips and three identical log lines for
    /// one problem.
    /// </summary>
    private readonly HashSet<string> _refused = new(StringComparer.OrdinalIgnoreCase);

    private ResolvedBridge? _last;
    private DateTimeOffset _at;
    private bool _migrated;
    private string? _swept;

    public KnownBridges Known => known;

    /// <summary>
    /// Why nothing could be found, in the order of how much it helps.
    ///
    /// What was learnt from a PC that actually answered comes first, because it is the only
    /// kind of finding anybody can act on: "that PC answered and its secret does not match any
    /// more" names the fix, and "the broadcast went unanswered" sends people to their firewall.
    /// This used to report only the second one, so a bridge that had been reinstalled read as a
    /// bridge that was not running.
    /// </summary>
    public string? Problem
    {
        get
        {
            List<string> notes;

            lock (_notes) notes = _notes.ToList();

            // A PC that answered and was refused is the whole story, and the network's own
            // account contradicts it: on the phone this read "GKS-0432 answered at … but could
            // not prove it holds the same pairing secret. Nothing answered on 192.168.137. Is
            // the bridge running?" — two opposite sentences about one PC.
            if (notes.Count > 0) return string.Join(" ", notes);

            if (_swept is { } sweep && discovery.Problem is { } both)
                return L.F("{0} Also tried every address on {1} directly.", both, sweep);

            return discovery.Problem;
        }
    }

    /// <summary>
    /// A known PC that has turned up somewhere new and needs one confirmation. Null normally.
    /// </summary>
    public BridgeMove? Move { get; private set; }

    /// <summary>Whether anything is paired at all. Nothing below runs when this is false.</summary>
    public async Task<bool> IsSetUpAsync()
    {
        await MigrateOnceAsync();

        return known.Any;
    }

    /// <summary>Forgets the cached resolution, so the next send looks again.</summary>
    public void Reset()
    {
        _last = null;
        Move = null;
        _swept = null;

        _moves.Clear();
        _refused.Clear();

        lock (_notes) _notes.Clear();
    }

    /// <summary>
    /// The PC to use, or null when none could be reached.
    ///
    /// Null is ignorance in exactly the sense <see cref="PlayedNow"/> means it: no bridge
    /// paired, none on this network, all asleep. It never means "there is no PC".
    /// </summary>
    /// <param name="sweep">
    /// Whether the last-resort subnet sweep is allowed.
    ///
    /// False for any caller that asks over and over. A sweep is a couple of hundred probes, each
    /// an HttpURLConnection through Network.OpenConnection, and each of those leaves an OkHttp
    /// connection thread behind for its pool's keep-alive. Behind a tap that is fine - it happens
    /// once and the threads drain. On the compass screen's reconnect loop it is not: measured on
    /// a device, the process went from 39 threads to 1834 in three minutes beside a switched-off
    /// PC, 1799 of them named "OkHttp Connecti", and was eventually killed with SIGABRT when a
    /// thread stack could no longer be allocated.
    /// </param>
    public async Task<ResolvedBridge?> ResolveAsync(CancellationToken ct = default, bool sweep = true)
    {
        await MigrateOnceAsync();

        if (!known.Any) return null;

        if (Current() is { } ready) return ready;

        await _searching.WaitAsync(ct);

        try
        {
            // Somebody else may have searched while this call was queued behind them, in which
            // case their answer is this call's answer and the network has been asked enough.
            return Current() ?? await SearchAsync(ct, sweep);
        }
        finally
        {
            _searching.Release();
        }
    }

    private ResolvedBridge? Current() =>
        _last is not null && DateTimeOffset.UtcNow - _at < Fresh ? _last : null;

    private async Task<ResolvedBridge?> SearchAsync(CancellationToken ct, bool sweep = true)
    {
        var all = known.All();
        var network = discovery.Network;

        Reset();

        // Which step succeeded is the single most useful line in the log: it says whether the
        // cache is doing its job, whether broadcast reaches anything on this network, and how
        // long somebody waited. Timed for the same reason.
        var clock = Stopwatch.StartNew();

        // Step one: the addresses already known, best first. A hit here is the whole point of
        // caching them and is what makes the common case indistinguishable from the old
        // typed-address behaviour, minus the typing.
        if (await UseAsync(await CachedAsync(all, network, ct), "cached", network, clock, ct)
            is { } cached)
            return cached;

        // Step two: ask the network who is out there. This is the step that makes a DHCP
        // renewal a non-event.
        if (await UseAsync(await BroadcastAsync(all, ct), "broadcast", network, clock, ct)
            is { } found)
            return found;

        // Step three: the gateway, over TCP. One probe, and it is where the bridge actually is
        // whenever the PC is the thing sharing the connection.
        if (await ProbeAsync(all, Gateway(all), GatewayWait, "gateway", network, clock, ct)
            is { } gateway)
            return gateway;

        // Step four: every host on this /24, over TCP. The last resort, and the one that cannot
        // be defeated by a network that drops broadcast — it uses the same transport that
        // carries a paste, so if a paste could reach the PC then so can this.
        // A caller that loops is not allowed the sweep; see the remarks on ResolveAsync.
        if (!sweep) return null;

        _swept = network;

        if (await ProbeAsync(all, Sweep(all, network), SweepWait, "sweep", network, clock, ct)
            is { } swept)
            return swept;

        // Only now, with nothing usable anywhere, is a confirmation worth interrupting anybody
        // for — and only the first one, because a queue of questions is not a question.
        Move = _moves.FirstOrDefault();

        journal.Write("find", $"no PC found on {network ?? "an unknown network"} after " +
                              $"{clock.ElapsedMilliseconds}ms — {Problem ?? "no reason given"}");

        return null;
    }

    /// <summary>
    /// Works down everything that answered and takes the first PC that proves out.
    ///
    /// The loop is the fix. Any single candidate can fail for a reason that says nothing about
    /// the others — a secret that no longer matches, a pairing whose secret is missing from the
    /// keystore, an address waiting on a confirmation — and each of those used to end the step.
    /// </summary>
    private async Task<ResolvedBridge?> UseAsync(
        IReadOnlyList<BridgeAnswer> answers, string how, string? network,
        Stopwatch clock, CancellationToken ct)
    {
        if (answers.Count == 0) return null;

        foreach (var answer in BridgePlan.Rank(await PlayingAsync(answers, ct)))
            if (await AcceptAsync(answer, network, ct) is { } resolved)
                return Keep(resolved, how, clock);

        return null;
    }

    /// <summary>
    /// Marks up the answers with which PC is running the game, when there is a choice to make.
    ///
    /// Skipped outright for a single answer: the round trip could only confirm what is already
    /// the only option. Asked of every candidate in parallel otherwise, because with a desktop
    /// and a laptop both up, the one rendering a character is where the person is sitting.
    /// </summary>
    private async Task<IReadOnlyList<BridgeAnswer>> PlayingAsync(
        IReadOnlyList<BridgeAnswer> answers, CancellationToken ct)
    {
        if (answers.Count < 2) return answers;

        return await Task.WhenAll(answers.Select(async answer =>
            answer with { Playing = await IsPlayingAsync(answer.Address, answer.Bridge, ct) }));
    }

    /// <summary>The paired PCs that answered at an address already on file.</summary>
    private async Task<IReadOnlyList<BridgeAnswer>> CachedAsync(
        IReadOnlyList<KnownBridge> all, string? network, CancellationToken ct)
    {
        var probes = BridgePlan.Addresses(all, network)
            .Select(async address =>
                (Address: address, Identity: await HelloAsync(address, null, AskWait, ct)))
            .ToList();

        var probed = await HarvestAsync(probes, probe => probe.Identity is not null, ct);

        return Answers(all, probed.Select(probe => (probe.Address, probe.Identity)));
    }

    /// <summary>
    /// Waits for the probes that are going to answer, and not for the ones that are not.
    ///
    /// Measured on the phone: a live PC answered <c>/hello</c> in about 4ms and the resolve still
    /// took 2.2 seconds, because <c>Task.WhenAll</c> waits for the slowest probe and one paired
    /// PC in the list was switched off — so the happy path was paying a dead entry's whole
    /// timeout, every time. Cutting that timeout only made the bill smaller; not waiting for it
    /// is the actual fix.
    ///
    /// The grace period is what keeps the ranking honest. Stopping at the first answer would
    /// throw away the choice between two awake PCs — which one is running the game — so once
    /// something answers, everything else gets a moment to answer too, and then the search moves
    /// on. Abandoned probes are left to time out on their own; nothing is waiting on them.
    /// </summary>
    private static async Task<IReadOnlyList<T>> HarvestAsync<T>(
        IReadOnlyList<Task<T>> probes, Func<T, bool> answered, CancellationToken ct)
    {
        var pending = probes.ToList();
        var harvested = new List<T>();

        Task? grace = null;

        while (pending.Count > 0 && !ct.IsCancellationRequested)
        {
            var finished = grace is null
                ? await Task.WhenAny(pending)
                : await Task.WhenAny(pending.Cast<Task>().Append(grace));

            if (ReferenceEquals(finished, grace)) break;

            var probe = (Task<T>)finished;

            pending.Remove(probe);

            var result = await probe;

            harvested.Add(result);

            // Not tied to ct: this is a short wait for stragglers, and cancelling it would leave
            // an unobserved exception behind for the sake of saving 250ms.
            if (grace is null && answered(result)) grace = Task.Delay(Grace, CancellationToken.None);
        }

        return harvested;
    }

    private async Task<IReadOnlyList<BridgeAnswer>> BroadcastAsync(
        IReadOnlyList<KnownBridge> all, CancellationToken ct)
    {
        var seen = await discovery.FindAsync(ct);

        return Answers(
            all, seen.Select(found => (found.Address, (BridgeIdentity?)found.Identity)));
    }

    /// <summary>
    /// Probes a list of addresses over TCP, taking the first paired PC that proves out.
    ///
    /// Chunked rather than fired all at once: two hundred simultaneous connections is a way to
    /// make a phone's networking stack unhappy for no gain, and the answer is usually in the
    /// first chunk anyway because the gateway and the low addresses come first.
    ///
    /// Each chunk is offered up before the next is probed, and a chunk that answers without
    /// yielding anything usable does not end the sweep — otherwise one unusable PC at a low
    /// address hides every PC above it, which is the same mistake as picking one winner per
    /// step, only harder to see.
    /// </summary>
    private async Task<ResolvedBridge?> ProbeAsync(
        IReadOnlyList<KnownBridge> all, IEnumerable<string> addresses, TimeSpan wait,
        string how, string? network, Stopwatch clock, CancellationToken ct)
    {
        foreach (var chunk in addresses.Chunk(Batch))
        {
            var probes = await Task.WhenAll(chunk.Select(async address =>
                (Address: address, Identity: await HelloAsync(address, null, wait, ct))));

            var answers = Answers(all, probes.Select(probe => (probe.Address, probe.Identity)));

            if (await UseAsync(answers, how, network, clock, ct) is { } resolved) return resolved;
        }

        return null;
    }

    /// <summary>Pairs up what answered with the entries in the list it could belong to.</summary>
    private static IReadOnlyList<BridgeAnswer> Answers(
        IReadOnlyList<KnownBridge> all,
        IEnumerable<(string Address, BridgeIdentity? Identity)> probes)
    {
        var answers = new List<BridgeAnswer>();

        foreach (var (address, identity) in probes)
        {
            if (identity is null) continue;

            if (Owner(all, identity, address) is { } owner)
                answers.Add(new BridgeAnswer(owner, address, identity));
        }

        return answers;
    }

    /// <summary>
    /// Which paired PC an answer belongs to, by identity where there is one and by address
    /// otherwise — a bridge too old to have an id still answers at the address it was typed as.
    /// </summary>
    private static KnownBridge? Owner(
        IReadOnlyList<KnownBridge> all, BridgeIdentity identity, string address)
    {
        if (identity.Id.Length > 0 &&
            all.FirstOrDefault(b => b.IsIdentified && b.Id == identity.Id) is { } byId)
            return byId;

        return all.FirstOrDefault(b => Same(b.Address, address));
    }

    private IEnumerable<string> Gateway(IReadOnlyList<KnownBridge> all) =>
        discovery.Gateway is { } gateway
            ? Ports(all).Select(port => $"{gateway}:{port}")
            : [];

    /// <summary>
    /// Every host on the phone's own /24, gateway-ward first.
    ///
    /// Low addresses first because that is where a machine with a static lease or an early DHCP
    /// grant tends to sit. Nothing is excluded — the phone's own address answering would simply
    /// not be a bridge, and one wasted probe is cheaper than the arithmetic to skip it.
    /// </summary>
    private static IEnumerable<string> Sweep(IReadOnlyList<KnownBridge> all, string? network)
    {
        if (network is null) yield break;

        var ports = Ports(all);

        for (var host = 1; host <= 254; host++)
            foreach (var port in ports)
                yield return $"{network}.{host}:{port}";
    }

    /// <summary>
    /// The ports worth knocking on, which is the ones the paired PCs said they listen on.
    ///
    /// Only matters where the address is being guessed at. A PC moved off the default port used
    /// to be findable by broadcast alone, because the card's port was read, used to pair, and
    /// then dropped on the floor.
    /// </summary>
    private static IReadOnlyList<int> Ports(IReadOnlyList<KnownBridge> all) =>
        all.Select(b => b.Listens)
            .Distinct()
            .DefaultIfEmpty(BridgeProtocol.DefaultPort)
            .ToList();

    /// <summary>
    /// Decides whether an answer at this address may be used, and remembers it if so.
    ///
    /// The two tiers of pairing meet here. A scanned pairing is checked with a nonce and a
    /// proof, so a new address is safe to adopt on the spot. A typed one cannot be checked at
    /// all, so a new address is noted as a <see cref="BridgeMove"/> and used only once the
    /// user has said yes.
    ///
    /// Every refusal leaves a sentence behind. A PC that answers and is then declined is the
    /// single most confusing state this app has, and it used to report itself as "no PC
    /// answered" — which sends somebody to look at a bridge that is running perfectly.
    /// </summary>
    private async Task<ResolvedBridge?> AcceptAsync(
        BridgeAnswer answer, string? network, CancellationToken ct)
    {
        var (bridge, address, identity, _) = answer;

        if (_refused.Contains($"{bridge.Id}@{address}")) return null;

        if (await known.SecretAsync(bridge.Id) is not { Length: > 0 } secret)
        {
            Note(L.F("{0} is in the list of paired PCs but this phone has no pairing " +
                     "secret for it. Forget it and pair it again.", bridge.Label));

            return null;
        }

        if (BridgeProtocol.IsStrong(secret))
        {
            if (!await ProvenAsync(address, secret, ct))
            {
                // Something answered and could not prove it holds the secret: either the PC
                // rotated its code, or that is not the PC it claims to be.
                Note(L.F("{0} answered at {1} but could not prove it holds the " +
                         "same pairing secret. If the bridge was reinstalled or its settings were " +
                         "cleared, scan its QR code again.", bridge.Label, address));

                journal.Write("find", $"{address} claims to be {bridge.Label} and failed the " +
                                      "challenge — not using it");

                _refused.Add($"{bridge.Id}@{address}");

                return null;
            }
        }
        else if (answer.Moved)
        {
            journal.Write("find", $"{bridge.Label} answers at {address} instead of " +
                                  $"{bridge.Address ?? "nowhere"} — waiting to be allowed, " +
                                  "because a typed code cannot prove which machine it is");

            _moves.Add(new BridgeMove(bridge, bridge.Address, address));

            return null;
        }

        var settled = bridge.IsIdentified || identity is null
            ? bridge
            : await known.AdoptAsync(bridge, identity);

        var current = settled
                .Seen(address, network ?? BridgePlan.NetworkOf(address), DateTimeOffset.UtcNow)
            with
            {
                Port = identity is { Port: > 0 } said ? said.Port : settled.Port
            };

        known.Save(current);

        return new ResolvedBridge(current, address, secret);
    }

    /// <summary>
    /// Accepts the address the user confirmed for a typed pairing, and forgets the question.
    /// </summary>
    public void Allow(BridgeMove move)
    {
        known.Save(move.Bridge.Seen(move.To, BridgePlan.NetworkOf(move.To), DateTimeOffset.UtcNow));

        Move = null;
        _last = null;

        _moves.Clear();
    }

    /// <summary>
    /// Whether the bridge at this address holds the same secret.
    ///
    /// A fresh nonce every time. Reusing one would let anything that overheard a single valid
    /// exchange replay it back at the phone for ever.
    /// </summary>
    private async Task<bool> ProvenAsync(string address, string secret, CancellationToken ct)
    {
        var nonce = BridgeProtocol.NewNonce();

        return await HelloAsync(address, nonce, AskWait, ct) is { } identity &&
               BridgeProtocol.ProofsMatch(identity.Proof, BridgeProtocol.Proof(secret, nonce));
    }

    private async Task<BridgeIdentity?> HelloAsync(
        string address, string? nonce, TimeSpan? wait, CancellationToken ct)
    {
        var path = nonce is null
            ? BridgeProtocol.HelloPath
            : $"{BridgeProtocol.HelloPath}?nonce={nonce}";

        var answer = await transport.SendAsync(
            Url(address, path), HttpMethod.Get, null, null, ct, wait);

        if (!answer.Ok) return null;

        try
        {
            return JsonSerializer.Deserialize(answer.Body, ProtocolJson.Default.BridgeIdentity);
        }
        catch (JsonException)
        {
            // Something answered on that port and it was not a bridge.
            return null;
        }
    }

    private async Task<bool> IsPlayingAsync(string address, KnownBridge bridge, CancellationToken ct)
    {
        if (await known.SecretAsync(bridge.Id) is not { Length: > 0 } secret) return false;

        var answer = await transport.SendAsync(
            Url(address, BridgeProtocol.GamePath), HttpMethod.Get, null, secret, ct, AskWait);

        if (!answer.Ok) return false;

        try
        {
            return JsonSerializer.Deserialize(answer.Body, BridgeJson.Default.GameReport)
                is { IsLive: true };
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Keeps one finding, once. The same PC is probed more than once in a search and the same
    /// sentence twice reads as two problems.
    /// </summary>
    private void Note(string sentence)
    {
        lock (_notes)
        {
            if (!_notes.Contains(sentence)) _notes.Add(sentence);
        }
    }

    private async Task MigrateOnceAsync()
    {
        if (_migrated) return;

        _migrated = true;

        await known.MigrateAsync();
    }

    private ResolvedBridge Keep(ResolvedBridge resolved, string how, Stopwatch clock)
    {
        _last = resolved;
        _at = DateTimeOffset.UtcNow;

        journal.Write("find", $"{resolved.Bridge.Label} at {resolved.Address} " +
                              $"via {how} in {clock.ElapsedMilliseconds}ms");

        return resolved;
    }

    /// <summary>Plain HTTP: a LAN address has no certificate and no hostname to put one on.</summary>
    internal static string Url(string address, string path)
    {
        var host = address.Trim();

        return host.Contains(':')
            ? $"http://{host}{path}"
            : $"http://{host}:{BridgeProtocol.DefaultPort}{path}";
    }

    private static bool Same(string? a, string? b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
