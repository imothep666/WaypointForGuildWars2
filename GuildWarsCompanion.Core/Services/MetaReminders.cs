using GuildWarsCompanion.Host;

namespace GuildWarsCompanion.Services;

/// <summary>
/// Which meta events somebody wants to hear about, kept as "map key|event name" lines.
///
/// Per event, not per map. Auric Basin has four named stretches — pylons, challenges, Octovine, a
/// reset — and the wiki's data does not say which one is the meta; a reminder for each would be
/// four a cycle, every two hours, which is how notifications get switched off. So the player
/// picks the one they go for. Newline-separated for the same reason as the compass's hidden
/// marks: a list of keys does not need a serialiser.
/// </summary>
public sealed class MetaReminderStore(ISettings settings)
{
    private const string Key = "meta_reminders";

    private HashSet<string>? _chosen;

    private HashSet<string> Chosen =>
        _chosen ??= new HashSet<string>(
            settings.Get(Key, string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries),
            StringComparer.Ordinal);

    public static string For(string mapKey, string eventName) => $"{mapKey}|{eventName}";

    public bool IsChosen(string mapKey, string eventName) => Chosen.Contains(For(mapKey, eventName));

    public IReadOnlyCollection<string> All => Chosen;

    public void Set(string mapKey, string eventName, bool remind)
    {
        if (remind) Chosen.Add(For(mapKey, eventName));
        else Chosen.Remove(For(mapKey, eventName));

        settings.Set(Key, string.Join('\n', Chosen.Order(StringComparer.Ordinal)));
    }
}

/// <summary>A meta event about to start that somebody asked to hear about.</summary>
public sealed record MetaCall(string MapKey, string MapName, string Event, DateTimeOffset At, int MinutesAway)
{
    /// <summary>One alert per occurrence: the same event two hours later is news again.</summary>
    public string Key => $"meta:{MapKey}:{Event}:{At.UtcDateTime:yyyyMMddHHmm}";
}

/// <summary>
/// Choosing which chosen metas to announce on this poll. The same window and the same ceiling as
/// world bosses, for the same reasons — see <see cref="BossAlerts.Window"/>.
/// </summary>
public static class MetaAlerts
{
    public static IReadOnlyList<MetaCall> Due(
        IEnumerable<MetaMap> maps, IReadOnlyCollection<string> chosen, DateTimeOffset utcNow)
    {
        if (chosen.Count == 0) return [];

        var calls = new List<MetaCall>();

        foreach (var map in maps)
            foreach (var name in MetaSchedule.Events(map))
            {
                if (!chosen.Contains(MetaReminderStore.For(map.Key, name))) continue;

                if (MetaSchedule.NextOf(map, name, utcNow) is not { } next) continue;

                var wait = next.Start - utcNow;
                if (wait > BossAlerts.Window) continue;

                calls.Add(new MetaCall(map.Key, map.Name, name, next.Start,
                    (int)Math.Round(wait.TotalMinutes, MidpointRounding.AwayFromZero)));
            }

        return calls.OrderBy(call => call.At).Take(BossAlerts.MostAtOnce).ToList();
    }

    /// <summary>"Battle in Tarir (Octovine) in 20 minutes".</summary>
    public static string Title(MetaCall call) => call.MinutesAway switch
    {
        <= 0 => $"{call.Event} is starting",
        1 => $"{call.Event} in a minute",
        _ => $"{call.Event} in {call.MinutesAway} minutes"
    };

    public static string Body(MetaCall call) => $"{call.MapName} · at {call.At.ToLocalTime():HH:mm}";
}
