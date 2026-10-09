namespace GuildWarsCompanion.Services;

/// <summary>Points earned, spent and left in one region.</summary>
public sealed record RegionPoints(string Region, int Earned, int Spent)
{
    public int Available => Math.Max(0, Earned - Spent);

    /// <summary>"6 to spend · 9 earned" — the spendable number leads, since that is the
    /// one that decides anything.</summary>
    public string Line => $"{Available:N0} to spend · {Earned:N0} earned";
}

/// <summary>One track, and what the next step of it would take.</summary>
public sealed record MasteryStep(
    string Name, string Region, int Done, int Total, string? NextLevel,
    int NextCost, bool Affordable)
{
    public bool IsComplete => Done >= Total && Total > 0;

    public string ProgressText => $"{Done} of {Total}";
}

/// <summary>
/// Reading mastery progress.
///
/// Two things make a plausible wrong answer easy here. The account reports a track's level as a
/// <em>0-indexed</em> position in its level list, so treating it as a count is off by one on
/// every track that has been touched at all. And points are earned and spent per region —
/// Central Tyria points cannot train a Maguuma track — so a single account-wide total says
/// nothing about what can actually be spent, which is the only question worth asking.
/// </summary>
public static class Masteries
{
    /// <summary>
    /// The two endpoints name the same region differently, and nothing says so.
    ///
    /// A track calls its region by the map — "Desert", "Jade", "Tyria" — while the points
    /// totals call it by the expansion that sells it: "Path of Fire", "End of Dragons",
    /// "Central Tyria". Matching the two strings directly fails on every region, which reads
    /// as an account that cannot afford anything anywhere. Found exactly that way: six Central
    /// Tyria points and not one track marked affordable.
    ///
    /// Paired by what the tracks are: Tyria holds Pact Commander and Fractal Attunement,
    /// Desert holds the mounts, Jade holds fishing and skiffs, and so on.
    /// </summary>
    private static readonly Dictionary<string, string> RegionNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Tyria"] = "Central Tyria",
            ["Maguuma"] = "Heart of Thorns",
            ["Desert"] = "Path of Fire",
            ["Tundra"] = "Icebrood Saga",
            ["Jade"] = "End of Dragons",
            ["Sky"] = "Secrets of the Obscure",
            ["Wild"] = "Janthir Wilds",
            ["Magic"] = "Visions of Eternity"
        };

    /// <summary>
    /// A track's region, named the way the points are named — which is the name a player sees
    /// in the game and on the line above.
    ///
    /// A region this build has not heard of keeps its own name rather than being dropped: if
    /// the two endpoints ever agree on a new one, that alone makes it work.
    /// </summary>
    public static string PointsRegion(string? trackRegion)
    {
        var region = trackRegion ?? string.Empty;

        return RegionNames.GetValueOrDefault(region, region.Length > 0 ? region : "Unknown");
    }

    /// <summary>
    /// How many levels of a track are done. The API's level is a position, not a count: a
    /// track sitting at level 4 has five of them finished.
    /// </summary>
    public static int LevelsDone(int? reported) => reported is { } level ? level + 1 : 0;

    /// <summary>
    /// Every track the account could work on, with what the next level would cost and whether
    /// the region's points cover it.
    ///
    /// Ordered by what can be acted on now: affordable next steps first, then the rest of the
    /// unfinished ones, then what is already done — a finished track is a record, not a plan.
    /// </summary>
    public static IReadOnlyList<MasteryStep> Plan(
        IEnumerable<(int Id, string Name, string? Region, IReadOnlyList<(string Name, int Cost)> Levels)> tracks,
        IReadOnlyDictionary<int, int> trained,
        IReadOnlyDictionary<string, int> availableByRegion)
    {
        var steps = new List<MasteryStep>();

        foreach (var track in tracks)
        {
            var levels = track.Levels ?? [];
            if (levels.Count == 0) continue;

            // Named as the points are named, since that is the pool it draws on.
            var region = PointsRegion(track.Region);
            var done = trained.TryGetValue(track.Id, out var level)
                ? Math.Min(LevelsDone(level), levels.Count)
                : 0;

            var next = done < levels.Count ? levels[done] : default;
            var available = availableByRegion.GetValueOrDefault(region);

            steps.Add(new MasteryStep(
                track.Name,
                region,
                done,
                levels.Count,
                done < levels.Count ? next.Name : null,
                done < levels.Count ? next.Cost : 0,
                done < levels.Count && available >= next.Cost));
        }

        return steps
            .OrderByDescending(step => !step.IsComplete && step.Affordable)
            .ThenBy(step => step.IsComplete)
            .ThenBy(step => step.Region, StringComparer.CurrentCulture)
            .ThenBy(step => step.Name, StringComparer.CurrentCulture)
            .ToList();
    }

    /// <summary>
    /// What the next level would take, or that there is nothing left to take.
    /// </summary>
    public static string Describe(MasteryStep step)
    {
        if (step.IsComplete) return "Trained";

        var cost = $"{step.NextCost} point{(step.NextCost == 1 ? string.Empty : "s")}";

        return step.Affordable
            ? $"{step.NextLevel} · {cost}"
            : $"{step.NextLevel} · {cost} — not enough in {step.Region}";
    }

    /// <summary>
    /// The heading. Points sitting unspent is the thing this screen exists to catch, so it is
    /// said as a number rather than left to be counted off the list.
    /// </summary>
    public static string Summarise(IEnumerable<RegionPoints> regions)
    {
        var spendable = regions.Sum(region => region.Available);

        return spendable == 0
            ? "No mastery points waiting to be spent."
            : $"{spendable:N0} mastery point{(spendable == 1 ? string.Empty : "s")} waiting to be spent.";
    }
}
