using System.Text.Json;
using GuildWarsCompanion.Host;

namespace GuildWarsCompanion.Services;

/// <summary>
/// The wiki's meta timetable, fetched now and then and kept on the phone.
///
/// Fetched rather than shipped, so a new expansion's metas arrive when the wiki's editors add them
/// rather than with the next build — Visions of Eternity is in it already. A week between fetches:
/// the schedule changes with releases, not daily. With no connection the last copy is used however
/// old; with no copy at all there is simply no meta timetable, and the screen says so.
///
/// A festival's events are in the same data all year. They are added to the maps only while
/// <see cref="Festivals"/> says the festival is on, as one more map under the festival's name, so
/// the timetable, its reminders and the alerts take them as they take any map's.
/// </summary>
public sealed class MetaSource(AppEnvironment environment)
{
    public const string Address =
        "https://wiki.guildwars2.com/index.php?title=Widget:Event_timer/data.json&action=raw";

    private static readonly TimeSpan Fresh = TimeSpan.FromDays(7);

    private static readonly HttpClient Http = Client();

    private IReadOnlyList<MetaMap>? _maps;
    private DateTimeOffset _readAt;

    private string FilePath => Path.Combine(environment.DataDirectory, "metas.json");

    /// <summary>Why there is no timetable, when there is none.</summary>
    public string? Problem { get; private set; }

    private IReadOnlyList<MetaMap> _festivals = [];

    public async Task<IReadOnlyList<MetaMap>> GetAsync(CancellationToken ct = default) =>
        WithFestivals(await MapsAsync(ct), _festivals, DateTimeOffset.UtcNow);

    /// <summary>The festival entries whether or not they are on, by their timer keys.</summary>
    public async Task<IReadOnlyList<MetaMap>> FestivalsAsync(CancellationToken ct = default)
    {
        await MapsAsync(ct);
        return _festivals;
    }

    /// <summary>
    /// Each festival on at <paramref name="utcNow"/> that has timer data, then the maps. A festival
    /// keeps the data's name, and carries its own name and last day where a map has its expansion.
    /// </summary>
    public static IReadOnlyList<MetaMap> WithFestivals(
        IReadOnlyList<MetaMap> maps, IReadOnlyList<MetaMap> festivals, DateTimeOffset utcNow)
    {
        var on = Festivals.On(Festivals.Announced, utcNow)
            .Select(period => festivals.FirstOrDefault(entry => entry.Key == period.Festival.TimerKey) is { } entry
                ? entry with { Category = $"{period.Festival.Name} · {period.Until}" }
                : null)
            .OfType<MetaMap>()
            .ToList();

        // First, so a festival event on now leads the many map metas that are too.
        return on.Count == 0 ? maps : [.. on, .. maps];
    }

    private async Task<IReadOnlyList<MetaMap>> MapsAsync(CancellationToken ct)
    {
        if (_maps is not null && DateTimeOffset.UtcNow - _readAt < Fresh) return _maps;

        var stored = File.Exists(FilePath) ? new FileInfo(FilePath) : null;

        if (stored is null || DateTimeOffset.UtcNow - stored.LastWriteTimeUtc > Fresh)
            await DownloadAsync(ct);

        if (!File.Exists(FilePath)) return [];

        try
        {
            var json = await File.ReadAllTextAsync(FilePath, ct);
            _maps = MetaSchedule.Parse(json);
            _festivals = MetaSchedule.ParseFestivals(json);
            _readAt = DateTimeOffset.UtcNow;
            Problem = null;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            // A page edited into something else entirely. Say so; do not invent a timetable.
            Problem = L.T("The Guild Wars 2 Wiki's event timer data could not be read.");
            _maps = [];
        }

        return _maps;
    }

    /// <summary>
    /// The timetable as kept, never fetched: for the Here widget, which is drawn when the launcher
    /// asks and must not wait on the wiki. Empty when no copy has been kept yet.
    /// </summary>
    public async Task<IReadOnlyList<MetaMap>> HeldAsync(CancellationToken ct = default)
    {
        if (_maps is not null) return WithFestivals(_maps, _festivals, DateTimeOffset.UtcNow);
        if (!File.Exists(FilePath)) return [];

        try
        {
            var json = await File.ReadAllTextAsync(FilePath, ct);
            return WithFestivals(MetaSchedule.Parse(json), MetaSchedule.ParseFestivals(json), DateTimeOffset.UtcNow);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or IOException)
        {
            return [];
        }
    }

    private async Task DownloadAsync(CancellationToken ct)
    {
        try
        {
            using var leash = CancellationTokenSource.CreateLinkedTokenSource(ct);
            leash.CancelAfter(TimeSpan.FromSeconds(20));

            var json = await Http.GetStringAsync(Address, leash.Token);

            // Only a copy that parses replaces the one already kept.
            MetaSchedule.Parse(json);

            await File.WriteAllTextAsync(FilePath, json, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException
                                       or KeyNotFoundException or InvalidOperationException)
        {
            if (ct.IsCancellationRequested) throw;

            if (!File.Exists(FilePath))
                Problem = L.T("The meta timetable comes from the Guild Wars 2 Wiki and could not be fetched.");
        }
    }

    /// <summary>The app's own name, as the marker pack download sends it.</summary>
    private static HttpClient Client()
    {
        var http = new HttpClient();

        http.DefaultRequestHeaders.UserAgent.ParseAdd("GuildWarsCompanion/1.0");

        return http;
    }
}
