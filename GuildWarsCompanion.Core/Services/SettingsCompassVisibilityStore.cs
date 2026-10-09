using GuildWarsCompanion.Host;

namespace GuildWarsCompanion.Services;

/// <summary>
/// The real store: plain settings, one line each.
///
/// Kept in its own file, away from <see cref="CompassVisibility"/>, for the same reason
/// <see cref="SettingsBridgeStore"/> is — this only stores, and nothing here decides anything.
///
/// Newline-separated rather than JSON. The values are a handful of kind names and a list of
/// "map:kind:x:y" keys with no separator of their own, so a format that needs a serialiser and a
/// source-generated context would be ceremony around a join.
/// </summary>
public sealed class SettingsCompassVisibilityStore(ISettings settings) : ICompassVisibilityStore
{
    /// <summary>Not per character on purpose: a player who does not walk to vistas does not walk
    /// to them on their alt either. See <see cref="CompassVisibility"/>.</summary>
    private const string KindsKey = "compass_hidden_kinds";

    private const string DismissedPrefix = "compass_dismissed_";

    public CompassVisibility Read(string character)
    {
        var kinds = new HashSet<TargetKind>();

        foreach (var name in Split(settings.Get(KindsKey, string.Empty)))
            if (Enum.TryParse<TargetKind>(name, out var kind))
                kinds.Add(kind);

        // An unknown name is dropped rather than refused. A kind removed in a later version
        // should cost its own line, not everything stored beside it.

        var dismissed = new HashSet<string>(
            character.Length == 0
                ? []
                : Split(settings.Get(DismissedPrefix + character, string.Empty)),
            StringComparer.Ordinal);

        return new CompassVisibility(kinds, dismissed);
    }

    public void Write(string character, CompassVisibility visibility)
    {
        settings.Set(KindsKey, string.Join('\n', visibility.HiddenKinds));

        if (character.Length == 0) return;

        settings.Set(
            DismissedPrefix + character, string.Join('\n', visibility.Dismissed));
    }

    private static string[] Split(string stored) =>
        stored.Length == 0
            ? []
            : stored.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
