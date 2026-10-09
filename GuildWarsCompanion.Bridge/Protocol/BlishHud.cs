using System.Diagnostics;
using System.Text.Json.Serialization;

namespace GuildWarsCompanion.Bridge.Protocol;

/// <summary>
/// Starting Blish HUD alongside the game, so the bridge is the only thing somebody launches.
///
/// <b>Started, never hosted.</b> Blish HUD is net472 and MonoGame, and its modules are built
/// against its own runtime; none of that loads into this process. Starting its executable is the
/// whole of the integration, and that is deliberate — it keeps official Blish HUD and every module
/// working exactly as they do when started by hand.
///
/// <b>Windows only.</b> Under Proton, Blish HUD runs inside the game's Wine prefix, and starting
/// something in there from a native Linux process is a different problem with no single answer.
///
/// Where it comes from is <see cref="BlishHudInstall"/>; this is only when and how it is started.
/// </summary>
public static class BlishHud
{
    /// <summary>What the release zip calls it, and so what the process is called.</summary>
    public const string ExeName = "Blish HUD.exe";

    private static string ProcessName => Path.GetFileNameWithoutExtension(ExeName);

    public static bool IsSupported => OperatingSystem.IsWindows();

    /// <summary>
    /// Whether this poll is the first one of a game session not yet handled.
    ///
    /// A session is one game process, not one stretch of a moving tick. Character select and a
    /// long loading screen both stall the tick past <see cref="Liveness.Window"/>, so going by
    /// live-again would start Blish HUD after every relog — including for somebody who closed it
    /// on purpose, who would then be arguing with their own bridge all evening.
    /// </summary>
    /// <param name="handled">The process id of the last session acted on, or zero.</param>
    /// <param name="processId">The one MumbleLink reports now. Zero means the game did not say,
    /// and a session that cannot be told apart from the next is not acted on at all.</param>
    public static bool IsNewSession(uint handled, uint processId, bool isLive) =>
        isLive && processId != 0 && processId != handled;

    /// <param name="running">Asked last, and only if everything else says yes: it enumerates
    /// processes.</param>
    public static bool ShouldStart(bool enabled, string? path, Func<bool> running) =>
        enabled && !string.IsNullOrWhiteSpace(path) && !running();

    /// <summary>
    /// Any Blish HUD, wherever it was started from. One somebody extracted and started by hand
    /// counts: two overlays drawing over the same game is never what anybody wanted.
    /// </summary>
    public static bool IsRunning()
    {
        var found = Process.GetProcessesByName(ProcessName);

        foreach (var process in found) process.Dispose();

        return found.Length > 0;
    }

    /// <summary>What the game's process is called: the 64-bit client, the old 32-bit one, and
    /// the Chinese client. The same three Blish HUD itself looks for.</summary>
    public static readonly string[] GameProcessNames = ["Gw2-64", "Gw2", "KZW"];

    /// <summary>
    /// Whether the game process MumbleLink named is still there.
    ///
    /// <b>This, not the tick, is what "the game closed" means.</b> The tick also stops at
    /// character select and on a long loading screen, and closing Blish HUD every time somebody
    /// swapped character would be worse than never closing it. MumbleLink still supplies the
    /// answer to "which process is the game"; the operating system answers whether it is alive.
    ///
    /// The name is checked as well as the id, because ids are reused: seconds after the game
    /// exits, the same number can belong to something else entirely.
    /// </summary>
    /// <param name="nameOf">The name of the live process with this id, or null if there is
    /// none. Injectable so the rule can be tested without a game.</param>
    public static bool IsGameRunning(uint processId, Func<int, string?>? nameOf = null)
    {
        if (processId == 0) return false;

        var name = (nameOf ?? NameOf)((int)processId);

        return name is not null &&
               GameProcessNames.Contains(name, StringComparer.OrdinalIgnoreCase);
    }

    private static string? NameOf(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);

