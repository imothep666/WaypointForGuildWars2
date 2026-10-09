using GuildWarsCompanion.Host;

namespace GuildWarsCompanion.Services;

/// <summary>
/// The game's own map marker art, fetched once and kept on disk.
///
/// Drawn on the compass rather than shapes invented here, because a player already knows what a
/// heart, a waypoint and a vista look like from the in-game map — the recognition is free and
/// anything else would have to be learnt. /v2/files publishes them; render.guildwars2.com serves
/// them.
///
/// Cached to <see cref="AppEnvironment.CacheDirectory"/> on first success, and read from there
/// afterwards. Five small PNGs, so this is not about bandwidth: it is so the compass still draws
/// its symbols on a phone whose Wi-Fi reaches the PC bridge and nothing else, which is exactly
/// the network this feature is used on.
/// </summary>
public sealed class MapIcons(Gw2.Gw2Client api, IImageDecoder decoder, AppEnvironment environment)
{
    /// <summary>
    /// Which asset stands for which kind of target.
    ///
    /// <c>map_heart_empty</c> rather than <c>map_heart_full</c>: the compass points at things
    /// still worth going to. A world boss borrows <c>map_special_event</c>, which is the closest
    /// thing the game ships — there is no world boss marker, because in the game the boss is an
    /// event on the map rather than a place.
    /// </summary>
    private static readonly Dictionary<TargetKind, string> Assets = new()
    {
        [TargetKind.Heart] = "map_heart_empty",
        [TargetKind.Waypoint] = "map_waypoint",
        [TargetKind.Landmark] = "map_poi",
        [TargetKind.Vista] = "map_vista",
        [TargetKind.HeroPoint] = "map_heropoint",
        [TargetKind.WorldBoss] = "map_special_event"
    };

    private readonly Dictionary<TargetKind, IImage> _loaded = [];

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };

    private bool _tried;

    /// <summary>
    /// The symbol for a kind, or null if it is not available.
    ///
    /// Null is a drawable state, not an error: <c>CompassRose</c> falls back to a plain dot. A
    /// compass that refused to draw because an icon would not download would be a worse answer
    /// than one drawn in dots.
    /// </summary>
    public IImage? For(TargetKind kind) => _loaded.GetValueOrDefault(kind);

    /// <summary>
    /// Loads every symbol, from disk where possible and from the network otherwise.
    ///
    /// Runs once per app run. Failures are swallowed per icon rather than in one block, so a
    /// single missing file costs one symbol instead of all five.
    /// </summary>
    public async Task LoadAsync(CancellationToken ct)
    {
        if (_tried) return;

        _tried = true;

        var folder = Path.Combine(environment.CacheDirectory, "mapicons");

        Directory.CreateDirectory(folder);

        // From disk first, so a phone with no internet still gets its symbols.
        foreach (var (kind, asset) in Assets)
        {
            var path = Path.Combine(folder, $"{asset}.png");

            if (File.Exists(path)) Remember(kind, path);
        }

        if (_loaded.Count == Assets.Count) return;

        List<Gw2.Gw2File> files;

        try
        {
            files = await api.GetFilesAsync(string.Join(',', Assets.Values), ct);
        }
        catch (Exception)
        {
            // No network, or the API is down. Whatever came off disk is what the dial gets.
            return;
        }

        var urls = files.ToDictionary(f => f.Id, f => f.Icon, StringComparer.OrdinalIgnoreCase);

        foreach (var (kind, asset) in Assets)
        {
            if (_loaded.ContainsKey(kind)) continue;

            if (!urls.TryGetValue(asset, out var url)) continue;

            var path = Path.Combine(folder, $"{asset}.png");

            try
            {
                var bytes = await _http.GetByteArrayAsync(url, ct);

                await File.WriteAllBytesAsync(path, bytes, ct);

                Remember(kind, path);
            }
            catch (Exception)
            {
                // One icon short. The rose draws a dot for it.
            }
        }
    }

    private void Remember(TargetKind kind, string path)
    {
        try
        {
            using var stream = File.OpenRead(path);

            if (decoder.Decode(stream) is { } image) _loaded[kind] = image;
        }
        catch (Exception)
        {
            // A half-written or corrupt cache file. Drop it so the next run fetches again.
            try
            {
                File.Delete(path);
            }
            catch (Exception)
            {
                // Nothing useful to do about a file that will not delete.
            }
        }
    }
}
