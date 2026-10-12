using Android.App;
using Android.Appwidget;
using Android.Content;

namespace GuildWarsCompanion.Platforms.Android.Widgets;

/// <summary>The Wizard's Vault home screen widget. See <see cref="BossWidget"/>.</summary>
[BroadcastReceiver(Label = "@string/widget_vault", Exported = true)]
[IntentFilter([AppWidgetManager.ActionAppwidgetUpdate])]
[MetaData("android.appwidget.provider", Resource = "@xml/widget_vault_info")]
public sealed class VaultWidget : AppWidgetProvider
{
    public override void OnUpdate(Context? context, AppWidgetManager? manager, int[]? appWidgetIds) =>
        WidgetDraw.UpdateAll(context);

    public override void OnEnabled(Context? context) => WidgetRefresh.Enable();

    public override void OnDisabled(Context? context) => WidgetRefresh.DisableIfUnused(context);
}
