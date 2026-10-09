using System.Reflection;

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

using GuildWarsCompanion.Host;

using Microsoft.Extensions.DependencyInjection;

using Velopack;

namespace GuildWarsCompanion.Ava.Desktop;

internal static class Program
{
    // Avalonia needs this before any SynchronizationContext exists, so it must stay the entry
    // point and stay free of anything that touches the toolkit.
    [STAThread]
    public static void Main(string[] args)
    {
        // First, before anything else runs: an installer or updater may start the app only to
        // run a hook and exit, and an update downloaded but never restarted for is applied here.
        VelopackApp.Build().Run();

        App.Platform = services =>
        {
            var environment = Environment();

            LinuxMenuEntry.Refresh(environment.DataDirectory, args);

            services.AddSingleton(environment);
            services.AddSingleton<ISettings>(new FileSettings(environment.DataDirectory));
            services.AddSingleton<ISecrets>(new FileSecrets(environment.DataDirectory));
            services.AddSingleton<IWindowMode, DesktopWindowMode>();
            services.AddSingleton<IAppUpdates>(provider => new VelopackUpdates(
                provider.GetRequiredService<IMainThread>(),
                provider.GetRequiredService<Bridge.Protocol.Journal>(),
                args,
                exit: () => provider.GetRequiredService<IMainThread>().BeginInvokeOnMainThread(() =>
                    (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown())));
        };

        BuildAvaloniaApp(args.Contains("--wayland"), args.Contains("--software")).StartWithClassicDesktopLifetime(args);
    }

    /// <summary>Also called by the Avalonia designer, which is why it is public.</summary>
    public static AppBuilder BuildAvaloniaApp() => BuildAvaloniaApp(wayland: false, software: false);

    private static AppBuilder BuildAvaloniaApp(bool wayland, bool software)
    {
        var builder = AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();

        // Drawing on the processor instead of the GPU, for a Linux whose GL driver will not have
        // it — the Tegra X1 under switchroot's L4T is the one in mind, and untested. Slower, but a
        // window that draws beats a black one; packaging/linux/install.sh --software sets it.
        if (software && OperatingSystem.IsLinux())
            builder = builder.With(new X11PlatformOptions { RenderingMode = [X11RenderingMode.Software] });

        // Win32 on Windows, X11 on Linux — XWayland under a Wayland session. Native Wayland is
        // asked for by name because 12.1 ships it as experimental and UsePlatformDetect leaves it
        // out; it is the backend Ubuntu Touch needs, so it is one switch away.
        return wayland && OperatingSystem.IsLinux() ? builder.UseWayland() : builder;
    }

    /// <summary>
    /// The platform's own places: %LOCALAPPDATA% on Windows; on Linux the XDG data and cache
    /// directories, ~/.local/share and ~/.cache unless the session says otherwise.
    ///
    /// Create, not the default: by default a folder that does not exist yet comes back as an empty
    /// string, and the data then landed in a relative "GuildWarsCompanion" in whatever directory
    /// the app was started from. Ubuntu Touch's launcher points XDG_DATA_HOME at a fresh folder,
    /// so there that would be the first run, every time, outside what confinement allows.
    /// </summary>
    private static AppEnvironment Environment()
    {
        const string name = "GuildWarsCompanion";

        var data = Path.Combine(
            System.Environment.GetFolderPath(
                System.Environment.SpecialFolder.LocalApplicationData,
                System.Environment.SpecialFolderOption.Create),
            name);

        var cache = OperatingSystem.IsWindows()
            ? Path.Combine(data, "cache")
            : Path.Combine(
                System.Environment.GetEnvironmentVariable("XDG_CACHE_HOME") is { Length: > 0 } xdg
                    ? xdg
                    : Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), ".cache"),
                name);

        Directory.CreateDirectory(data);
        Directory.CreateDirectory(cache);

        var version = typeof(Program).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

        return new AppEnvironment(data, cache, version);
    }
}
