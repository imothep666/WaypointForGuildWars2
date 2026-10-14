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

                // On the pool, not here: a broadcast arrives on the main thread, which may carry the
                // app's synchronization context, and a read that really is asynchronous - the meta
                // timetable's file - would post its continuation back to the thread blocked waiting
                // for it. That froze the app into an ANR on the first redraw that read the file.
                return Task.Run(() => widgets.ReadAsync(timeout.Token), timeout.Token).GetAwaiter().GetResult();
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

        views.SetImageViewBitmap(R.Id.boss_heading, Carved(context, L.T("World Bosses")));

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

        views.SetImageViewBitmap(R.Id.vault_heading, Carved(context, L.T("Wizard's Vault")));

        views.SetTextViewText(R.Id.vault_daily, panel.Daily);
        views.SetTextViewText(R.Id.vault_weekly, panel.Weekly);
        views.SetTextViewText(R.Id.vault_reset, panel.Reset);

        views.SetTextViewText(R.Id.vault_claim, panel.Claim);
        views.SetViewVisibility(R.Id.vault_claim, panel.HasClaim ? ViewStates.Visible : ViewStates.Gone);

        views.SetTextViewText(R.Id.vault_age, panel.Age ?? string.Empty);
        views.SetViewVisibility(R.Id.vault_age, panel.Age is null ? ViewStates.Gone : ViewStates.Visible);

        (int Row, int Title, int Count)[] tasks =
        [
            (R.Id.vault_task_one, R.Id.vault_task_one_title, R.Id.vault_task_one_count),
            (R.Id.vault_task_two, R.Id.vault_task_two_title, R.Id.vault_task_two_count),
            (R.Id.vault_task_three, R.Id.vault_task_three_title, R.Id.vault_task_three_count),
            (R.Id.vault_task_four, R.Id.vault_task_four_title, R.Id.vault_task_four_count),
            (R.Id.vault_task_five, R.Id.vault_task_five_title, R.Id.vault_task_five_count),
            (R.Id.vault_task_six, R.Id.vault_task_six_title, R.Id.vault_task_six_count)
        ];

        for (var i = 0; i < tasks.Length; i++)
        {
            var (row, title, count) = tasks[i];
            var task = i < panel.Tasks.Count ? panel.Tasks[i] : null;

            views.SetViewVisibility(row, task is null ? ViewStates.Gone : ViewStates.Visible);
            if (task is null) continue;

            // Done reads as done: the title steps back to the muted grey, the count becomes a
            // tick in the vault's own astral.
            views.SetTextViewText(title, task.Title);
            views.SetTextColor(title, task.Done ? Mist : Vellum);
            views.SetTextViewText(count, task.Progress);
            views.SetTextColor(count, task.Done ? Astral : Mist);
        }

        OpenApp(context, views, R.Id.vault_root);
        return views;
    }

    // The widget palette, Resources/values/widget_colors.xml, for lines coloured from code.
    private static readonly Color Vellum = new(0xE6, 0xDE, 0xCB);
    private static readonly Color Mist = new(0x7E, 0x8B, 0x9B);
    private static readonly Color Astral = new(0x46, 0xB7, 0xAE);
    private static readonly Color Brass = new(0xC8, 0xA3, 0x4E);

    public static RemoteViews Here(Context context, HerePanel panel)
    {
        var views = new RemoteViews(context.PackageName, R.Layout.widget_here);

        var art = panel.Art is { } map ? Painting(map) : null;

        if (art is not null) views.SetImageViewBitmap(R.Id.here_art, art);
        views.SetViewVisibility(R.Id.here_art, art is null ? ViewStates.Gone : ViewStates.Visible);
        views.SetViewVisibility(R.Id.here_wash, art is null ? ViewStates.Visible : ViewStates.Gone);

        views.SetImageViewBitmap(R.Id.here_map, Carved(context, panel.Map, 17));
        views.SetTextViewText(R.Id.here_who, panel.Who);

        var emblem = HostServices.Get<Callings>()?.Held(panel.Emblem) is { } file ? BitmapFactory.DecodeFile(file) : null;

        if (emblem is not null) views.SetImageViewBitmap(R.Id.here_emblem, emblem);
        views.SetViewVisibility(R.Id.here_emblem, emblem is null ? ViewStates.Gone : ViewStates.Visible);

        views.SetViewVisibility(R.Id.here_meta, panel.Meta is null ? ViewStates.Gone : ViewStates.Visible);

        if (panel.Meta is { } meta)
        {
            views.SetTextViewText(R.Id.here_meta_name, meta.Name);
            views.SetTextViewText(R.Id.here_meta_note, meta.Note);
            views.SetTextViewText(R.Id.here_meta_on, L.T("on now"));

            // Counted as the boss widget counts: on the elapsed-realtime clock, by the launcher.
            var untilMs = meta.StartsAt is { } start ? (long)(start - DateTimeOffset.UtcNow).TotalMilliseconds : 0;
            views.SetChronometer(R.Id.here_meta_countdown, SystemClock.ElapsedRealtime() + untilMs, null, !meta.IsOn);
            views.SetChronometerCountDown(R.Id.here_meta_countdown, true);

            views.SetViewVisibility(R.Id.here_meta_countdown, meta.IsOn ? ViewStates.Gone : ViewStates.Visible);
            views.SetViewVisibility(R.Id.here_meta_on, meta.IsOn ? ViewStates.Visible : ViewStates.Gone);
        }

        int[] lines = [R.Id.here_one, R.Id.here_two, R.Id.here_three];

        for (var i = 0; i < lines.Length; i++)
        {
            var fact = i < panel.Lines.Count ? panel.Lines[i] : null;

            views.SetTextViewText(lines[i], fact?.Text ?? string.Empty);
            views.SetTextColor(lines[i], fact?.Tone switch
            {
                HereTone.Waiting => Brass,
                HereTone.Done => Mist,
                _ => Vellum
            });
            views.SetViewVisibility(lines[i], fact is null ? ViewStates.Gone : ViewStates.Visible);
        }

        OpenApp(context, views, R.Id.here_root);
        return views;
    }

    /// <summary>The widest a painting is kept for a widget, in pixels. The wiki's copy is 900; a
    /// launcher has to hold every widget's bitmaps in one parcel, and a widget is a few inches.</summary>
    private const int PaintingWidth = 720;

    /// <summary>
    /// A map's loading screen as the Here widget's ground, or null when none is kept: decoded
    /// smaller, then darkened toward the left and the top, where the words are, and left brighter
    /// on the right, where the painting can be seen. The darkening is in the picture rather than a
    /// second view, since a launcher draws a widget from a fixed set of views.
    /// </summary>
    private static Bitmap? Painting(string map)
    {
        if (HostServices.Get<MapArt>()?.Held(map) is not { } file) return null;

        try
        {
            var bounds = new BitmapFactory.Options { InJustDecodeBounds = true };
            BitmapFactory.DecodeFile(file, bounds);

            var sample = 1;
            while (bounds.OutWidth / (sample * 2) >= PaintingWidth) sample *= 2;

            using var source = BitmapFactory.DecodeFile(file, new BitmapFactory.Options { InSampleSize = sample });
            if (source is null) return null;

            var painting = source.Copy(Bitmap.Config.Argb8888!, true)!;

            using var canvas = new Canvas(painting);
            using var shade = new Paint();

            // Across: words on the left, painting on the right.
            shade.SetShader(new LinearGradient(0, 0, painting.Width, 0,
                Ink(0xD8), Ink(0x50), Shader.TileMode.Clamp!));
            canvas.DrawRect(0, 0, painting.Width, painting.Height, shade);

            // And down a little, for the heading.
            shade.SetShader(new LinearGradient(0, 0, 0, painting.Height,
                Ink(0x60), Ink(0x00), Shader.TileMode.Clamp!));
            canvas.DrawRect(0, 0, painting.Width, painting.Height / 2f, shade);

            return painting;
        }
        catch (Exception)
        {
            // A half-written or unreadable file: the wash, as before there was art.
            return null;
        }
    }

    /// <summary>The app's ink, #0E141B, at a strength. A Color rather than an int: given ints,
    /// LinearGradient takes them for Android's 64-bit colours and throws "Invalid ID".</summary>
    private static Color Ink(int alpha) => new(Color.Argb(alpha, 0x0E, 0x14, 0x1B));

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
