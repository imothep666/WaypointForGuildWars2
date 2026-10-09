using GuildWarsCompanion.Data;
using GuildWarsCompanion.Gw2;

namespace GuildWarsCompanion.Services;

/// <summary>
/// Every map in the game, by name, so the map screen can be reached without the bridge.
///
/// The map screen draws where the character is standing, which means it has nothing at all to
/// show on a train — exactly when somebody wants to look at Frostgorge and work out where they
/// are going. This is the other way in: a list, a search box, and a map id.
///
/// <b>One request, then never again until the build moves.</b> <c>/v2/maps?ids=all</c> is about
/// eight hundred maps and a megabyte of JSON, which is a lot to ask for once and absurd to ask
/// for twice — so it lands in <see cref="Gw2Cache"/> beside the currencies, which is the store
/// for things that only change when ArenaNet ships a build. A phone with no connection and a
/// filled cache has the whole list; a phone with neither says so rather than showing an empty
/// search and letting somebody conclude the game has no maps in it.
///
/// What counts as a map worth offering, and what a typed name matches, is
/// <see cref="MapPicks"/>. This class is the fetching and the keeping.
/// </summary>
public sealed class MapCatalogue(Gw2Client api, Gw2Cache cache)
{
    private IReadOnlyList<MapChoice>? _choices;

    /// <summary>What the last fill did, when it could not do the usual thing.</summary>
    public string? Note { get; private set; }

    /// <summary>
    /// The whole list, sorted by name.
    ///
    /// Held for the run once built. The search runs against it a letter at a time, and a
    /// database query per keystroke is a worse answer than one list in hand.
    /// </summary>
    public async Task<IReadOnlyList<MapChoice>> AllAsync(CancellationToken ct = default)
    {
        if (_choices is { } held) return held;

        // "Has the whole list been stored", not "are there any rows". The compass writes the one
        // map a character is standing in as it goes, so a row count answers yes to a database
        // holding Queensdale and nothing else — which is exactly what this screen first showed.
        List<MapInfo> stored = await cache.HasAllMapsAsync(ct)
            ? await cache.GetMapsAsync(ct)
            : [];

        if (stored.Count == 0)
        {
            // Nothing stored and no connection is the one case this cannot answer, and it says
            // so through Note rather than by throwing: the screen that asked still has a search
            // box to draw and a sentence to put under it.
            try
            {
                var fetched = await api.GetAllMapsAsync(ct);

                await cache.SaveMapsAsync(fetched, replaceAll: true, ct);

                stored = fetched;
                Note = null;
            }
            catch (Gw2ApiException ex)
            {
                Note = Describe(ex);

                return [];
            }
        }

        _choices = MapPicks.Choices(stored);

        return _choices;
    }

    /// <summary>Maps whose name contains what was typed. See <see cref="MapPicks.Search"/>.</summary>
    public async Task<IReadOnlyList<MapChoice>> SearchAsync(
        string term, CancellationToken ct = default) =>
        MapPicks.Search(await AllAsync(ct), term);

    private static string Describe(Gw2ApiException ex) => ex.Kind switch
    {
        Gw2ErrorKind.Network =>
            "No connection, and no map list stored yet. Open this once with a connection and it " +
            "works offline from then on.",
        _ => $"The map list could not be read — {ex.Message}"
    };
}
