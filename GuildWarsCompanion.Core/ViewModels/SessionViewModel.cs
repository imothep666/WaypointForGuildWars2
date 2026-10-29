using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GuildWarsCompanion.Gw2;
using GuildWarsCompanion.Services;

namespace GuildWarsCompanion.ViewModels;

/// <summary>
/// The page behind Today's recap: one session in full — what it earned, the whole wallet, the
/// items gained and spent, the progress made — and the fortnight of sessions the ledger keeps.
/// </summary>
public sealed partial class SessionViewModel(LedgerService ledger, ApiKeySession session, TestEvidence evidence)
    : BaseViewModel
{
    /// <summary>"Last session", "This session".</summary>
    [ObservableProperty] private string? _title;

    /// <summary>"2 h 10 min", the page's headline.</summary>
    [ObservableProperty] private string? _duration;

    [ObservableProperty] private string? _when;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMissing))]
    private string? _missing;

    public bool HasMissing => Missing is not null;

    /// <summary>"Last 7 days · 9 h 20 min in 5 sessions".</summary>
    [ObservableProperty] private string? _week;

    public ObservableCollection<string> Characters { get; } = [];

    public ObservableCollection<SessionLine> Earned { get; } = [];

    public ObservableCollection<SessionLine> Wallet { get; } = [];

    public ObservableCollection<SessionLine> Gained { get; } = [];

    public ObservableCollection<SessionLine> Spent { get; } = [];

    public ObservableCollection<string> Progress { get; } = [];

    public ObservableCollection<SessionDay> Days { get; } = [];

    public bool HasEarned => Earned.Count > 0;

    public bool HasWallet => Wallet.Count > 0;

    public bool HasGained => Gained.Count > 0;

    public bool HasSpent => Spent.Count > 0;

    public bool HasProgress => Progress.Count > 0;

    public bool HasDays => Days.Count > 0;

    /// <summary>The session on the page, by when it was measured from; null for the latest.</summary>
    private DateTimeOffset? _shown;

    protected override string DefaultEmptyMessage => L.T("No session yet: the recap starts with the next time you play.");

    /// <summary>The latest session, as when the page is opened from Today.</summary>
    public Task ShowLatestAsync()
    {
        _shown = null;
        return RefreshAsync();
    }

    /// <summary>An earlier session from the list, in place of the one on the page.</summary>
    [RelayCommand]
    public Task ShowAsync(SessionRow? row)
    {
        if (row is null || row.IsShown) return Task.CompletedTask;

        _shown = row.Key;
        return RefreshAsync();
    }

    [RelayCommand]
    public Task RefreshAsync() => RunAsync(async ct =>
    {
        Clear();

        await session.EnsureRestoredAsync(ct);

        if (!session.IsAuthenticated)
        {
            Notice = L.T("Add your API key under Settings to see what is left today.");
            return;
        }

        if (!ledger.CanRecord)
        {
            Notice = L.T("A recap needs the 'characters' and 'wallet' permissions. Create a new key that includes them.");
            return;
        }

        Notice = null;

        var sessions = await ledger.AllAsync(ct);
        var played = sessions.FirstOrDefault(candidate => candidate.Before.At == _shown) ?? sessions.FirstOrDefault();

        if (played is null) return;

        var report = await ledger.ReportAsync(played, ct);

        Title = played != sessions[0] ? L.T("Earlier session")
            : played.IsOngoing ? L.T("This session")
            : L.T("Last session");

        Duration = SessionRecap.Duration(played.PlayedSeconds);
        When = report.Text.When;
        Missing = report.Missing;

        foreach (var line in report.Text.Characters) Characters.Add(line);
        foreach (var line in report.Earned) Earned.Add(line);
        foreach (var line in report.Wallet) Wallet.Add(line);
        foreach (var line in report.Gained) Gained.Add(line);
        foreach (var line in report.Spent) Spent.Add(line);
        foreach (var line in report.Progress) Progress.Add(line);

        foreach (var day in await ledger.DaysAsync(sessions, played.Before.At, ct)) Days.Add(day);

        var week = sessions.Where(earlier => DateTimeOffset.UtcNow - earlier.LastSeen.At < TimeSpan.FromDays(7)).ToList();
        Week = L.F("Last 7 days · {0} in {1} sessions",
            SessionRecap.Duration(week.Sum(earlier => earlier.PlayedSeconds)), week.Count);

        Announce();

        evidence.Saw("session-page");
        if (Earned.Count > 0) evidence.Saw("session-loot");
        if (Progress.Count > 0) evidence.Saw("session-progress");
        if (sessions.Count > 1) evidence.Saw("session-earlier");
    });

    private void Clear()
    {
        Title = Duration = When = Missing = Week = null;

        Characters.Clear();
        Earned.Clear();
        Wallet.Clear();
        Gained.Clear();
        Spent.Clear();
        Progress.Clear();
        Days.Clear();

        Announce();
    }

    private void Announce()
    {
        OnPropertyChanged(nameof(HasEarned));
        OnPropertyChanged(nameof(HasWallet));
        OnPropertyChanged(nameof(HasGained));
        OnPropertyChanged(nameof(HasSpent));
        OnPropertyChanged(nameof(HasProgress));
        OnPropertyChanged(nameof(HasDays));
    }
}
