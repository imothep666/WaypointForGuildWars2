using GuildWarsCompanion.Host;
using GuildWarsCompanion.Services;
using GuildWarsCompanion.ViewModels;
using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// The test plan ticking itself. The tests build in Debug, which is the only build where it does.
/// </summary>
public class TestEvidenceTests
{
    [Fact]
    public void FirstSightingTicksTheCheck()
    {
        var settings = new MemorySettings();

        new TestEvidence(settings).Saw("goal-ready");

        Assert.True(settings.Get(TestEvidence.TickKey("goal-ready"), false));
    }

    [Fact]
    public void AnUntickedCheckStaysUntickedWhenSeenAgain()
    {
        var settings = new MemorySettings();
        var evidence = new TestEvidence(settings);
        var plan = new TestPlanViewModel(settings, evidence);

        evidence.Saw("goal-ready");
        plan.Reload();

        var row = plan.Groups.SelectMany(group => group.Rows).Single(row => row.Check.Id == "goal-ready");
        plan.Toggle(row);

        evidence.Saw("goal-ready");
        plan.Reload();

        Assert.False(row.IsChecked);
        Assert.StartsWith("seen ", row.Evidence);
    }

    [Fact]
    public void ChecksTheAppCannotSeeAskForAHandTick()
    {
        var settings = new MemorySettings();
        var plan = new TestPlanViewModel(settings, new TestEvidence(settings));

        var rows = plan.Groups.SelectMany(group => group.Rows).ToList();

        Assert.Equal("tick by hand", rows.Single(row => row.Check.Id == "today-here").Evidence);
        Assert.Equal("ticks itself when the app sees it", rows.Single(row => row.Check.Id == "trail-arrival").Evidence);
    }

    [Fact]
    public void EveryCheckHasItsOwnId() =>
        Assert.Equal(TestPlan.Checks.Count, TestPlan.Checks.Select(check => check.Id).Distinct().Count());

    private sealed class MemorySettings : ISettings
    {
        private readonly Dictionary<string, object?> _values = [];

        public T Get<T>(string key, T defaultValue) =>
            _values.TryGetValue(key, out var value) && value is T typed ? typed : defaultValue;

        public void Set<T>(string key, T value) => _values[key] = value;

        public void Remove(string key) => _values.Remove(key);
    }
}
