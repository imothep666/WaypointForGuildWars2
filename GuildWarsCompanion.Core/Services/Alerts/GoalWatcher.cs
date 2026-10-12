using GuildWarsCompanion.Data;
using GuildWarsCompanion.Gw2;

namespace GuildWarsCompanion.Services.Alerts;

/// <summary>
/// Tells you a pinned goal has everything it needs in hand.
///
/// Once per goal and quantity: the key carries both, so raising a goal from one to five is news
/// again when the fifth is covered, and nothing nags for as long as it stays ready. Spending the
/// materials and gathering them again does not announce it twice — the goal did not change.
/// </summary>
public sealed class GoalWatcher(GoalService goals) : IAccountWatcher
{
    public string Name => "goals";

    /// <summary>Holdings are the bank, material storage and bags.</summary>
    public IReadOnlyList<string> RequiredPermissions { get; } = [Gw2Permissions.Inventories];

    /// <summary>A goal already complete when this first runs is still worth being told about.</summary>
    public bool NeedsBaseline => false;

    public async Task<IReadOnlyList<AccountAlert>> CheckAsync(CancellationToken ct) =>
        (await goals.ReadyAsync(ct)).Select(GoalAlerts.Ready).ToList();
}

/// <summary>The words of the goal reminder.</summary>
public static class GoalAlerts
{
    public static AccountAlert Ready(Goal goal) =>
        new($"goal:{goal.ItemId}:{goal.Quantity}",
            L.T("Goal ready"),
            L.F("Everything for {0} is in hand.", GoalService.Title(goal)));
}
