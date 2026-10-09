using GuildWarsCompanion.Host;

namespace GuildWarsCompanion.Services;

/// <summary>
/// The real store for chosen route categories: one setting, newline separated.
///
/// Its own file, away from <see cref="RoutePicks"/>, for the same reason
/// <see cref="SettingsCompassVisibilityStore"/> is away from <see cref="CompassVisibility"/> —
/// this only stores, and nothing here decides anything.
///
/// Not per map. A player who wants map completion trails wants them in the next zone too, which
/// is the whole point of a pack that carries fifty.
/// </summary>
public sealed class SettingsRouteChoiceStore(ISettings settings) : IRouteChoiceStore
{
    private const string Key = "map_routes_shown";

    public IReadOnlySet<string> Read() =>
        new HashSet<string>(
            settings.Get(Key, string.Empty)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries),
            StringComparer.Ordinal);

    public void Write(IReadOnlySet<string> chosen) =>
        settings.Set(Key, string.Join('\n', chosen));
}
