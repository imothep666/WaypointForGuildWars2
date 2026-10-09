namespace GuildWarsCompanion.Services;

/// <summary>One account-wide number, already phrased.</summary>
public sealed record StandingRow(string Name, string Value, string? Note)
{
    public bool HasNote => !string.IsNullOrWhiteSpace(Note);
}

/// <summary>
/// The account-wide numbers that apply to every character: luck, fractal level, WvW rank.
///
/// None of these appears anywhere else in the app, and two of them were already arriving in
/// the /v2/account response and being thrown away.
/// </summary>
public static class AccountStanding
{
    /// <summary>
    /// Luck consumed to reach the 300% magic find cap.
    ///
    /// From the wiki, not the API. The per-percent cost climbs steeply — 100 luck for the
    /// first, 760 for the fiftieth, 30,000 for the three-hundredth — so this is the total of a
    /// 300-row table rather than anything derivable.
    /// </summary>
    public const long LuckForCap = 4_295_450;

    /// <summary>
    /// What the luck actually bought.
    ///
    /// This used to show the raw number of essences and refuse to convert it, because the
    /// conversion has no formula and interpolating between published anchors produces a
    /// figure that looks authoritative and is wrong. It is a real table now — see
    /// <see cref="LuckTable"/> — so the percentage leads and the essences explain it.
    ///
    /// Base magic find only: achievement points, food, boosters and guild bonuses stack on
    /// top of this and none of them is counted here.
    /// </summary>
    public static StandingRow Luck(long consumed)
    {
        var note = LuckTable.StillNeeded(consumed) is { } needed
            ? $"{consumed:N0} luck consumed · {needed:N0} more for " +
              $"{LuckTable.MagicFind(consumed) + 1}%. Base magic find only."
            : $"{consumed:N0} luck consumed — the 300% cap, which costs {LuckForCap:N0}.";

        return new StandingRow("Magic find from luck", LuckTable.Describe(consumed), note);
    }

    /// <summary>Fractal personal level. Zero means fractals have not been started.</summary>
    public static StandingRow? Fractals(int level) =>
        level <= 0
            ? null
            : new StandingRow("Fractal level", $"{level}",
                level >= 100 ? "At the maximum." : null);

    /// <summary>WvW rank, which is account-wide and carries its own ability line.</summary>
    public static StandingRow? Wvw(int rank) =>
        rank <= 0 ? null : new StandingRow("WvW rank", $"{rank:N0}", null);

    /// <summary>Mastery points already earned, shown as a total across every region.</summary>
    /// <summary>
    /// Mastery points, as the number that decides something.
    ///
    /// Earned is a lifetime tally; what matters is what is left to spend, and the two are
    /// different the moment a track is trained. Showing 9 to an account with 6 spendable
    /// overstates what it can do today.
    /// </summary>
    public static StandingRow? Mastery(int? earned, int? spent = null)
    {
        if (earned is null or <= 0) return null;

        var available = Math.Max(0, earned.Value - (spent ?? 0));

        return new StandingRow(
            "Mastery points",
            $"{available:N0}",
            spent is > 0 ? $"{earned:N0} earned, {spent:N0} spent." : null);
    }
}
