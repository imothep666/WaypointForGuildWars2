using GuildWarsCompanion.Data;
using GuildWarsCompanion.Services.Alerts;

using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>The "goal ready" reminder: once per goal and quantity, in plain words.</summary>
public class GoalAlertsTests
{
    private static Goal Spearhead(int quantity) => new(1, 74356, "Weighted Spearhead", null, quantity);

    [Fact]
    public void One_reminder_per_goal_and_quantity()
    {
        Assert.Equal(GoalAlerts.Ready(Spearhead(1)).Key, GoalAlerts.Ready(Spearhead(1)).Key);

        // Raising the goal to five is news again once the fifth is covered.
        Assert.NotEqual(GoalAlerts.Ready(Spearhead(1)).Key, GoalAlerts.Ready(Spearhead(5)).Key);
    }

    [Fact]
    public void The_words_name_the_goal_and_how_many()
    {
        Assert.Equal("Everything for Weighted Spearhead is in hand.", GoalAlerts.Ready(Spearhead(1)).Body);
        Assert.Equal("Everything for Weighted Spearhead × 5 is in hand.", GoalAlerts.Ready(Spearhead(5)).Body);
    }
}
