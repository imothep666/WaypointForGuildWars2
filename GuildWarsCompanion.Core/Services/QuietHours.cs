namespace GuildWarsCompanion.Services;

/// <summary>
/// The hours notifications are allowed to arrive.
///
/// Stated as when you <em>can</em> play rather than as two blocks of quiet. Someone who is at
/// work all day and asleep at night has one window in between, and describing it that way takes
/// one setting instead of two — and it stays right when the two blocks would meet in the middle.
/// </summary>
public sealed record NotifyWindow(
    bool Enabled, TimeOnly From, TimeOnly To, bool AllDayAtWeekends)
{
    /// <summary>Off until asked for: an app that silently withholds notifications is worse
    /// than one that sends them at a bad hour, because the second is at least visible.</summary>
    public static readonly NotifyWindow Always =
        new(false, new TimeOnly(17, 0), new TimeOnly(22, 30), true);
}

/// <summary>
/// When the app may put something on the notification shade.
///
/// The two traps are opposites. A window that crosses midnight — 20:00 to 02:00 — is the normal
/// case for an evening player and reads backwards to a naive comparison, which would allow
/// exactly the hours it should silence. And a window of zero length, which is easy to set by
/// accident with two pickers, must not mean "never": silence with no way to tell it apart from
/// a broken app is the worst outcome this feature can produce.
/// </summary>
public static class QuietHours
{
    /// <summary>
    /// Whether a notification may be shown at this local time.
    ///
    /// Local, deliberately: the whole point is the player's evening, not UTC's.
    /// </summary>
    public static bool Allows(NotifyWindow window, DateTimeOffset local)
    {
        if (!window.Enabled) return true;

        if (window.AllDayAtWeekends &&
            local.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            return true;

        // Two identical pickers mean "no restriction", never "no notifications ever".
        if (window.From == window.To) return true;

        var now = TimeOnly.FromDateTime(local.DateTime);

        return window.From < window.To
            ? now >= window.From && now < window.To
            // Crosses midnight: late evening and the small hours are the same window.
            : now >= window.From || now < window.To;
    }

    /// <summary>What the setting is currently doing, in a line the screen can show.</summary>
    public static string Describe(NotifyWindow window)
    {
        if (!window.Enabled) return "Notifications arrive at any hour.";

        if (window.From == window.To) return "Notifications arrive at any hour.";

        var line = $"Only between {window.From:HH:mm} and {window.To:HH:mm}";

        return window.AllDayAtWeekends ? $"{line}, and all day at weekends." : $"{line}.";
    }
}
