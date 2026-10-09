using GuildWarsCompanion.Host;

namespace GuildWarsCompanion.Services.Alerts;

/// <summary>
/// Where the notification window is kept.
///
/// Plain preferences: it is four small values, it is read on a background poll where nothing
/// else is loaded, and losing it would only mean notifications arrive at any hour again.
/// </summary>
public sealed class NotifySchedule(ISettings settings)
{
    private const string EnabledKey = "notify_window_enabled";
    private const string FromKey = "notify_window_from";
    private const string ToKey = "notify_window_to";
    private const string WeekendKey = "notify_window_weekends";

    public NotifyWindow Current
    {
        get
        {
            var fallback = NotifyWindow.Always;

            return new NotifyWindow(
                settings.Get(EnabledKey, fallback.Enabled),
                Read(FromKey, fallback.From),
                Read(ToKey, fallback.To),
                settings.Get(WeekendKey, fallback.AllDayAtWeekends));
        }
        set
        {
            settings.Set(EnabledKey, value.Enabled);
            settings.Set(FromKey, value.From.ToString("HH:mm"));
            settings.Set(ToKey, value.To.ToString("HH:mm"));
            settings.Set(WeekendKey, value.AllDayAtWeekends);
        }
    }

    /// <summary>
    /// Times are stored as "HH:mm" rather than as ticks: a preferences file is worth being
    /// able to read, and a value that cannot be parsed falls back rather than throwing on a
    /// background thread where nothing would report it.
    /// </summary>
    private TimeOnly Read(string key, TimeOnly fallback) =>
        TimeOnly.TryParse(settings.Get(key, string.Empty), out var found)
            ? found
            : fallback;
}
