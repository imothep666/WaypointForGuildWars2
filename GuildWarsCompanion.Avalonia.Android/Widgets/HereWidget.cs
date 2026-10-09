using Android.App;
using Android.Appwidget;
using Android.Content;

using GuildWarsCompanion.Host;

namespace GuildWarsCompanion.Platforms.Android.Widgets;

/// <summary>
/// The Here home screen widget: the map being played and what is left on it. See
/// <see cref="BossWidget"/>; it shares their poll, which also asks the PC who is playing where.
/// </summary>
[BroadcastReceiver(Label = "Here", Exported = true)]
[IntentFilter([AppWidgetManager.ActionAppwidgetUpdate])]
[MetaData("android.appwidget.provider", Resource = "@xml/widget_here_info")]
public sealed class HereWidget : AppWidgetProvider
{
    public override void OnUpdate(Context? context, AppWidgetManager? manager, int[]? appWidgetIds) =>
        WidgetDraw.UpdateAll(context);

    public override void OnEnabled(Context? context) => WidgetRefresh.Enable();

    public override void OnDisabled(Context? context) => WidgetRefresh.DisableIfUnused(context);
}

/// <summary>Lets Core redraw the widgets the moment it has something new for them.</summary>
public sealed class AndroidWidgetRedraw : IWidgetRedraw
{
    public void Redraw() => WidgetDraw.UpdateAll(global::Android.App.Application.Context);
}
