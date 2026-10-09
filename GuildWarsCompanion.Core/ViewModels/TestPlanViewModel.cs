using CommunityToolkit.Mvvm.ComponentModel;
using GuildWarsCompanion.Host;
using GuildWarsCompanion.Services;

namespace GuildWarsCompanion.ViewModels;

/// <summary>
/// The test plan as a checklist, for the debug build only.
///
/// A tick is kept in settings under the check's id the moment it is made, so a check done in the
/// game this evening is still ticked when the app is opened tomorrow. Some checks tick themselves
/// (see <see cref="TestEvidence"/>), often in the background poll while this screen is not open,
/// so the ticks are read again each time it appears.
/// </summary>
public sealed partial class TestPlanViewModel : BaseViewModel
{
    private readonly ISettings _settings;
    private readonly TestEvidence _evidence;

    [ObservableProperty] private string _summary = string.Empty;

    public TestPlanViewModel(ISettings settings, TestEvidence evidence)
    {
        _settings = settings;
        _evidence = evidence;

        Groups = TestPlan.Checks
            .GroupBy(check => check.Area)
            .Select(group => new TestGroup(group.Key.ToUpperInvariant(),
                group.Select(check => new TestRow(check, Ticked)).ToList()))
            .ToList();

        Reload();
    }

    public IReadOnlyList<TestGroup> Groups { get; }

    private IEnumerable<TestRow> Rows => Groups.SelectMany(group => group.Rows);

    public void Toggle(TestRow row) => row.IsChecked = !row.IsChecked;

    /// <summary>Picks up what ticked itself since the screen was last open.</summary>
    public void Reload()
    {
        foreach (var row in Rows)
            row.Load(_settings.Get(TestEvidence.TickKey(row.Check.Id), false), _evidence.LastSeen(row.Check.Id));

        Count();
    }

    private void Ticked(TestRow row)
    {
        var key = TestEvidence.TickKey(row.Check.Id);

        if (row.IsChecked) _settings.Set(key, true);
        else _settings.Remove(key);

        Count();
    }

    private void Count()
    {
        var rows = Rows.ToList();

        Summary = $"{rows.Count(row => row.IsChecked)} of {rows.Count} checked · {TestPlan.Built}";
    }
}

public sealed record TestGroup(string Title, IReadOnlyList<TestRow> Rows);

public sealed partial class TestRow(TestCheck check, Action<TestRow> ticked) : ObservableObject
{
    private bool _loading;

    public TestCheck Check { get; } = check;

    public string Do => Check.Do;

    public string Expect => Check.Expect;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Mark), nameof(Fade))]
    private bool _isChecked;

    /// <summary>
    /// "ticks itself when the app sees it", or when it last did: "seen Tue 21:14". A check only the
    /// player can judge says "tick by hand", so it is not left waiting for the app.
    /// </summary>
    [ObservableProperty]
    private string? _evidence;

    public string Mark => IsChecked ? "✓" : string.Empty;

    /// <summary>A ticked check steps back, so what is left to try stands out.</summary>
    public double Fade => IsChecked ? 0.5 : 1.0;

    public void Load(bool isChecked, DateTimeOffset? seen)
    {
        _loading = true;
        IsChecked = isChecked;
        _loading = false;

        Evidence = seen is { } at
            ? $"seen {at.ToLocalTime():ddd HH:mm}"
            : Check.Auto ? "ticks itself when the app sees it" : "tick by hand";
    }

    partial void OnIsCheckedChanged(bool value)
    {
        if (!_loading) ticked(this);
    }
}
