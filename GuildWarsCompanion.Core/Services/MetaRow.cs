using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace GuildWarsCompanion.Services;

/// <summary>One of a map's events, with its reminder switch.</summary>
public sealed partial class MetaChoice(string mapKey, string name, MetaReminderStore store) : ObservableObject
{
    public string Name { get; } = name;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Label))]
    private bool _isReminded = store.IsChosen(mapKey, name);

    /// <summary>What the button says it is doing, not what it would do.</summary>
    public string Label => IsReminded ? "REMINDING" : "REMIND";

    public event Action? Changed;

    [RelayCommand]
    public void Toggle()
    {
        IsReminded = !IsReminded;
        store.Set(mapKey, Name, IsReminded);
        Changed?.Invoke();
    }
}

/// <summary>
/// One map's metas on the timetable: what is on now, or what comes next and when. Observable for
/// the same reason as <see cref="EventRow"/> — the countdown ticks while the screen is open.
/// </summary>
public sealed partial class MetaRow : ObservableObject
{
    /// <summary>Close enough to start travelling — the same window as a boss.</summary>
    private static readonly TimeSpan Imminent = TimeSpan.FromMinutes(15);

    [ObservableProperty] private string _countdown = string.Empty;
    [ObservableProperty] private string _subtitle = string.Empty;
    [ObservableProperty] private string _localTime = string.Empty;
    [ObservableProperty] private Color _edgeColor = Colors.Transparent;

    public MetaRow(MetaMap map, DateTimeOffset utcNow, MetaReminderStore reminders)
    {
        Map = map;
        Choices = MetaSchedule.Events(map).Select(name => new MetaChoice(map.Key, name, reminders)).ToList();

        foreach (var choice in Choices) choice.Changed += () => OnPropertyChanged(nameof(Reminding));

        Tick(utcNow);
    }

    public MetaMap Map { get; }

    /// <summary>The map's events, to choose which to be reminded of. Shown when the row is opened.</summary>
    public IReadOnlyList<MetaChoice> Choices { get; }

    [ObservableProperty] private bool _isOpen;

    /// <summary>"reminding: Battle in Tarir (Octovine)" under a closed row, so a set reminder can be
    /// seen without opening it; empty when none is.</summary>
    public string Reminding =>
        Choices.Where(choice => choice.IsReminded).Select(choice => choice.Name).ToList() is { Count: > 0 } names
            ? $"reminding: {string.Join(", ", names)}"
            : string.Empty;

    [RelayCommand]
    public void ToggleOpen() => IsOpen = !IsOpen;

    public string Name => Map.Name;

    /// <summary>How long until something here is on; zero while it is. For ordering the list.</summary>
    public TimeSpan Wait { get; private set; }

    public void Tick(DateTimeOffset utcNow)
    {
        if (MetaSchedule.Current(Map, utcNow) is { } now)
        {
            Wait = TimeSpan.Zero;
            Countdown = "now";
            Subtitle = $"{now.Name} · until {now.End.ToLocalTime():HH:mm}";
            LocalTime = string.Empty;
            EdgeColor = Theme.Brass;
            return;
        }

        if (MetaSchedule.Next(Map, utcNow) is not { } next)
        {
            Wait = TimeSpan.MaxValue;
            Countdown = string.Empty;
            Subtitle = Map.Category;
            LocalTime = string.Empty;
            EdgeColor = Colors.Transparent;
            return;
        }

        Wait = next.Start - utcNow;
        Countdown = Meta.Until(Wait);
        Subtitle = next.Name;
        LocalTime = next.Start.ToLocalTime().ToString("HH:mm");
        EdgeColor = Wait <= Imminent ? Theme.Brass : Colors.Transparent;
    }
}

/// <summary>The words shared by the timetable and the "Here:" line.</summary>
public static class Meta
{
    /// <summary>"25 min", "1h 05m", "&lt; 1 min" — the timetable's own countdown.</summary>
    public static string Until(TimeSpan wait) =>
        wait < TimeSpan.FromMinutes(1)
            ? "< 1 min"
            : wait < TimeSpan.FromHours(1)
                ? $"{(int)wait.TotalMinutes} min"
                : $"{(int)wait.TotalHours}h {wait.Minutes:00}m";
}
