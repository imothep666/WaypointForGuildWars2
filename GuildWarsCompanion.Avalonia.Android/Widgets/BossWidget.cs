using Android.App;
using Android.Appwidget;
using Android.Content;

namespace GuildWarsCompanion.Platforms.Android.Widgets;

/// <summary>
/// The world boss home screen widget.
///
/// Exported because the receiver's caller is the system: an unexported provider is simply
/// never offered in the widget picker.
/// </summary>
[BroadcastReceiver(Label = "World Bosses", Exported = true)]
[IntentFilter([AppWidgetManager.ActionAppwidgetUpdate])]
[MetaData("android.appwidget.provider", Resource = "@xml/widget_bosses_info")]
public sealed class BossWidget : AppWidgetProvider
{
    public override void OnUpdate(Context? context, AppWidgetManager? manager, int[]? appWidgetIds)
    {
        // Both widgets are redrawn together. They read the same store in one pass, and a user
        // with both on the same screen should never see them disagree about the time.
        WidgetDraw.UpdateAll(context);
    }

    /// <summary>The first instance being placed is what starts the refresh.</summary>
    public override void OnEnabled(Context? context) => WidgetRefresh.Enable();

    /// <summary>Stops only when neither widget is left anywhere.</summary>
    public override void OnDisabled(Context? context) => WidgetRefresh.DisableIfUnused(context);
}
