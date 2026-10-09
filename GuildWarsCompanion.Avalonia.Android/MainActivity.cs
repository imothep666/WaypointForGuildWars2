using Android;
using Android.App;
using Android.Content.PM;
using Android.Runtime;

using AndroidX.Core.Content;

using Avalonia.Android;

using GuildWarsCompanion.Ava.Host;
using GuildWarsCompanion.Host;
using GuildWarsCompanion.Platforms.Android.Background;
using GuildWarsCompanion.Platforms.Android;
using GuildWarsCompanion.Services.Alerts;
using GuildWarsCompanion.Services;

using Microsoft.Extensions.DependencyInjection;

using AndroidApplication = Android.App.Application;

namespace GuildWarsCompanion.Ava.Droid;

/// <summary>
/// Portrait only: every screen was laid out for it, and the compass turns its
/// own dial against the accelerometer rather than turning the window.
/// </summary>
[Activity(
    Label = "Waypoint",
    Theme = "@style/CompanionTheme",
    MainLauncher = true,
    ScreenOrientation = ScreenOrientation.Portrait,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity
{
    /// <summary>The activity on screen, for what has to start another one: the scanner.</summary>
    internal static MainActivity? Current { get; private set; }

    protected override void OnCreate(Android.OS.Bundle? savedInstanceState)
    {
        Current = this;
        base.OnCreate(savedInstanceState);
    }

    protected override void OnDestroy()
    {
        if (ReferenceEquals(Current, this)) Current = null;
        base.OnDestroy();
    }

    private const int NotificationRequest = 11;

    private static TaskCompletionSource<bool>? _notifications;

    /// <summary>
    /// Asks for permission to post notifications, which Android 13 and later want granted by the
    /// user. Older versions grant it with the install. False when there is no screen to ask from.
    /// </summary>
    internal static Task<bool> RequestNotificationsAsync()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(33)) return Task.FromResult(true);

        if (Current is not { } activity) return Task.FromResult(false);

        if (ContextCompat.CheckSelfPermission(activity, Manifest.Permission.PostNotifications) == Permission.Granted)
            return Task.FromResult(true);

        _notifications = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        activity.RequestPermissions([Manifest.Permission.PostNotifications], NotificationRequest);
        return _notifications.Task;
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);

        if (requestCode == NotificationRequest)
            _notifications?.TrySetResult(grantResults.Length > 0 && grantResults[0] == Permission.Granted);
    }
}

[Application]
public class CompanionApplication : AvaloniaAndroidApplication<App>
{
    /// <summary>
    /// The platform's half of the container, set here rather than when Avalonia starts: Android
    /// creates this class for every process — including one started only to run the reminder poll
    /// or redraw a widget, where no screen and no Avalonia app ever appear — and those find the
    /// services through HostServices, which builds them on first use.
    /// </summary>
    public CompanionApplication(nint javaReference, JniHandleOwnership transfer) : base(javaReference, transfer)
    {
        App.Platform = Register;
        HostServices.Build = App.BuildServices;
    }

    private static void Register(IServiceCollection services)
    {
        var context = AndroidApplication.Context;

        services.AddSingleton(Environment(context));
        services.AddSingleton<ISettings>(new PreferenceSettings(context));
        services.AddSingleton<ISecrets>(new KeystoreSecrets(context));
        services.AddSingleton<IQrScanner, CameraQrScanner>();
        services.AddSingleton<IKeepAwake, ActivityKeepAwake>();
        // Transient: each tilt owns its listener.
        services.AddTransient<IAccelerometer>(_ => new AndroidAccelerometer(context));

        // The bridge leaves by Wi-Fi, even when mobile data is the default network; see Wifi\.
        // Listening starts now, so the first send finds the network already known.
        WifiNetwork.Listen(context);
        services.AddSingleton<IStreamBinding, WifiStreamBinding>();
        services.AddSingleton<IBridgeDiscovery, WifiBridgeDiscovery>();
        services.AddSingleton<IBridgeTransport>(_ => new WifiBridgeTransport(new DefaultBridgeTransport()));

        // Reminders: posted to the shade, polled by WorkManager; see Alerts\.
        services.AddSingleton<INotifier>(_ => new AndroidNotifier(MainActivity.RequestNotificationsAsync));
        services.AddSingleton<IWatchScheduler, AndroidWatchScheduler>();

        // The share sheet, for the diagnostic log.
        services.AddSingleton<ISystemActions, AndroidSystemActions>();
        services.AddSingleton<IWidgetRedraw, GuildWarsCompanion.Platforms.Android.Widgets.AndroidWidgetRedraw>();
    }

    private static AppEnvironment Environment(Android.Content.Context context)
    {
        var version = context.PackageManager?.GetPackageInfo(context.PackageName!, 0)?.VersionName ?? "unknown";

        return new AppEnvironment(context.FilesDir!.AbsolutePath, context.CacheDir!.AbsolutePath, version);
    }
}