            return process.HasExited ? null : process.ProcessName;
        }
        catch (ArgumentException)
        {
            // No process with that id: exactly the answer being asked for.
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // There, but not ours to look at — an elevated game. Alive, as far as can be told,
            // and "don't know" must never read as "closed".
            return GameProcessNames[0];
        }
    }

    /// <summary>Whether a file lies inside a folder, compared the way Windows compares paths.</summary>
    public static bool IsUnder(string file, string root)
    {
        var folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;

        return Path.GetFullPath(file).StartsWith(folder, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Closes every Blish HUD running out of <paramref name="root"/>, and only those.
    ///
    /// By folder rather than by the process that was started, for two reasons. One somebody
    /// started by hand from their own folder is theirs, and is left alone. And Blish HUD set to
    /// stay in the tray does not simply wait when the game exits — it restarts itself, as a new
    /// process, which a handle to the one the bridge started would never see.
    ///
    /// Politely first: asked to close, given a few seconds, and only then ended. Returns how
    /// many were closed, and the first reason one could not be.
    /// </summary>
    public static async Task<(int Closed, string? Problem)> CloseFromAsync(string root, TimeSpan grace)
    {
        var closed = 0;
        string? problem = null;

        foreach (var process in Process.GetProcessesByName(ProcessName))
        {
            using (process)
            {
                try
                {
                    if (process.MainModule?.FileName is not { } file || !IsUnder(file, root)) continue;

                    process.CloseMainWindow();

                    using var wait = new CancellationTokenSource(grace);

                    try
                    {
                        await process.WaitForExitAsync(wait.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        // Minimised to the tray has no window to close, and a hung one will not
                        // answer. Either way, the game is gone and so is the reason to be here.
                        process.Kill();
                    }

                    closed++;
                }
                catch (Exception ex)
                {
                    // Elevated, or already gone between listing and closing.
                    problem ??= ex.Message;
                }
            }
        }

        return (closed, problem);
    }

    /// <summary>
    /// Starts it, and says why not if it could not be started.
    ///
    /// Shell execute, so a Blish HUD that has to run elevated — because the game does — gets a
    /// UAC prompt rather than failing with ERROR_ELEVATION_REQUIRED. Its own folder as the working
    /// directory, because Blish HUD decides where its settings live from there: a Settings folder
    /// beside the exe means portable, and its absence means Documents.
    /// </summary>
    /// <param name="module">
    /// A .bhm for Blish HUD to load on top of whatever it has installed — the bridge's own
    /// module. Passed on the command line, which Blish HUD offers for exactly this, so nothing is
    /// copied into its folders: start Blish HUD by hand and the module is simply not there.
    /// </param>
    public static string? Start(string path, string? module = null)
    {
        try
        {
            using var _ = Process.Start(new ProcessStartInfo(path, StartArguments(module))
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(path) ?? string.Empty
            });

            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    /// <summary>
    /// "--module "C:\…\GuildWarsCompanion.Blish.bhm"", or nothing. Quoted, because the path
    /// sits under a user folder and user folders have spaces in them.
    /// </summary>
    public static string StartArguments(string? module) =>
        string.IsNullOrWhiteSpace(module) ? string.Empty : $"--module \"{module}\"";
}

/// <summary>The latest release, as far as installing it goes.</summary>
/// <param name="Sha256">Lower-case hex, from the asset's digest. Never null: a release that
/// does not publish one is not installed at all, see <see cref="BlishRelease.From"/>.</param>
public sealed record BlishRelease(string Tag, Version Version, Uri Zip, string Sha256)
{
    /// <summary>
    /// The release worth installing out of /releases/latest, or null if there is none.
    ///
    /// <b>The digest is required.</b> This downloads a program and runs it, and the digest
    /// GitHub computes on upload is the only check there is that the bytes that arrived are the
    /// bytes that were published. Releases before mid-2025 have none — GitHub did not compute
    /// them yet — and are refused rather than trusted; every release since has one.
    ///
    /// The asset is found by shape rather than name, because the name is not stable: v1.2.0 was
    /// Blish.HUD.1.2.0.zip and v1.3.0 is Blish.HUD.zip.
    /// </summary>
    public static BlishRelease? From(GitHubRelease? release)
    {
        if (release is null || release.Draft || release.Prerelease) return null;

        if (VersionOf(release.Tag) is not { } version) return null;

        var asset = release.Assets?.FirstOrDefault(a =>
            a.Name.StartsWith("Blish.HUD", StringComparison.OrdinalIgnoreCase) &&
            a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));

        if (asset is null) return null;

        const string prefix = "sha256:";

        if (asset.Digest is not { } digest ||
            !digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            digest.Length != prefix.Length + 64)
            return null;

        // GitHub's own host, over HTTPS. The digest is the real check; this keeps a release
        // object that somehow pointed elsewhere from even being asked for.
        if (!Uri.TryCreate(asset.Url, UriKind.Absolute, out var zip) ||
            zip.Scheme != Uri.UriSchemeHttps || zip.Host != "github.com")
            return null;

        return new BlishRelease(release.Tag, version, zip, digest[prefix.Length..].ToLowerInvariant());
    }

    /// <summary>"v1.3.0" → 1.3.0. Null for anything that is not a plain version.</summary>
    public static Version? VersionOf(string? tag) =>
        Version.TryParse(tag?.TrimStart('v', 'V'), out var version) ? version : null;
}

/// <summary>The part of GitHub's release object this reads.</summary>
public sealed record GitHubRelease(
    [property: JsonPropertyName("tag_name")] string Tag,
    [property: JsonPropertyName("draft")] bool Draft,
    [property: JsonPropertyName("prerelease")] bool Prerelease,
    [property: JsonPropertyName("assets")] IReadOnlyList<GitHubAsset>? Assets);

public sealed record GitHubAsset(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("browser_download_url")] string Url,
    [property: JsonPropertyName("digest")] string? Digest);

[JsonSerializable(typeof(GitHubRelease))]
internal partial class GitHubJson : JsonSerializerContext;
