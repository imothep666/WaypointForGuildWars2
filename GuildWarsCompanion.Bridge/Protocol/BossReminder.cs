namespace GuildWarsCompanion.Bridge.Protocol;

/// <summary>A world boss about to appear, as the PC would mention it.</summary>
public sealed record BossDue(string Name, string Location, DateTimeOffset At, int MinutesAway)
{
    /// <summary>
    /// One mention per appearance, not per boss: the same boss tomorrow is news again, and the
    /// same boss on this tick and the next is not.
    /// </summary>
    public string Key => $"{Name}:{At.UtcDateTime:yyyyMMddHHmm}";

    public string Title => MinutesAway switch
    {
        <= 0 => $"{Name} is up now",
        1 => $"{Name} in a minute",
        _ => $"{Name} in {MinutesAway} minutes"
    };

    public string Where => Location;
}

/// <summary>
/// Which world boss is worth putting on the PC screen, and when.
///
/// <b>Why the PC says this at all, when the phone already does.</b> The phone's reminder is for
/// someone who is not playing — on a train, at a desk — and it is the right thing there. During a
/// session it is the wrong screen entirely: the phone is propped up showing the compass, and the
/// player is looking at the monitor. This is the same fact said where the eyes are.
///
/// <b>It deliberately says less than the phone's.</b> <see cref="Services.BossAlerts"/> only
/// mentions bosses the account has not killed today, because the app has an API key and can know.
/// The bridge has no key and no account — by design, since it is a thing that reads shared memory
/// and answers a socket — so it cannot filter by what is done. It says what the timetable says,
/// and does not pretend to know more.
/// </summary>
public static class BossReminder
{
    /// <summary>
    /// How far ahead to mention one.
    ///
    /// Ten minutes rather than the phone's thirty-five. The phone's window is sized around a
    /// background poll that Android runs when it feels like it, so it has to be generous or an
    /// appearance falls between two polls and is never mentioned. The bridge is a desktop process
    /// checking every twenty seconds, so it can afford to be exact — and ten minutes is the
    /// useful number in game: long enough to finish what you are doing and get there, short
    /// enough that it is still true when you arrive.
    /// </summary>
    public static readonly TimeSpan Lead = TimeSpan.FromMinutes(10);

    /// <summary>
    /// The soonest boss inside the window that has not already been mentioned.
    ///
    /// One at a time on purpose. The phone allows two because a notification shade holds them
    /// side by side; a panel on top of the game does not, and two stacked over a fight would be
    /// worse than missing one.
    /// </summary>
    public static BossDue? Next(
        IEnumerable<(string Name, string ApiId, string Location, int Level, DateTimeOffset Next)> appearances,
        IReadOnlySet<string> alreadySaid,
        DateTimeOffset utcNow)
    {
        BossDue? soonest = null;

        foreach (var boss in appearances)
        {
            var away = boss.Next - utcNow;

            if (away <= TimeSpan.Zero || away > Lead) continue;

            var due = new BossDue(
                boss.Name, boss.Location, boss.Next,
                // Rounded, so something 89 seconds out is "in 1 minute" rather than staying "in
                // 1 minute" only once it has been waiting for two.
                (int)Math.Round(away.TotalMinutes, MidpointRounding.AwayFromZero));

            if (alreadySaid.Contains(due.Key)) continue;

            if (soonest is null || boss.Next < soonest.At) soonest = due;
        }

        return soonest;
    }
}
