using System.Text.RegularExpressions;

using GuildWarsCompanion.Gw2;

namespace GuildWarsCompanion.Services;

/// <summary>One fractal's daily at one tier: "Daily Tier 4 Aetherblade".</summary>
public sealed record FractalDaily(int Id, string Fractal, int Tier);

/// <summary>One recommended fractal: "Daily Recommended Fractal—Scale 28".</summary>
public sealed record FractalScale(int Id, int Scale);

/// <summary>A day's fractal dailies, read out of their names.</summary>
public sealed record FractalDay(IReadOnlyList<FractalDaily> Tiers, IReadOnlyList<FractalScale> Recommended)
{
    /// <summary>The day's fractals, each once, in alphabetical order.</summary>
    public IReadOnlyList<string> Fractals { get; } =
        [.. Tiers.Select(daily => daily.Fractal).Distinct().Order(StringComparer.Ordinal)];

    public bool IsEmpty => Tiers.Count == 0 && Recommended.Count == 0;
}

/// <summary>One thing to do today, and whether it is done.</summary>
/// <param name="Note">What the page says under it: the tiers done, or why it does not count.</param>
/// <param name="Counts">False for a recommended scale above the account's fractal level, which
/// cannot be entered yet and so is not left undone.</param>
public sealed record FractalTask(string Name, bool Done, string Note, bool Counts = true)
{
    /// <summary>Brass for what is still to do; quiet for what is done or out of reach.</summary>
    public Color EdgeColor => Done || !Counts ? Theme.Banked : Theme.Brass;
}

/// <summary>
/// The daily fractals: which they are, and which of them this account has done.
///
/// The API has no fractal endpoint. What it has is the "Daily Fractals" achievement category,
/// fifteen achievements a day — three fractals at four tiers each, and three recommended scales —
/// whose names are the only place the fractal is written down. So the English names are read,
/// and an achievement whose name does not have one of the two shapes below is left out rather
/// than guessed at.
/// </summary>
public static partial class Fractals
{
    [GeneratedRegex(@"^Daily Tier (?<tier>[1-4]) (?<fractal>.+)$")]
    private static partial Regex TierName();

    [GeneratedRegex(@"^Daily Recommended Fractal\W+Scale (?<scale>\d+)$")]
    private static partial Regex ScaleName();

    /// <summary>The day, from the category's achievements as English names them.</summary>
    public static FractalDay Read(IEnumerable<Achievement> english)
    {
        var tiers = new List<FractalDaily>();
        var scales = new List<FractalScale>();

        foreach (var achievement in english)
        {
            var name = achievement.Name.Trim();

            if (TierName().Match(name) is { Success: true } tier)
                tiers.Add(new FractalDaily(
                    achievement.Id, tier.Groups["fractal"].Value.Trim(), int.Parse(tier.Groups["tier"].Value)));
            else if (ScaleName().Match(name) is { Success: true } scale)
                scales.Add(new FractalScale(achievement.Id, int.Parse(scale.Groups["scale"].Value)));
        }

        return new FractalDay(tiers, [.. scales.OrderBy(scale => scale.Scale)]);
    }

    /// <summary>
    /// The daily tier a fractal level reaches: a tier's daily asks for its fractal at scale 1, 26,
    /// 51 or 76 or higher. Zero for an account that has not started fractals.
    /// </summary>
    public static int TierFor(int level) => level switch
    {
        >= 76 => 4,
        >= 51 => 3,
        >= 26 => 2,
        >= 1 => 1,
        _ => 0
    };

    /// <summary>
    /// What is left today: each fractal at the account's own tier, and each recommended scale it
    /// can enter.
    ///
    /// The account's tier rather than all four: a player at level 80 runs tier 4, and "9 of 12"
    /// because tiers 1 to 3 went unclaimed would be counting chests nobody wants. A fractal whose
    /// daily stops short of that tier counts at its highest.
    /// </summary>
    public static IReadOnlyList<FractalTask> Tasks(FractalDay day, int level, IReadOnlySet<int> done)
    {
        var tier = TierFor(level);
        var tasks = new List<FractalTask>();

        if (tier == 0) return tasks;

        foreach (var fractal in day.Fractals)
        {
            var dailies = day.Tiers.Where(daily => daily.Fractal == fractal).OrderBy(daily => daily.Tier).ToList();
            var aim = dailies.LastOrDefault(daily => daily.Tier <= tier) ?? dailies[0];
            var tiersDone = dailies.Where(daily => done.Contains(daily.Id)).Select(daily => daily.Tier).ToList();

            var note = tiersDone.Count == 0
                ? L.F("Tier {0} to do", aim.Tier)
                : L.F("Tiers done: {0}", string.Join(", ", tiersDone));

            tasks.Add(new FractalTask(fractal, done.Contains(aim.Id), note));
        }

        foreach (var scale in day.Recommended)
        {
            var name = L.F("Recommended scale {0}", scale.Scale);

            tasks.Add(scale.Scale > level
                ? new FractalTask(name, false, L.T("Above your fractal level"), Counts: false)
                : new FractalTask(name, done.Contains(scale.Id),
                    done.Contains(scale.Id) ? L.T("Done") : L.T("To do")));
        }

        return tasks;
    }

    /// <summary>"Aetherblade · Thaumanova Reactor · Uncategorized · scales 6, 28, 61": a day in one line.</summary>
    public static string Describe(FractalDay day)
    {
        var parts = new List<string>(day.Fractals);

        if (day.Recommended.Count > 0)
            parts.Add(L.F("scales {0}", string.Join(", ", day.Recommended.Select(scale => scale.Scale))));

        return string.Join(" · ", parts);
    }

    /// <summary>The fractal currencies, in the order the wallet page would list them: Fractal
    /// Relic, Pristine Fractal Relic, Unstable Fractal Essence.</summary>
    public static readonly IReadOnlyList<int> Currencies = [7, 24, 59];
}
