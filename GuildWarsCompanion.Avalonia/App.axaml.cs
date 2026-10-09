using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

using GuildWarsCompanion.Ava.Controls;
using GuildWarsCompanion.Ava.Host;
using GuildWarsCompanion.Ava.Shell;
using GuildWarsCompanion.Host;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GuildWarsCompanion.Ava;

public partial class App : Application
{
    /// <summary>
    /// What the platform head adds before anything else is registered: where files live, how
    /// settings and secrets are kept. Set by the head before Avalonia starts.
    /// </summary>
    public static Action<IServiceCollection>? Platform { get; set; }

    /// <summary>The app's container. Built on first use, by whichever comes first: the first
    /// screen, or Android starting the process cold for a reminder poll or a widget redraw.</summary>
    public static IServiceProvider Services =>
        HostServices.Current ?? throw new InvalidOperationException("No services: nothing set HostServices.Build.");

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        // The compass's direct path for marker images, around ICanvas.DrawImage's allocations.
        Drawing.SymbolStamp.Platform = SkiaStamp.TryDraw;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        HostServices.Build ??= BuildServices;

        var shells = Services.GetRequiredService<Shells>();

        switch (ApplicationLifetime)
        {
            case IClassicDesktopStyleApplicationLifetime desktop:
                var window = new MainWindow { Page = shells.Current };
                desktop.MainWindow = window;
                var updates = Services.GetRequiredService<IAppUpdates>();
                window.Updates = updates;
                updates.Start();

                var windowMode = Services.GetRequiredService<IWindowMode>();

                // Leaving fullscreen only touches a window that is in it, so a maximized one stays.
                windowMode.Attach(fullScreen =>
                {
                    if (fullScreen) window.WindowState = WindowState.FullScreen;
                    else if (window.WindowState == WindowState.FullScreen) window.WindowState = WindowState.Normal;
                });

                // F11 and the system change the window too, and the setting follows. Minimizing
                // says nothing either way: the window comes back as it was.
                window.PropertyChanged += (_, e) =>
                {
                    if (e.Property != Window.WindowStateProperty || window.WindowState == WindowState.Minimized) return;

                    windowMode.FullScreen = window.WindowState == WindowState.FullScreen;
                };
                break;

            // Called once per activity, and Android can make more than one; see Shells.
            case IActivityApplicationLifetime activity:
                activity.MainViewFactory = shells.Fresh;
                break;

            case ISingleViewApplicationLifetime single:
                single.MainView = shells.Current;
                break;
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Everything the app needs, the head's answers first. Public for a head whose process can
    /// start without a screen — Android's — to hand to <see cref="HostServices.Build"/>.
    /// </summary>
    public static IServiceProvider BuildServices()
    {
        var services = new ServiceCollection();

        // The head's answers win: everything below is TryAdd or does not overlap.
        Platform?.Invoke(services);

        services.AddSingleton<Router>();
        services.AddSingleton<INavigator>(provider => provider.GetRequiredService<Router>());
        services.AddSingleton<Shells>();
        services.TryAddSingleton<IMainThread, AvaloniaMainThread>();
        services.TryAddSingleton<ISystemActions, AvaloniaSystemActions>();
        services.TryAddSingleton<IImageDecoder, SkiaImageDecoder>();
        services.TryAddSingleton<IAccelerometer, NoAccelerometer>();
        services.TryAddSingleton<IQrScanner, ImageQrScanner>();
        services.TryAddSingleton<IKeepAwake, NoKeepAwake>();

        services.AddCompanionCore();

        return services.BuildServiceProvider();
    }
}
