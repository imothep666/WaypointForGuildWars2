using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace GuildWarsCompanion.Bridge.Protocol;

/// <summary>The one field wanted out of <c>/v2/maps/{id}</c>, of the dozen it returns.</summary>
internal sealed record Gw2Map(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string? Name);

/// <summary>Kept out of <c>GameJson</c>: that context is shared with the phone app, and the
/// phone has no business knowing the bridge talks to the API.</summary>
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(Gw2Map))]
internal partial class MapJson : JsonSerializerContext;

/// <summary>
/// Asking api.guildwars2.com what a map id is called.
///
/// The only request the bridge makes about the game, and the reason it can be made at all is
/// that this endpoint is public: no API key, no account, nothing about the person playing leaves
/// this PC. That was a condition of the design — the bridge deliberately holds no key, and
/// resolving a map name must not be the thing that changes that.
///
/// No rate limiting, unlike the phone's <c>Gw2Client</c>, and it is not an oversight: the
/// caller asks once per map id ever and <see cref="MapNames"/> remembers the answer, so a long
/// evening of playing is a handful of requests against an allowance of 600 a minute.
/// </summary>
public static class Gw2Maps
{
    public const string Endpoint = "https://api.guildwars2.com/v2/maps/";

    /// <summary>
    /// The same schema the phone app pins, for the same reason: ANet's breaking changes should
    /// land when they are chosen rather than when they ship. Repeated rather than shared
    /// because the bridge does not reference the app, and this is two lines of a contract
    /// against a whole project reference.
    /// </summary>
    private const string SchemaVersion = "2024-07-20T01:00:00.000Z";

    /// <summary>
    /// One for the process. A client per request is the classic way to exhaust sockets, and a
    /// pooled connection lifetime keeps a long-running bridge from pinning a stale DNS answer.
    /// </summary>
    private static readonly HttpClient Http = new(new SocketsHttpHandler
    {
        AutomaticDecompression = DecompressionMethods.All,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5)
    })
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    /// <summary>A cache wired to the real API. The bridge's only way of making one.</summary>
    public static MapNames Names() => new(FetchAsync);

    /// <summary>
    /// The name, or null if there is no such map.
    ///
    /// Everything else throws, and the difference matters: null is an answer and is kept, while
    /// an exception is a failure and is retried later. See <see cref="MapNames.ResolveAsync"/>,
    /// which is the only caller and catches on purpose.
    /// </summary>
    public static async Task<string?> FetchAsync(int mapId, CancellationToken ct = default)
    {
        using var answer = await Http
            .GetAsync($"{Endpoint}{mapId}?v={SchemaVersion}", ct)
            .ConfigureAwait(false);

        // {"text":"no such id"} — asking again tomorrow will say the same thing.
        if (answer.StatusCode == HttpStatusCode.NotFound) return null;

        answer.EnsureSuccessStatusCode();

        var map = await answer.Content
            .ReadFromJsonAsync(MapJson.Default.Gw2Map, ct)
            .ConfigureAwait(false);

        return map?.Name;
    }
}
