using System.IO.Compression;
using System.Net.Http.Json;
using System.Security.Cryptography;

namespace GuildWarsCompanion.Bridge.Protocol;

/// <summary>A Blish HUD on disk, ready to start.</summary>
public sealed record InstalledBlish(Version Version, string Exe);

/// <summary>
/// Keeping a copy of Blish HUD that the bridge owns: downloaded from its GitHub releases, and
/// replaced when a newer one is published.
///
/// Blish HUD ships as a zip and nothing else, so "installing" it is unpacking it somewhere. Each
/// version gets its own folder under <see cref="DefaultRoot"/> — 1.3.0, 1.4.0 — rather than one
/// folder overwritten in place, because an update can arrive while the old one is running and
/// Windows will not replace an executable that is in use. The new folder is made beside it,
/// the next start uses it, and the old one is removed once nothing is running from it.
///
/// <b>Nothing of the user's lives in here.</b> Blish HUD keeps settings and modules in
/// Documents\Guild Wars 2\addons\blishhud unless a Settings folder sits beside the exe, and a
/// freshly unpacked release has none. Deleting an old version folder deletes a program, never
/// somebody's module setup.
/// </summary>
public sealed class BlishHudInstall(string root, HttpClient? http = null)
{
    public const string LatestUrl = "https://api.github.com/repos/blish-hud/Blish-HUD/releases/latest";

    public static string DefaultRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GuildWarsCompanion", "BlishHud");

    /// <summary>
    /// Its own client, not the map lookup's. That one gives up after ten seconds, which is right
    /// for a map name and wrong for sixteen megabytes on a slow line. GitHub refuses API calls
    /// without a User-Agent, so every request carries one.
    /// </summary>
    private static readonly Lazy<HttpClient> Shared = new(() =>
    {
        var client = new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        })
        {
            Timeout = TimeSpan.FromMinutes(5)
        };

        client.DefaultRequestHeaders.UserAgent.ParseAdd("GuildWarsCompanion-Bridge");

        return client;
    });

    private HttpClient Http => http ?? Shared.Value;

    public string Root => root;

    /// <summary>The newest complete version on disk, or null if there is none yet.</summary>
    public InstalledBlish? Current()
    {
        if (!Directory.Exists(root)) return null;

        return Directory.EnumerateDirectories(root)
            .Select(dir => (dir, version: Version.TryParse(Path.GetFileName(dir), out var v) ? v : null))
            .Where(x => x.version is not null && File.Exists(Path.Combine(x.dir, BlishHud.ExeName)))
            .OrderByDescending(x => x.version)
            .Select(x => new InstalledBlish(x.version!, Path.Combine(x.dir, BlishHud.ExeName)))
            .FirstOrDefault();
    }

    /// <summary>What GitHub says is latest, or null if that release is not one to install.</summary>
    public async Task<BlishRelease?> LatestAsync(CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestUrl);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");

        using var answer = await Http.SendAsync(request, ct).ConfigureAwait(false);
        answer.EnsureSuccessStatusCode();

        return BlishRelease.From(await answer.Content
            .ReadFromJsonAsync(GitHubJson.Default.GitHubRelease, ct)
            .ConfigureAwait(false));
    }

    /// <summary>
    /// Brings the install up to the latest release, and returns what is on disk afterwards.
    ///
    /// Throws when GitHub cannot be reached or a download is bad. The caller still has
    /// <see cref="Current"/> in that case, and an offline evening should play on the version
    /// already here rather than on nothing.
    /// </summary>
    /// <param name="running">Whether a Blish HUD is running, asked before old versions are
    /// removed.</param>
    /// <param name="say">What is happening, for the window. Called from whatever thread this
    /// is on.</param>
    public async Task<InstalledBlish?> UpdateAsync(
        Func<bool> running, Action<string>? say = null, CancellationToken ct = default)
    {
        var release = await LatestAsync(ct).ConfigureAwait(false);
        var current = Current();

        if (release is null || current is not null && current.Version >= release.Version)
            return current;

        say?.Invoke($"Downloading Blish HUD {release.Version}…");

        var installed = await InstallAsync(release, ct).ConfigureAwait(false);

        if (!running()) Prune(installed);

        return installed;
    }

    /// <summary>
    /// Downloads, checks and unpacks one release.
    ///
    /// Written to a .partial name and moved into place only once complete, so a download cut
    /// off halfway — a closed lid, a dropped connection — is never mistaken for a version by
    /// <see cref="Current"/>. The hash is taken while the bytes stream to disk, and checked before
    /// a single file is unpacked.
    /// </summary>
    public async Task<InstalledBlish> InstallAsync(BlishRelease release, CancellationToken ct = default)
    {
        var target = Path.Combine(root, release.Version.ToString());
        var exe = Path.Combine(target, BlishHud.ExeName);

        if (File.Exists(exe)) return new InstalledBlish(release.Version, exe);

        Directory.CreateDirectory(root);

        var zip = Path.Combine(root, $"{release.Version}.zip.partial");
        var staging = Path.Combine(root, $"{release.Version}.partial");

        try
        {
            await DownloadAsync(release, zip, ct).ConfigureAwait(false);

            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);

            // ExtractToDirectory refuses entries that would land outside the folder, so a zip
            // with ..\ in its names cannot write anywhere else on the disk.
            ZipFile.ExtractToDirectory(zip, staging);

            if (!File.Exists(Path.Combine(staging, BlishHud.ExeName)))
                throw new InvalidDataException(
                    $"The Blish HUD {release.Version} download has no {BlishHud.ExeName} in it.");

            if (Directory.Exists(target)) Directory.Delete(target, recursive: true);

            Directory.Move(staging, target);

            return new InstalledBlish(release.Version, exe);
        }
        finally
        {
            TryDelete(zip);

            if (Directory.Exists(staging)) TryDelete(staging);
        }
    }

    private async Task DownloadAsync(BlishRelease release, string zip, CancellationToken ct)
    {
        using var answer = await Http
            .GetAsync(release.Zip, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);

        answer.EnsureSuccessStatusCode();

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        await using (var from = await answer.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
        await using (var to = File.Create(zip))
        {
            var buffer = new byte[81920];
            int read;

            while ((read = await from.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                hash.AppendData(buffer, 0, read);
                await to.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            }
        }

        var got = Convert.ToHexStringLower(hash.GetHashAndReset());

        if (got != release.Sha256)
            throw new InvalidDataException(
                $"The Blish HUD {release.Version} download does not match the checksum GitHub " +
                "published for it, so it was thrown away rather than run.");
    }

    /// <summary>
    /// Removes every version but <paramref name="keep"/>, and anything left half-made.
    /// Best effort: a folder still in use stays until the next update tries again.
    /// </summary>
    public void Prune(InstalledBlish keep)
    {
        var kept = Path.GetDirectoryName(keep.Exe);

        foreach (var dir in Directory.EnumerateDirectories(root))
            if (!string.Equals(dir, kept, StringComparison.OrdinalIgnoreCase))
                TryDelete(dir);

        foreach (var file in Directory.EnumerateFiles(root, "*.partial"))
            TryDelete(file);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            else if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception)
        {
            // In use, or already gone. Neither is worth failing an update over.
        }
    }
}
