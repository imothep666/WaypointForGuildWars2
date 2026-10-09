using System.Reflection;

using CommunityToolkit.Mvvm.ComponentModel;

using GuildWarsCompanion.Bridge.Protocol;
using GuildWarsCompanion.Host;

using Velopack;
using Velopack.Sources;

namespace GuildWarsCompanion.Ava.Desktop;

/// <summary>
/// Updates from the GitHub releases the pipeline publishes (see azure-pipelines.yml).
///
/// Quiet until there is something to say: it checks a minute after start and then every six
/// hours, downloads in the background, and only then shows itself — a button in the title bar.
/// An update that is never restarted for is applied on the next start anyway, which Velopack does
/// by default before the app is up.
///
/// The releases address is baked in at build time (WaypointReleases in the project file), so a
/// local build has none and does not update. Neither does a build that was not installed by
/// Velopack: a folder from dotnet publish or packaging/linux/build.ps1 has nothing to swap.
/// </summary>
public sealed partial class VelopackUpdates : ObservableObject, IAppUpdates
{
    private static readonly TimeSpan FirstCheck = TimeSpan.FromMinutes(1);

    /// <summary>A release is a commit on main, so several a day are possible; four checks a day
    /// stay far inside GitHub's sixty unauthenticated requests an hour.</summary>
    private static readonly TimeSpan Every = TimeSpan.FromHours(6);

    private readonly UpdateManager? _manager;
    private readonly IMainThread _mainThread;
    private readonly Journal _journal;
    private readonly string[] _args;

    private VelopackAsset? _ready;
    private Timer? _timer;
    private int _checking;

    [ObservableProperty] private string _status = string.Empty;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isReady;

    public VelopackUpdates(IMainThread mainThread, Journal journal, string[] args)
    {
        _mainThread = mainThread;
        _journal = journal;
        _args = args;

        var releases = typeof(VelopackUpdates).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "WaypointReleases")?.Value;

        if (string.IsNullOrWhiteSpace(releases)) return;

        try
        {
            // GitHub's releases in practice; any other address is a plain feed — a web folder,
            // or a local one, which is how an update is tried out without publishing it.
            var manager = releases.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase)
                ? new UpdateManager(new GithubSource(releases, accessToken: null, prerelease: false))
                : new UpdateManager(releases);
            if (manager.IsInstalled) _manager = manager;
        }
        catch (Exception e)
        {
            journal.Write("update", $"no updater: {e.Message}");
        }

        if (_manager is not null)
            _status = $"Waypoint {_manager.CurrentVersion}. Looks for updates a minute after start, then every six hours.";
    }

    public bool IsSupported => _manager is not null;

    public void Start()
    {
        if (_manager is null || _timer is not null) return;

        // An update downloaded in an earlier run and never applied — the start-up apply did not
        // happen, or failed — still only needs a restart.
        if (_manager.UpdatePendingRestart is { } pending) Ready(pending);

        _timer = new Timer(_ => _ = CheckAsync(), null, FirstCheck, Every);
    }

    public async Task CheckAsync()
    {
        if (_manager is null || _ready is not null) return;

        // The timer and the button can both ask; one check at a time.
        if (Interlocked.Exchange(ref _checking, 1) == 1) return;

        try
        {
            await Show(busy: true, "Looking for an update…");

            var update = await _manager.CheckForUpdatesAsync();
            if (update is null)
            {
                await Show(busy: false, $"Up to date: {_manager.CurrentVersion}, checked {DateTime.Now:HH:mm}.");
                return;
            }

            var version = update.TargetFullRelease.Version;
            _journal.Write("update", $"{version} found, downloading");
            await Show(busy: true, $"Downloading {version}…");

            await _manager.DownloadUpdatesAsync(update, progress =>
                _mainThread.BeginInvokeOnMainThread(() => Status = $"Downloading {version}… {progress}%"));

            _journal.Write("update", $"{version} downloaded");
            Ready(update.TargetFullRelease);
        }
        catch (Exception e)
        {
            // Offline is the usual reason, and the next check is only hours away.
            _journal.Write("update", $"check failed: {e.Message}");
            await Show(busy: false, $"Could not check for an update ({e.Message}). It tries again later.");
        }
        finally
        {
            Interlocked.Exchange(ref _checking, 0);
        }
    }

    public void RestartToUpdate()
    {
        if (_manager is null || _ready is null) return;

        _journal.Write("update", $"restarting into {_ready.Version}");

        // The arguments this run had, so a --software start comes back as one.
        _manager.ApplyUpdatesAndRestart(_ready, _args);
    }

    private void Ready(VelopackAsset asset)
    {
        _ready = asset;
        _mainThread.BeginInvokeOnMainThread(() =>
        {
            IsBusy = false;
            IsReady = true;
            Status = $"Waypoint {asset.Version} is ready. Restart to use it, or it is applied on the next start.";
        });
    }

    private Task Show(bool busy, string status) => _mainThread.InvokeOnMainThreadAsync(() =>
    {
        IsBusy = busy;
        Status = status;
    });
}
