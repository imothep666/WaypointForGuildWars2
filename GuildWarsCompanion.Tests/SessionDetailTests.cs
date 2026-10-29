using GuildWarsCompanion.Data;
using GuildWarsCompanion.Services;
using Microsoft.Data.Sqlite;

using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// The recap beyond the wallet: every session in the ledger, which details they are measured
/// against, which details are kept, and what two details differ by.
/// </summary>
public class SessionDetailTests : IDisposable
{
    private static readonly DateTimeOffset Evening = new(2026, 10, 5, 18, 0, 0, TimeSpan.Zero);

    private readonly string _database = Path.Combine(Path.GetTempPath(), $"gw2-ledger-{Guid.NewGuid():N}.db3");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_database);
    }

    private static LedgerPoint At(int minutes, long age, long coin = 10_000, bool detail = false) =>
        new(Evening.AddMinutes(minutes),
            new Dictionary<string, (long, int)> { ["Sweet Child O Night"] = (age, 80) },
            new Dictionary<int, long> { [1] = coin })
        {
            HasDetail = detail
        };

    private static LedgerDetail Holding(Dictionary<int, int> items) => new(items, null, null, null, null, null);

    [Fact]
    public void Every_session_is_found_oldest_first_and_the_latest_agrees()
    {
        LedgerPoint[] points =
        [
            At(0, 100), At(30, 1900), At(60, 1900), // 30 minutes
            At(90, 1900), At(120, 4600), At(150, 6400) // 75 minutes, still going
        ];

        var all = SessionRecap.All(points);

        Assert.Equal([1800L, 4500L], all.Select(session => session.PlayedSeconds));
        Assert.False(all[0].IsOngoing);
        Assert.True(all[1].IsOngoing);
        Assert.Equal(SessionRecap.Latest(points)!.Before.At, all[1].Before.At);
    }

    [Fact]
    public void A_detail_stands_for_later_points_only_while_nothing_moved()
    {
        // The detail is taken idle at 0; at 30 the account has not moved, so it still holds there.
        LedgerPoint[] points = [At(0, 100, detail: true), At(30, 100), At(60, 1900), At(90, 1900, coin: 20_000)];

        Assert.Equal(points[0], SessionRecap.DetailFor(points, points[1]));

        // At 90 the coin moved and no detail was taken there: nothing stands for it.
        Assert.Null(SessionRecap.DetailFor(points, points[3]));
    }

    [Fact]
    public void Only_the_details_a_session_is_measured_by_are_kept()
    {
        LedgerPoint[] points =
        [
            At(0, 100, detail: true), At(30, 100), // idle: the detail at 0 is the session's start
            At(60, 1900, coin: 12_000, detail: true), // mid-session: not needed once it ends
            At(90, 3700, coin: 15_000, detail: true), At(120, 3700, coin: 16_000, detail: true) // the end
        ];

        var keep = SessionRecap.DetailsToKeep(points);

        Assert.Equal([Evening, Evening.AddMinutes(120)], keep.Order());
    }

    [Fact]
    public void What_a_session_changed_is_the_difference_of_its_two_details()
    {
        var before = new LedgerDetail(
            new Dictionary<int, int> { [19721] = 10, [24277] = 5 },
            new Dictionary<int, AchievementMark> { [1] = new(true, 0), [2] = new(false, 3) },
            [new VaultMark("daily", 7, "Kill 10 enemies", false), new VaultMark("daily", 8, "Gather 5", true)],
            new Dictionary<string, int> { ["Central Tyria"] = 100 },
            new Dictionary<int, int> { [4] = 2 },
            new Dictionary<string, IReadOnlyDictionary<string, int>>
            {
                ["Sweet Child O Night"] = new Dictionary<string, int> { ["Chef"] = 400 }
            });

        var after = new LedgerDetail(
            new Dictionary<int, int> { [19721] = 14, [24277] = 2, [12345] = 1 },
            new Dictionary<int, AchievementMark> { [1] = new(true, 0), [2] = new(false, 4), [3] = new(true, 0) },
            [new VaultMark("daily", 7, "Kill 10 enemies", true), new VaultMark("daily", 8, "Gather 5", true)],
            new Dictionary<string, int> { ["Central Tyria"] = 102, ["Janthir Wilds"] = 1 },
            new Dictionary<int, int> { [4] = 3, [9] = 1 },
            new Dictionary<string, IReadOnlyDictionary<string, int>>
            {
                ["Sweet Child O Night"] = new Dictionary<string, int> { ["Chef"] = 410 },
                // Made during the session: from nothing.
                ["Imothep The First"] = new Dictionary<string, int> { ["Tailor"] = 25 }
            });

        var changes = SessionChanges.Between(before, after);

        Assert.Equal(new Dictionary<int, int> { [19721] = 4, [24277] = -3, [12345] = 1 }, changes.Items);
        Assert.Equal([2, 3], changes.Achievements);
        Assert.Equal("Kill 10 enemies", Assert.Single(changes.Vault!).Title);
        Assert.Equal(new Dictionary<string, int> { ["Central Tyria"] = 2, ["Janthir Wilds"] = 1 }, changes.MasteryPoints);
        Assert.Equal([new MasteryRise(4, 2, 3), new MasteryRise(9, 0, 1)], changes.Masteries);
        Assert.Equal(
            [new CraftingRise("Sweet Child O Night", "Chef", 400, 410), new CraftingRise("Imothep The First", "Tailor", 0, 25)],
            changes.Crafting);

        // Gains less losses, at what buyers offer; an item with no price counts for nothing.
        Assert.Equal(4 * 2500 - 3 * 100,
            SessionChanges.Worth(changes.Items!, new Dictionary<int, long> { [19721] = 2500, [24277] = 100 }));
    }

    [Fact]
    public void A_part_either_end_could_not_read_is_unknown_not_unchanged()
    {
        var changes = SessionChanges.Between(Holding([]), new LedgerDetail(null, null, null, null, null, null));

        Assert.Null(changes.Items);
        Assert.Null(changes.Achievements);
    }

    [Fact]
    public void A_rate_needs_a_quarter_hour_of_play()
    {
        Assert.Null(SessionRecap.PerHour(50_000, 600));
        Assert.Equal(100_000, SessionRecap.PerHour(50_000, 1800));
        Assert.Equal("+5g 00s 00c in all · +10g 00s 00c an hour", SessionRecap.Worth(50_000, 1800));
    }

    [Fact]
    public async Task The_store_keeps_a_detail_with_its_point_and_drops_the_rest_on_request()
    {
        var store = new LedgerStore(_database);

        await store.SaveAsync(At(0, 100), Holding(new() { [19721] = 10 }));
        await store.SaveAsync(At(30, 1900), Holding(new() { [19721] = 12 }));
        await store.SaveAsync(At(60, 1900));

        var points = await store.LoadAsync();

        Assert.Equal([true, true, false], points.Select(point => point.HasDetail));
        Assert.Equal(12, (await store.DetailAsync(Evening.AddMinutes(30)))!.Items![19721]);

        // Every part comes back as it went in, the nested crafting included.
        await store.SaveAsync(At(90, 1900, coin: 20_000), new LedgerDetail(
            new Dictionary<int, int> { [1] = 2 },
            new Dictionary<int, AchievementMark> { [5] = new(true, 1) },
            [new VaultMark("weekly", 3, "Ein Ziel", true)],
            new Dictionary<string, int> { ["Central Tyria"] = 9 },
            new Dictionary<int, int> { [4] = 3 },
            new Dictionary<string, IReadOnlyDictionary<string, int>>
            {
                ["Sweet Child O Night"] = new Dictionary<string, int> { ["Chef"] = 400 }
            }));

        var full = (await store.DetailAsync(Evening.AddMinutes(90)))!;
        Assert.Equal(new AchievementMark(true, 1), full.Achievements![5]);
        Assert.Equal("Ein Ziel", Assert.Single(full.Vault!).Title);
        Assert.Equal(9, full.MasteryPoints!["Central Tyria"]);
        Assert.Equal(400, full.Crafting!["Sweet Child O Night"]["Chef"]);

        await store.KeepDetailsAsync([Evening.AddMinutes(30)]);

        Assert.Null(await store.DetailAsync(Evening));
        Assert.NotNull(await store.DetailAsync(Evening.AddMinutes(30)));
    }
}
