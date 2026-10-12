using CommunityToolkit.Mvvm.ComponentModel;
using GuildWarsCompanion.Data;
using GuildWarsCompanion.Gw2;

namespace GuildWarsCompanion.Services;

/// <summary>
/// One boss on the timetable. Observable because the countdown ticks while the screen is
/// open, and rebuilding the whole list every fifteen seconds would make it flicker.
/// </summary>
public sealed partial class EventRow : ObservableObject
{
    /// <summary>Close enough that it is worth travelling now.</summary>
    private static readonly TimeSpan Imminent = TimeSpan.FromMinutes(15);

    [ObservableProperty] private string _countdown = string.Empty;
    [ObservableProperty] private Color _edgeColor = Colors.Transparent;

    public EventRow(
        string name, string location, int level, DateTimeOffset next, bool doneToday, int? characterLevel)
    {
        Name = name;
        Location = location;
        Level = level;
        Next = next;
        DoneToday = doneToday;
        OutOfReach = characterLevel is not null && characterLevel < level;
        LocalTime = next.ToLocalTime().ToString("HH:mm");
        Tick(DateTimeOffset.UtcNow);
    }

    public string Name { get; }
    public string Location { get; }
    public int Level { get; }
    public DateTimeOffset Next { get; }
    public bool DoneToday { get; }

    /// <summary>The active character is below the level this boss's zone expects.</summary>
    public bool OutOfReach { get; }

    /// <summary>Spawn time in the player's own timezone — nobody thinks in UTC.</summary>
    public string LocalTime { get; }

    public string LevelNote => OutOfReach ? L.F("needs level {0}", Level) : L.F("level {0}", Level);

    /// <summary>
    /// A dim spine alone cannot explain itself, so the second line says it in words: the
    /// colour is the glance, this is the answer.
    /// </summary>
    public string Subtitle => DoneToday ? L.F("{0} · done today", Location) : Location;

    public void Tick(DateTimeOffset utcNow)
    {
        var remaining = Next - utcNow;

        Countdown = remaining <= TimeSpan.Zero
            ? L.T("now")
            : remaining < TimeSpan.FromMinutes(1)
                ? L.T("< 1 min")
                : remaining < TimeSpan.FromHours(1)
                    ? L.F("{0} min", (int)remaining.TotalMinutes)
                    : $"{(int)remaining.TotalHours}h {remaining.Minutes:00}m";

        // Brass says "go now" — so it must not appear on a boss the current character cannot
        // survive, however soon it starts.
        EdgeColor = remaining <= Imminent && !OutOfReach
            ? Theme.Brass
            : DoneToday || OutOfReach
                ? Theme.Banked
                : Colors.Transparent;
    }
}

/// <summary>The timetable, and who it is being read for.</summary>
public sealed record EventsView(IReadOnlyList<EventRow> Bosses, ActiveCharacter? For);

/// <summary>
/// The world boss timetable, crossed with what this account has killed today and with the
/// character it has been playing.
///
/// The schedule is local (see <see cref="EventSchedule"/>); only the "done today" half needs
/// the API, so the screen still works without a key.
/// </summary>
public sealed class EventService(Gw2Client client, ApiKeySession session, ActivityStore activity)
{
    public async Task<EventsView> GetUpcomingAsync(CancellationToken ct = default)
    {
        var done = await ReadCompletedAsync(ct);
        var active = await activity.LoadActiveAsync(ct);
        var now = DateTimeOffset.UtcNow;

        var rows = EventSchedule.NextAppearances(now)
            .Select(boss => new EventRow(
                boss.Name, boss.Location, boss.Level, boss.Next,
                done.Contains(boss.ApiId), active?.Level))
            .ToList();

        return new EventsView(rows, active);
    }

    /// <summary>
    /// Bosses already killed since the 00:00 UTC reset. Requires "progression"; without it
    /// the timetable simply shows nothing as done.
    /// </summary>
    private async Task<HashSet<string>> ReadCompletedAsync(CancellationToken ct)
    {
        if (!session.IsAuthenticated || !session.HasPermission(Gw2Permissions.Progression))
            return [];

        try
        {
            return [.. await client.GetCompletedWorldBossesAsync(ct)];
        }
        catch (Gw2ApiException)
        {
            // The timetable is the point; losing the "done" marks is not worth an error.
            return [];
        }
    }
}
