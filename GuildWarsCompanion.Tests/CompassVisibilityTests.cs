using System.Globalization;

using GuildWarsCompanion.Services;

using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// What the dial is allowed to draw once the player has had their say.
///
/// The part worth testing is the key: a dismissal is remembered by map, kind and position, and
/// everything else here depends on two targets that are the same thing agreeing on one string.
/// </summary>
public class CompassVisibilityTests
{
    private static CompassTarget Target(
        TargetKind kind = TargetKind.Heart, int map = 15, double x = 44416.4, double y = 29248.6) =>
        new("Help the farmers", kind, map, "Queensdale", x, y);

    [Fact]
    public void Everything_is_shown_by_default()
    {
        Assert.True(CompassVisibility.Everything.Shows(Target()));
        Assert.True(CompassVisibility.Everything.ShowsKind(TargetKind.Vista));
    }

    [Fact]
    public void Hiding_a_kind_hides_every_one_of_them()
    {
        var hidden = CompassVisibility.Everything.ShowingKind(TargetKind.Heart, false);

        Assert.False(hidden.Shows(Target()));
        Assert.False(hidden.Shows(Target(x: 1, y: 2)));

        // And nothing else. A switch that quietly took the waypoints with it would be found only
        // by someone counting marks.
        Assert.True(hidden.Shows(Target(TargetKind.Waypoint)));
    }

    [Fact]
    public void A_kind_can_be_brought_back()
    {
        var back = CompassVisibility.Everything
            .ShowingKind(TargetKind.Vista, false)
            .ShowingKind(TargetKind.Vista, true);

        Assert.True(back.Shows(Target(TargetKind.Vista)));
        Assert.Empty(back.HiddenKinds);
    }

    [Fact]
    public void Dismissing_hides_that_one_and_not_its_neighbour()
    {
        var one = Target();
        var other = Target(x: 44500);

        var after = CompassVisibility.Everything.Dismissing(one, true);

        Assert.False(after.Shows(one));
        Assert.True(after.IsDismissed(one));
        Assert.True(after.Shows(other));
    }

    /// <summary>
    /// The same landmark on a later run is a different <see cref="CompassTarget"/> instance with
    /// the same coordinates, and it has to be recognised as the one that was dismissed — that is
    /// the whole point of storing a key rather than a reference.
    /// </summary>
    [Fact]
    public void A_dismissal_survives_the_target_being_built_again()
    {
        var after = CompassVisibility.Everything.Dismissing(Target(), true);

        Assert.False(after.Shows(Target()));
    }

    [Fact]
    public void The_key_separates_map_kind_and_position()
    {
        Assert.NotEqual(
            CompassVisibility.KeyOf(Target(map: 15)),
            CompassVisibility.KeyOf(Target(map: 24)));

        Assert.NotEqual(
            CompassVisibility.KeyOf(Target(TargetKind.Heart)),
            CompassVisibility.KeyOf(Target(TargetKind.Vista)));

        Assert.NotEqual(
            CompassVisibility.KeyOf(Target(x: 44416)),
            CompassVisibility.KeyOf(Target(x: 44417)));
    }

    /// <summary>
    /// A phone set to German writes 44416,4 where an English one writes 44416.4. Without
    /// invariant formatting a dismissal would vanish the moment the handset's language changed,
    /// which is the sort of thing nobody connects to the setting they changed a week ago.
    /// </summary>
    [Fact]
    public void The_key_does_not_change_with_the_phones_language()
    {
        var was = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("en-GB");
            var english = CompassVisibility.KeyOf(Target());

            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var german = CompassVisibility.KeyOf(Target());

            Assert.Equal(english, german);
        }
        finally
        {
            CultureInfo.CurrentCulture = was;
        }
    }

    [Fact]
    public void Restoring_brings_back_dismissals_and_leaves_the_kinds_alone()
    {
        var after = CompassVisibility.Everything
            .Dismissing(Target(), true)
            .ShowingKind(TargetKind.Waypoint, false)
            .RestoringAll();

        Assert.True(after.Shows(Target()));
        Assert.False(after.ShowsKind(TargetKind.Waypoint));
    }

    /// <summary>
    /// Every kind reachable from the sheet, or one of them can be hidden and never found again.
    /// </summary>
    [Fact]
    public void The_sheet_lists_every_kind_there_is()
    {
        Assert.Equal(
            Enum.GetValues<TargetKind>().OrderBy(k => k),
            CompassVisibility.AllKinds.OrderBy(k => k));

        foreach (var kind in CompassVisibility.AllKinds)
            Assert.False(string.IsNullOrWhiteSpace(CompassVisibility.Plural(kind)));
    }
}
