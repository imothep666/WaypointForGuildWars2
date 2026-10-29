using GuildWarsCompanion.Gw2;

namespace GuildWarsCompanion.Services;

/// <summary>
/// A section on Today for each festival that is on: when its next event is, and what is left of
/// the achievements it brings back.
///
/// Every read goes through <see cref="OfflineReader"/>, so the page drawn from what was stored
/// last time has the festival in it too.
/// </summary>
public sealed class FestivalService(Gw2Client client, OfflineReader offline, MetaSource metas)
{
    public async Task<IReadOnlyList<TodaySection>> SectionsAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var sections = new List<TodaySection>();

        foreach (var period in Festivals.On(Festivals.Announced, now))
        {
            var rows = new List<TodayRow>();

            if (await EventAsync(period.Festival, now, ct) is { } next) rows.Add(next);

            rows.AddRange(await AchievementsAsync(period.Festival, ct));

            sections.Add(new TodaySection(period.Festival.Name, period.Until, rows, $"festival:{period.Festival.Key}"));
        }

        return sections;
    }

    /// <summary>
    /// The festival's next event from the wiki's timer data, at a clock time rather than a
    /// countdown: the page is not redrawn every minute, and a time does not go stale until it passes.
    /// </summary>
    private async Task<TodayRow?> EventAsync(Festival festival, DateTimeOffset now, CancellationToken ct)
    {
        if (festival.TimerKey is not { } key) return null;

        MetaMap? map;
        try
        {
            map = (await metas.FestivalsAsync(ct)).FirstOrDefault(entry => entry.Key == key);
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException)
        {
            return null;
        }

        if (map is null) return null;

        if (MetaSchedule.Current(map, now) is { } on)
            return new TodayRow(on.Name, 0, 0, L.T("on now"), Route: "//events",
                Figure: L.F("until {0:HH:mm}", on.End.ToLocalTime()));

        return MetaSchedule.Next(map, now) is { } next
            ? new TodayRow(next.Name, 0, 0, L.T("next festival event"), Route: "//events",
                Figure: next.Start.ToLocalTime().ToString("HH:mm"))
            : null;
    }

    /// <summary>One row per category, with what is left named; unread rows when it cannot be read.</summary>
    private async Task<IReadOnlyList<TodayRow>> AchievementsAsync(Festival festival, CancellationToken ct)
    {
        HashSet<int> done;
        try
        {
            done = (await offline.ReadAsync(
                    "account/achievements", Gw2JsonContext.Default.ListAccountAchievement,
                    client.GetAccountAchievementsAsync, ct)).Value
                .Where(achievement => achievement.Done)
                .Select(achievement => achievement.Id)
                .ToHashSet();
        }
        catch (Gw2ApiException)
        {
            return [new TodayRow(L.T("Festival achievements"), 0, 0, null, Unread: true)];
        }

        var rows = new List<TodayRow>();

        foreach (var id in festival.Categories)
        {
            try
            {
                var category = (await offline.ReadAsync(
                    $"achievements/categories/{id}", Gw2JsonContext.Default.DailyCategory,
                    token => client.GetAchievementCategoryAsync(id, token), ct)).Value;

                var ids = category.Achievements.Select(entry => entry.Id).ToList();
                var open = ids.Where(achievement => !done.Contains(achievement)).ToList();

                var names = open.Count == 0
                    ? []
                    : (await offline.ReadAsync(
                        $"achievements/festival/{id}", Gw2JsonContext.Default.ListAchievement,
                        token => client.GetAchievementsAsync(open, token), ct)).Value;

                var named = names.ToDictionary(achievement => achievement.Id, achievement => achievement.Name);

                rows.Add(new TodayRow(
                    category.Name ?? L.F("Category {0}", id),
                    ids.Count - open.Count, ids.Count,
                    TodayService.Join(open.Select(achievement => named.GetValueOrDefault(achievement)).OfType<string>().ToList()),
                    Route: "//achievements"));
            }
            catch (Gw2ApiException)
            {
                rows.Add(new TodayRow(L.F("Category {0}", id), 0, 0, null, Route: "//achievements", Unread: true));
            }
        }

        return rows;
    }
}
