using Android.App;
using Android.Appwidget;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Text;
using Android.Views;
using Android.Widget;
using GuildWarsCompanion.Host;
using GuildWarsCompanion.Services;

using R = _Microsoft.Android.Resource.Designer.Resource;

namespace GuildWarsCompanion.Platforms.Android.Widgets;

/// <summary>
/// Turns a panel into the RemoteViews a launcher draws, and pushes it out.
///
/// Everything here is text-setting. The decisions are all made in
/// <see cref="WidgetContent"/>, where they can be tested without an emulator — a widget is one
/// of the few surfaces in the app that cannot be looked at while it is being written.
/// </summary>
internal static class WidgetDraw
{
    /// <summary>
    /// Reads what both widgets should show, without the network.
    ///
    /// The fallback matters more than it looks. A launcher can ask for a redraw while the app
    /// process is cold — on unlock, on rotation, after a theme change — and the app's container
    /// may not exist yet. The boss half needs no container at all, because the timetable is a
    /// local table, so it still draws correct times; only the "already killed" marks are lost,
    /// and those go absent rather than wrong.
    /// </summary>
    private static (BossPanel Bosses, VaultPanel Vault, HerePanel Here) Read()
    {
        var services = HostServices.Current;

        if (services?.GetService(typeof(WidgetService)) is WidgetService widgets)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                return widgets.ReadAsync(timeout.Token).GetAwaiter().GetResult();
            }
            catch (Exception)
            {
                // A locked or missing database must not take the home screen down with it.
            }
        }

        var now = DateTimeOffset.UtcNow;

        return (WidgetContent.Bosses(EventSchedule.NextAppearances(now), null, null, now),
            WidgetContent.Vault(null, null, null, now),
            GuildWarsCompanion.Services.HereWidget.Panel(null, null, now));
    }

    /// <summary>Context is nullable because the broadcast callbacks declare it so; a redraw
    /// with nothing to draw into is simply skipped.</summary>
    public static void UpdateAll(Context? context)
    {
        if (context is null) return;

        var manager = AppWidgetManager.GetInstance(context);
        if (manager is null) return;

        var (bosses, vault, here) = Read();

        Push(context, manager, typeof(BossWidget), () => Bosses(context, bosses));
        Push(context, manager, typeof(VaultWidget), () => Vault(context, vault));
        Push(context, manager, typeof(HereWidget), () => Here(context, here));
    }

    /// <summary>Draws one provider's instances, if any are on a home screen.</summary>
    private static void Push(
        Context context, AppWidgetManager manager, Type provider, Func<RemoteViews> draw)
    {
        var ids = manager.GetAppWidgetIds(new ComponentName(context, Java.Lang.Class.FromType(provider)));
        if (ids is null || ids.Length == 0) return;

        manager.UpdateAppWidget(ids, draw());
    }

    public static RemoteViews Bosses(Context context, BossPanel panel)
    {
        var views = new RemoteViews(context.PackageName, R.Layout.widget_bosses);

        views.SetImageViewBitmap(R.Id.boss_heading, Carved(context, "World Bosses"));

        if (panel.Next is { } next)
        {
            views.SetTextViewText(R.Id.boss_name, next.Name);
            views.SetTextViewText(R.Id.boss_where, $"{next.Location} · {next.TimeText}");

            // Counted down by the launcher, so the seconds stay live while this app sleeps.
            // The base is on the elapsed-realtime clock, which is what Chronometer reads.
            var untilMs = (long)(next.At - DateTimeOffset.UtcNow).TotalMilliseconds;
            views.SetChronometer(R.Id.boss_countdown, SystemClock.ElapsedRealtime() + untilMs, null, true);
            views.SetChronometerCountDown(R.Id.boss_countdown, true);
        }

        var later = panel.Later;
        Line(views, R.Id.boss_later_one, later.Count > 0 ? later[0] : null);
        Line(views, R.Id.boss_later_two, later.Count > 1 ? later[1] : null);

        views.SetTextViewText(R.Id.boss_footnote, panel.Footnote);
        views.SetViewVisibility(R.Id.boss_footnote,
            panel.Footnote.Length == 0 ? ViewStates.Gone : ViewStates.Visible);

        OpenApp(context, views, R.Id.boss_root);
        return views;
    }

    private static void Line(RemoteViews views, int id, BossLine? call)
    {
        views.SetTextViewText(id, call is null ? string.Empty : $"{call.TimeText}  {call.Name}");
        views.SetViewVisibility(id, call is null ? ViewStates.Gone : ViewStates.Visible);
    }

    public static RemoteViews Vault(Context context, VaultPanel panel)
    {
        var views = new RemoteViews(context.PackageName, R.Layout.widget_vault);

        views.SetImageViewBitmap(R.Id.vault_heading, Carved(context, "Wizard's Vault"));

        views.SetTextViewText(R.Id.vault_daily, panel.Daily);
        views.SetTextViewText(R.Id.vault_weekly, panel.Weekly);
        views.SetTextViewText(R.Id.vault_reset, panel.Reset);

        views.SetTextViewText(R.Id.vault_claim, panel.Claim);
        views.SetViewVisibility(R.Id.vault_claim, panel.HasClaim ? ViewStates.Visible : ViewStates.Gone);

        views.SetTextViewText(R.Id.vault_age, panel.Age ?? string.Empty);
        views.SetViewVisibility(R.Id.vault_age, panel.Age is null ? ViewStates.Gone : ViewStates.Visible);

        OpenApp(context, views, R.Id.vault_root);
        return views;
    }

    public static RemoteViews Here(Context context, HerePanel panel)
    {
        var views = new RemoteViews(context.PackageName, R.Layout.widget_here);

        views.SetImageViewBitmap(R.Id.here_map, Carved(context, panel.Map, 17));
        views.SetTextViewText(R.Id.here_who, panel.Who);

        int[] lines = [R.Id.here_one, R.Id.here_two, R.Id.here_three];

        for (var i = 0; i < lines.Length; i++)
        {
            var text = i < panel.Lines.Count ? panel.Lines[i] : null;

            views.SetTextViewText(lines[i], text ?? string.Empty);
            views.SetViewVisibility(lines[i], text is null ? ViewStates.Gone : ViewStates.Visible);
        }

        OpenApp(context, views, R.Id.here_root);
        return views;
    }

    private static Typeface? _marcellus;

    /// <summary>
    /// A heading in the app's carved capitals, painted into a bitmap. A launcher may ignore a font
    /// resource on a TextView - the phone's own did - but every launcher shows a picture. Painted
    /// at the screen's density, so it is as sharp as text; a heading too long for the widget is
    /// cut with an ellipsis at 300 dp, and the ImageView scales anything wider down to fit.
    /// </summary>
    private static Bitmap Carved(Context context, string text, float sp = 16)
    {
        var metrics = context.Resources!.DisplayMetrics!;

        _marcellus ??= context.Resources.GetFont(R.Font.marcellus_sc);

        using var paint = new TextPaint(PaintFlags.AntiAlias)
        {
            Color = new Color(0xE6, 0xDE, 0xCB),
            TextSize = TypedValue(sp, metrics),
            LetterSpacing = 0.08f
        };
        paint.SetTypeface(_marcellus);

        var room = 300 * metrics.Density;
        var shown = TextUtils.Ellipsize(text, paint, room, TextUtils.TruncateAt.End!) ?? text;

        var font = paint.GetFontMetrics()!;
        var width = Math.Max(1, (int)Math.Ceiling(paint.MeasureText(shown)) + 2);
        var height = Math.Max(1, (int)Math.Ceiling(font.Descent - font.Ascent) + 2);

        var bitmap = Bitmap.CreateBitmap(width, height, Bitmap.Config.Argb8888!)!;
        bitmap.Density = (int)metrics.DensityDpi;

        using var canvas = new Canvas(bitmap);
        canvas.DrawText(shown, 0, -font.Ascent, paint);

        return bitmap;
    }

    private static float TypedValue(float sp, global::Android.Util.DisplayMetrics metrics) =>
        global::Android.Util.TypedValue.ApplyDimension(global::Android.Util.ComplexUnitType.Sp, sp, metrics);

    /// <summary>A tap anywhere opens the app, which is the only thing a widget can offer.</summary>
    private static void OpenApp(Context context, RemoteViews views, int rootId)
    {
        var launch = context.PackageManager?.GetLaunchIntentForPackage(context.PackageName!);
        if (launch is null) return;

        var pending = PendingIntent.GetActivity(
            context, 0, launch, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

        views.SetOnClickPendingIntent(rootId, pending);
    }
}
