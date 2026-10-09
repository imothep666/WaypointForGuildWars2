namespace GuildWarsCompanion.Services;

/// <summary>
/// Turning essence of luck consumed into base magic find.
///
/// There is no formula: the steps rise irregularly — 100 at first, 30,000 near the cap — and
/// the API carries none of it. The app refused to show a percentage for a long time because
/// interpolating between a few published anchors produces a number that looks authoritative
/// and is wrong.
///
/// This is the real table instead, all three hundred rows of it, taken from the Guild Wars 2
/// Wiki's Luck article and checked on the way in: three hundred levels with no gaps, strictly
/// increasing totals, each step equal to the difference between neighbouring totals, and a
/// final total of 4,295,450 — which the app already knew independently as the cost of the cap.
/// </summary>
public static class LuckTable
{
    /// <summary>Base magic find stops here, however much more is eaten.</summary>
    public const int Cap = 300;

    /// <summary>
    /// Luck consumed to reach each percent: <c>Thresholds[0]</c> is the 100 that buys the
    /// first, <c>Thresholds[299]</c> the 4,295,450 that buys the three hundredth.
    /// </summary>
    private static readonly long[] Thresholds =
    [
        100, 200, 300, 400, 500, 600, 700, 800, 910, 1020, 1130, 1240, 1360, 1480, 1600, 1730,
        1860, 2000, 2150, 2300, 2460, 2630, 2810, 3000, 3200, 3410, 3630, 3860, 4110, 4370,
        4640, 4930, 5240, 5560, 5900, 6260, 6640, 7040, 7460, 7900, 8370, 8860, 9380, 9920,
        10490, 11090, 11720, 12380, 13070, 13790, 14550, 15340, 16170, 17030, 17930, 18870,
        19850, 20870, 21940, 23050, 24200, 25400, 26650, 27950, 29300, 30700, 32150, 33660,
        35220, 36840, 38510, 40240, 42030, 43890, 45810, 47790, 49840, 51960, 54150, 56410,
        58740, 61140, 63620, 66170, 68800, 71510, 74300, 77170, 80130, 83170, 86300, 89520,
        92830, 96230, 99720, 103300, 106980, 110760, 114640, 118620, 122700, 126890, 131180,
        135580, 140090, 144710, 149440, 154290, 159250, 164330, 169530, 174850, 180300, 185870,
        191570, 197400, 203360, 209450, 215670, 222030, 228530, 235160, 241940, 248860, 255920,
        263130, 270490, 278000, 285660, 293480, 301460, 309590, 317880, 326340, 334960, 343750,
        352710, 361840, 371140, 380610, 390260, 400090, 410100, 420290, 430660, 441220, 451970,
        462910, 474040, 485370, 496890, 508610, 520530, 532660, 544990, 557530, 570280, 583240,
        596420, 609810, 623420, 637250, 651310, 665590, 680100, 694840, 709810, 725010, 740450,
        756130, 772050, 788210, 804620, 821270, 838170, 855330, 872740, 890410, 908340, 926530,
        944980, 963700, 982690, 1001950, 1021480, 1041290, 1061380, 1081750, 1102400, 1123340,
        1144560, 1166070, 1187880, 1209980, 1232380, 1255080, 1278080, 1301390, 1325000,
        1348920, 1373160, 1397710, 1422580, 1447770, 1473280, 1499120, 1525280, 1551780,
        1578610, 1605770, 1633270, 1661110, 1689290, 1717820, 1746700, 1775930, 1805510,
        1835450, 1865450, 1895450, 1925450, 1955450, 1985450, 2015450, 2045450, 2075450,
        2105450, 2135450, 2165450, 2195450, 2225450, 2255450, 2285450, 2315450, 2345450,
        2375450, 2405450, 2435450, 2465450, 2495450, 2525450, 2555450, 2585450, 2615450,
        2645450, 2675450, 2705450, 2735450, 2765450, 2795450, 2825450, 2855450, 2885450,
        2915450, 2945450, 2975450, 3005450, 3035450, 3065450, 3095450, 3125450, 3155450,
        3185450, 3215450, 3245450, 3275450, 3305450, 3335450, 3365450, 3395450, 3425450,
        3455450, 3485450, 3515450, 3545450, 3575450, 3605450, 3635450, 3665450, 3695450,
        3725450, 3755450, 3785450, 3815450, 3845450, 3875450, 3905450, 3935450, 3965450,
        3995450, 4025450, 4055450, 4085450, 4115450, 4145450, 4175450, 4205450, 4235450,
        4265450, 4295450
    ];

    /// <summary>
    /// Base magic find for this much luck: how many thresholds have been passed.
    ///
    /// Base only — achievement points, food, boosters and guild bonuses all add on top, and
    /// none of them is in this number.
    /// </summary>
    public static int MagicFind(long consumed)
    {
        // Nothing consumed, or a nonsense negative, lands below the first threshold and comes
        // back as zero on its own — no guard needed, and one would only be untestable.
        var found = Array.BinarySearch(Thresholds, consumed);

        // An exact match is the level it buys; otherwise the count of thresholds below it.
        return Math.Min(found >= 0 ? found + 1 : ~found, Cap);
    }

    /// <summary>
    /// Total luck for the next percent, or null at the cap where there is no next.
    /// </summary>
    public static long? NextAt(long consumed)
    {
        var level = MagicFind(consumed);

        return level >= Cap ? null : Thresholds[level];
    }

    /// <summary>How much more to eat for the next percent.</summary>
    public static long? StillNeeded(long consumed) =>
        NextAt(consumed) is { } next ? Math.Max(0, next - Math.Max(0, consumed)) : null;

    /// <summary>
    /// "34% base magic find", which is what the number was always for.
    /// </summary>
    public static string Describe(long consumed) => $"{MagicFind(consumed)}%";
}
