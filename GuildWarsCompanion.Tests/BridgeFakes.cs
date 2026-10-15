using System.Text.Json;

using GuildWarsCompanion.Bridge.Protocol;
using GuildWarsCompanion.Services;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// The paired list and the secrets, in memory.
///
/// The whole reason <see cref="IBridgeStore"/> exists. On the phone this is preferences plus
/// the Android Keystore, neither of which a test host can load; here it is two dictionaries,
/// and <see cref="KnownBridges"/> cannot tell the difference.
/// </summary>
public sealed class MemoryBridgeStore : IBridgeStore
{
    private List<KnownBridge> _all = [];
    private readonly Dictionary<string, string> _secrets = new(StringComparer.Ordinal);

    public string? LegacyHost { get; set; }
    public string? LegacyCode { get; set; }

    public IReadOnlyList<KnownBridge> Read() => _all.ToList();

    public void Write(IReadOnlyList<KnownBridge> all) => _all = all.ToList();

    public Task<string?> SecretAsync(string id) =>
        Task.FromResult(_secrets.GetValueOrDefault(id));

    public Task SetSecretAsync(string id, string secret)
    {
        _secrets[id] = secret;

        return Task.CompletedTask;
    }

    public void RemoveSecret(string id) => _secrets.Remove(id);

    public (string? Host, string? Code) LegacyPairing() => (LegacyHost, LegacyCode);

    public void ClearLegacyPairing()
    {
        LegacyHost = null;
        LegacyCode = null;
    }

    public IReadOnlyList<string> SecretIds => _secrets.Keys.OrderBy(k => k).ToList();
}

/// <summary>
/// One PC, as the network would present it: an id, a secret, and whether it is switched on.
/// </summary>
public sealed class FakePc(string id, string name, string secret, string address)
{
    public string Id => id;
    public string Name => name;
    public string Secret => secret;
    public string Address => address;

    /// <summary>Whether it answers at all. A PC that is off is the ordinary case, not an edge.</summary>
    public bool Running { get; set; } = true;

    /// <summary>Whether it answers the broadcast. Off for a firewall that allows only TCP.</summary>
    public bool Discoverable { get; set; } = true;

    /// <summary>Whether the game is up on it.</summary>
    public bool Playing { get; set; }

    /// <summary>Who is in the game while <see cref="Playing"/>; null reports a game with nobody named.</summary>
    public string? Character { get; set; }

    /// <summary>How it answers /notify: 204 shown in the game, 409 no module to show it. Null is a
    /// bridge from before alerts, which does not know the path.</summary>
    public int? NotifyStatus { get; set; }

    public int Port { get; set; } = BridgeProtocol.DefaultPort;

    /// <summary>How it answers /emote: 200 done in game, 204 copied. Null is a bridge from
    /// before /emote existed, which answers 400 like any path it does not know.</summary>
    public int? EmoteStatus { get; set; }

    /// <summary>A bridge from before the objective's name, which refuses a trail that has one.</summary>
    public bool RefusesNames { get; set; }

    /// <summary>What this PC will actually prove with — a reinstall changes it and not the id.</summary>
    public string? ProvesWith { get; set; }

    public BridgeCard Card => new(Id, Name, Secret, Port);

    public BridgeIdentity Identity(string? nonce) =>
        new(Id, Name, Port, BridgeProtocol.Proof(ProvesWith ?? Secret, nonce));
}

/// <summary>
/// A network of fake PCs behind the transport interface, so a whole search can be watched.
/// </summary>
public sealed class FakeNetwork : IBridgeTransport, IBridgeDiscovery
{
    private readonly List<FakePc> _pcs = [];

    public FakeNetwork(string? network = "192.168.1", string? gateway = null)
    {
        Network = network;
        Gateway = gateway;
    }

    public string? Network { get; }

    public string? Problem { get; private set; }

    public string? Gateway { get; }

    /// <summary>Every URL asked for, in order. The evidence for "it never even tried".</summary>
    public List<string> Asked { get; } = [];

    /// <summary>The body sent with each of <see cref="Asked"/>, in the same order.</summary>
    public List<string?> Bodies { get; } = [];

    /// <summary>Whether the broadcast reaches anything at all.</summary>
    public bool BroadcastWorks { get; set; } = true;

    public FakeNetwork With(FakePc pc)
    {
        _pcs.Add(pc);

        return this;
    }

    public Task<BridgeResponse> SendAsync(
        string url, HttpMethod method, string? body, string? code, CancellationToken ct,
        TimeSpan? timeout = null)
    {
        lock (Asked)
        {
            Asked.Add(url);
            Bodies.Add(body);
        }

        var uri = new Uri(url);
        var at = $"{uri.Host}:{uri.Port}";

        if (_pcs.FirstOrDefault(pc => pc.Running && pc.Address == at) is not { } found)
            return Task.FromResult(BridgeResponse.Failed("No route to host"));

        if (BridgeProtocol.PathIs(uri.AbsolutePath, BridgeProtocol.HelloPath))
        {
            var nonce = uri.Query.Contains("nonce=")
                ? uri.Query.Split("nonce=")[1].Split('&')[0]
                : null;

            return Task.FromResult(new BridgeResponse(
                200,
                JsonSerializer.Serialize(found.Identity(nonce), ProtocolJson.Default.BridgeIdentity),
                null));
        }

        if (!BridgeProtocol.Accepts(code, null, found.ProvesWith ?? found.Secret))
            return Task.FromResult(new BridgeResponse(403, "Wrong pairing code", null));

        if (BridgeProtocol.PathIs(uri.AbsolutePath, BridgeProtocol.GamePath))
            return Task.FromResult(new BridgeResponse(
                200,
                JsonSerializer.Serialize(
                    new GameReport(
                        found.Playing && found.Character is { } name
                            ? new GameState(1, name, 4, 0, 3, false, 15, 1, 1, 0, UiState.GameHasFocus, 0, 0)
                            : null,
                        found.Playing, "as the fake PC has it"),
                    BridgeJson.Default.GameReport),
                null));

        if (BridgeProtocol.PathIs(uri.AbsolutePath, BridgeProtocol.PastePath))
            return Task.FromResult(new BridgeResponse(204, string.Empty, null));

        if (BridgeProtocol.PathIs(uri.AbsolutePath, BridgeProtocol.NotifyPath))
            return Task.FromResult(new BridgeResponse(found.NotifyStatus ?? 404, string.Empty, null));

        if (BridgeProtocol.PathIs(uri.AbsolutePath, BridgeProtocol.GuidePath))
            return Task.FromResult(new BridgeResponse(
                found.RefusesNames && body?.Contains("\nname ") == true ? 400 : 204, string.Empty, null));

        if (BridgeProtocol.PathIs(uri.AbsolutePath, BridgeProtocol.EmotePath))
            return Task.FromResult(new BridgeResponse(found.EmoteStatus ?? 400, string.Empty, null));

        return Task.FromResult(new BridgeResponse(404, string.Empty, null));
    }

    public Task<IReadOnlyList<DiscoveredBridge>> FindAsync(CancellationToken ct = default)
    {
        if (!BroadcastWorks)
        {
            Problem = "Nothing answered the broadcast.";

            return Task.FromResult<IReadOnlyList<DiscoveredBridge>>([]);
        }

        Problem = null;

        IReadOnlyList<DiscoveredBridge> seen = _pcs
            .Where(pc => pc.Running && pc.Discoverable)
            .Select(pc => new DiscoveredBridge(pc.Address, pc.Identity(null)))
            .ToList();

        if (seen.Count == 0) Problem = "Nothing answered the broadcast.";

        return Task.FromResult(seen);
    }
}
