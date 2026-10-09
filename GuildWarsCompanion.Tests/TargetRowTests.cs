using GuildWarsCompanion.Services;
using GuildWarsCompanion.ViewModels;

using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// The compass's objective list: its order, and a distance that can be measured again.
///
/// The rows used to keep the distance they were built with, so a port within one map left the
/// list saying how far everything was from where the character had been.
/// </summary>
public class TargetRowTests
{
    private static TargetRow Row(string name, TargetKind kind, double distance, string? note = null) =>
        new(new CompassTarget(name, kind, 1015, "The Silverwastes", 0, 0, Note: note), distance);

    [Fact]
    public void What_is_on_the_map_comes_nearest_first()
    {
        var far = Row("Charnel Grounds", TargetKind.Landmark, 903);
        var near = Row("Camp Resolve Waypoint", TargetKind.Waypoint, 1);
        var middle = Row("Chest", TargetKind.Chest, 78);

        Assert.Equal([near, middle, far], TargetRow.Ranked([far, near, middle]));
    }

    [Fact]
    public void World_bosses_follow_in_the_order_they_were_given()
    {
        // By spawn time, which is the question about a boss; a near one is no more urgent.
        var soon = Row("Tequatl", TargetKind.WorldBoss, 9000, "in 5 min");
        var later = Row("Shatterer", TargetKind.WorldBoss, 10, "in 40 min");
        var heart = Row("Help", TargetKind.Heart, 500);

        Assert.Equal([heart, soon, later], TargetRow.Ranked([soon, heart, later]));
    }

    [Fact]
    public void A_new_distance_reorders_the_list_and_rewords_the_row()
    {
        var a = Row("A", TargetKind.Landmark, 100);
        var b = Row("B", TargetKind.Landmark, 200);

        // The character ported: B is now the near one.
        a.Distance = 900;
        b.Distance = 50;

        Assert.Equal([b, a], TargetRow.Ranked([a, b]));
        Assert.Equal("Landmark · 900 away", a.Detail);
    }

    [Fact]
    public void Changing_the_distance_tells_the_list_the_words_changed()
    {
        var row = Row("A", TargetKind.Landmark, 100);
        var changed = new List<string?>();

        row.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        row.Distance = 200;

        Assert.Contains(nameof(TargetRow.Detail), changed);
    }
}
