using System.Text.Json;
using GuildWarsCompanion.Host;

namespace GuildWarsCompanion.Services;

/// <summary>
/// When the festivals run, read from their pages on the wiki and kept on the phone.
///
/// Each festival's page lists every year's run by its first and last day, the coming one as soon
/// as ArenaNet announces it; one request reads all six pages. Fetched once a day, as an
/// announcement comes a week or two ahead. With no connection the last copy is used however old;
/// with no copy at all, the dates the app was built with (<see cref="Festivals.Shipped"/>).
/// </summary>
public sealed class FestivalSource(AppEnvironment environment)
{
    public static readonly string Address =
        "https://wiki.guildwars2.com/api.php?action=query&format=json&formatversion=2" +
        "&prop=revisions&rvprop=content&rvslots=main&titles=" +
        Uri.EscapeDataString(string.Join("|", Festivals.All.Select(festival => festival.English)));

    private static readonly TimeSpan Fresh = TimeSpan.FromDays(1);

    /// <summary>Offline, a fetch is not tried again on every page drawn.</summary>
    private static readonly TimeSpan Retry = TimeSpan.FromMinutes(15);

    private static readonly HttpClient Http = Client();

    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<FestivalPeriod>? _periods;
    private DateTimeOffset _triedAt;

    private string FilePath => Path.Combine(environment.DataDirectory, "festivals.json");

    /// <summary>
    /// The dates as kept, never fetched: for whatever must not wait on the wiki, such as the Here
    /// widget and a page's first drawing.
    /// </summary>
    public IReadOnlyList<FestivalPeriod> Held() => _periods ??= Read() ?? Festivals.Shipped;

    /// <summary>The dates, fetched first when the kept copy is a day old or there is none.</summary>
    public async Task<IReadOnlyList<FestivalPeriod>> GetAsync(CancellationToken ct = default)
    {
        var stored = File.Exists(FilePath) ? File.GetLastWriteTimeUtc(FilePath) : (DateTime?)null;

        if ((stored is null || DateTime.UtcNow - stored > Fresh) && DateTimeOffset.UtcNow - _triedAt > Retry)
        {
            await _gate.WaitAsync(ct);
            try
            {
                if (DateTimeOffset.UtcNow - _triedAt > Retry)
                {
                    _triedAt = DateTimeOffset.UtcNow;
                    await DownloadAsync(ct);
                }
            }
            finally
            {
                _gate.Release();
            }
        }

        return Held();
    }

    private IReadOnlyList<FestivalPeriod>? Read()
    {
        try
        {
            return File.Exists(FilePath)
                ? Festivals.Merge(Festivals.ParseWiki(File.ReadAllText(FilePath)), Festivals.Shipped)
                : null;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or IOException)
        {
            return null;
        }
    }

    private async Task DownloadAsync(CancellationToken ct)
    {
        try
        {
            using var leash = CancellationTokenSource.CreateLinkedTokenSource(ct);
            leash.CancelAfter(TimeSpan.FromSeconds(20));

            var json = await Http.GetStringAsync(Address, leash.Token);

            // Only a copy that lists runs replaces the one already kept.
            var periods = Festivals.Merge(Festivals.ParseWiki(json), Festivals.Shipped);

            await File.WriteAllTextAsync(FilePath, json, ct);
            _periods = periods;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException
                                       or KeyNotFoundException or InvalidOperationException or IOException)
        {
            if (ct.IsCancellationRequested) throw;
        }
    }

    /// <summary>The app's own name, as the meta timetable's download sends it.</summary>
    private static HttpClient Client()
    {
        var http = new HttpClient();

        http.DefaultRequestHeaders.UserAgent.ParseAdd("GuildWarsCompanion/1.0");

        return http;
    }
}
