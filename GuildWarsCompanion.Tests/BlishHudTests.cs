using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;

using GuildWarsCompanion.Bridge.Protocol;

using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// When the bridge starts Blish HUD. The promise pinned here is "once per game, never twice, and
/// never against somebody who closed it" — the process start itself is not tested.
/// </summary>
public class BlishHudTests
{
    private const string Path = @"C:\Games\Blish HUD\Blish HUD.exe";

    private static bool NotRunning() => false;

    [Fact]
    public void A_new_game_process_is_a_new_session()
    {
        Assert.True(BlishHud.IsNewSession(handled: 0, processId: 4242, isLive: true));
        Assert.True(BlishHud.IsNewSession(handled: 4242, processId: 5151, isLive: true));
    }

    [Fact]
    public void The_same_game_live_again_is_not()
    {
        // Character select or a long loading screen: the tick stalled, the game never closed.
        Assert.False(BlishHud.IsNewSession(handled: 4242, processId: 4242, isLive: true));
    }

    [Fact]
    public void Nothing_happens_while_the_game_is_not_live()
    {
        // What a closed game leaves behind in the mapping, with an id nobody has handled.
        Assert.False(BlishHud.IsNewSession(handled: 0, processId: 4242, isLive: false));
    }

    [Fact]
    public void A_game_that_gives_no_process_id_is_never_acted_on()
    {
        Assert.False(BlishHud.IsNewSession(handled: 0, processId: 0, isLive: true));
    }

    [Fact]
    public void It_starts_only_when_asked_and_present_and_not_already_running()
    {
        Assert.True(BlishHud.ShouldStart(enabled: true, Path, NotRunning));

        Assert.False(BlishHud.ShouldStart(enabled: false, Path, NotRunning));
        Assert.False(BlishHud.ShouldStart(enabled: true, path: null, NotRunning));
        Assert.False(BlishHud.ShouldStart(enabled: true, Path, running: () => true));
    }

    [Fact]
    public void The_game_is_running_while_its_process_is()
    {
        Assert.True(BlishHud.IsGameRunning(4242, _ => "Gw2-64"));
        Assert.True(BlishHud.IsGameRunning(4242, _ => "gw2-64"));
        Assert.True(BlishHud.IsGameRunning(4242, _ => "KZW"));
    }

    [Fact]
    public void The_game_has_closed_when_its_process_is_gone()
    {
        Assert.False(BlishHud.IsGameRunning(4242, _ => null));
    }

    [Fact]
    public void A_reused_process_id_is_not_the_game()
    {
        // Seconds after the game exits, Windows can hand its id to anything at all.
        Assert.False(BlishHud.IsGameRunning(4242, _ => "chrome"));
    }

    [Fact]
    public void No_process_id_is_no_game()
    {
        Assert.False(BlishHud.IsGameRunning(0, _ => "Gw2-64"));
    }

    [Theory]
    [InlineData(@"C:\Users\x\AppData\Local\GuildWarsCompanion\BlishHud\1.3.0\Blish HUD.exe", true)]
    [InlineData(@"c:\users\X\appdata\local\guildwarscompanion\blishhud\1.3.0\blish hud.exe", true)]
    [InlineData(@"C:\Games\Blish HUD\Blish HUD.exe", false)]
    // A sibling folder whose name merely starts the same is not inside.
    [InlineData(@"C:\Users\x\AppData\Local\GuildWarsCompanion\BlishHudOld\Blish HUD.exe", false)]
    public void Only_blish_hud_from_the_bridge_folder_is_the_bridges(string file, bool ours)
    {
        if (!OperatingSystem.IsWindows()) return;

        Assert.Equal(ours, BlishHud.IsUnder(file, @"C:\Users\x\AppData\Local\GuildWarsCompanion\BlishHud"));
    }

    [Fact]
    public void Processes_are_not_looked_at_unless_everything_else_says_yes()
    {
        var asked = false;

        BlishHud.ShouldStart(enabled: false, Path, () => asked = true);

        Assert.False(asked);
    }
}

/// <summary>
/// Which GitHub release is installed. The rule that matters most is the refusal: a release with
/// no checksum is never downloaded, because the checksum is the only thing standing between a
/// download and running whatever arrived.
/// </summary>
public class BlishReleaseTests
{
    private const string Digest = "sha256:eb7f3fde8dcb59ea21dae2ffb1ff450c0ab3d5b6f87cea02da14d3710acac68e";

    private static GitHubRelease Release(
        string tag = "v1.3.0",
        string name = "Blish.HUD.zip",
        string url = "https://github.com/blish-hud/Blish-HUD/releases/download/v1.3.0/Blish.HUD.zip",
        string? digest = Digest,
        bool prerelease = false) =>
        new(tag, Draft: false, prerelease, [new GitHubAsset(name, url, digest)]);

    [Fact]
    public void The_current_release_shape_is_read()
    {
        var release = BlishRelease.From(Release())!;

        Assert.Equal(new Version(1, 3, 0), release.Version);
        Assert.Equal("eb7f3fde8dcb59ea21dae2ffb1ff450c0ab3d5b6f87cea02da14d3710acac68e", release.Sha256);
    }

    [Fact]
    public void The_older_asset_name_is_found_too()
    {
        // v1.2.0 was called Blish.HUD.1.2.0.zip; the name is not a stable contract.
        Assert.NotNull(BlishRelease.From(Release(tag: "v1.2.0", name: "Blish.HUD.1.2.0.zip",
            url: "https://github.com/blish-hud/Blish-HUD/releases/download/v1.2.0/Blish.HUD.1.2.0.zip")));
    }

    [Fact]
    public void A_release_without_a_checksum_is_refused()
    {
        Assert.Null(BlishRelease.From(Release(digest: null)));
        Assert.Null(BlishRelease.From(Release(digest: "sha256:tooshort")));
        Assert.Null(BlishRelease.From(Release(digest: "md5:" + new string('0', 64))));
    }

    [Fact]
    public void A_download_anywhere_but_github_over_https_is_refused()
    {
        Assert.Null(BlishRelease.From(Release(url: "http://github.com/x/Blish.HUD.zip")));
        Assert.Null(BlishRelease.From(Release(url: "https://example.com/Blish.HUD.zip")));
    }

    [Fact]
    public void Prereleases_and_odd_tags_are_not_installed()
    {
        Assert.Null(BlishRelease.From(Release(prerelease: true)));
        Assert.Null(BlishRelease.From(Release(tag: "nightly")));
        Assert.Null(BlishRelease.From(null));
    }
}

/// <summary>
/// The install itself, against a fake GitHub. Real zips and real hashes, on a temporary folder:
/// the point is that a bad download never becomes a version that gets started.
/// </summary>
public sealed class BlishHudInstallTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("blish");

    public void Dispose() => _root.Delete(recursive: true);

    private static byte[] Zip(string exeText, bool withExe = true)
    {
        using var buffer = new MemoryStream();

        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            if (withExe)
            {
                using var exe = new StreamWriter(zip.CreateEntry(BlishHud.ExeName).Open());
                exe.Write(exeText);
            }

            using var content = new StreamWriter(zip.CreateEntry("Content/common/error.xnb").Open());
            content.Write("art");
        }

        return buffer.ToArray();
    }

    private static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    /// <summary>GitHub, as far as this needs it: one latest release and its one zip.</summary>
    private sealed class FakeGitHub(string tag, byte[] zip, string? digest) : HttpMessageHandler
    {
        public int Downloads { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.AbsoluteUri == BlishHudInstall.LatestUrl)
            {
                var json = $$"""
                    {"tag_name":"{{tag}}","draft":false,"prerelease":false,"assets":[
                      {"name":"Blish.HUD.zip",
                       "browser_download_url":"https://github.com/blish-hud/Blish-HUD/releases/download/{{tag}}/Blish.HUD.zip",
                       "digest":"sha256:{{digest}}"}]}
                    """;

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                });
            }

            Downloads++;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(zip)
            });
        }
    }

    private BlishHudInstall Install(FakeGitHub github) =>
        new(_root.FullName, new HttpClient(github));

    [Fact]
    public async Task A_good_download_becomes_the_current_version()
    {
        var zip = Zip("1.3.0");
        var install = Install(new FakeGitHub("v1.3.0", zip, Sha(zip)));

        var installed = await install.UpdateAsync(() => false);

        Assert.NotNull(installed);
        Assert.Equal(new Version(1, 3, 0), installed.Version);
        Assert.Equal("1.3.0", await File.ReadAllTextAsync(installed.Exe));
        Assert.Equal(installed, install.Current());

        // Nothing half-made left beside it.
        Assert.Empty(Directory.GetFiles(_root.FullName));
    }

    [Fact]
    public async Task A_download_that_does_not_match_its_checksum_is_thrown_away()
    {
        var zip = Zip("tampered");
        var install = Install(new FakeGitHub("v1.3.0", zip, Sha(Zip("genuine"))));

        await Assert.ThrowsAsync<InvalidDataException>(() => install.UpdateAsync(() => false));

        Assert.Null(install.Current());
        Assert.Empty(_root.GetFileSystemInfos());
    }

    [Fact]
    public async Task A_zip_without_the_exe_is_not_a_version()
    {
        var zip = Zip("", withExe: false);
        var install = Install(new FakeGitHub("v1.3.0", zip, Sha(zip)));

        await Assert.ThrowsAsync<InvalidDataException>(() => install.UpdateAsync(() => false));

        Assert.Null(install.Current());
    }

    [Fact]
    public async Task Up_to_date_downloads_nothing()
    {
        var zip = Zip("1.3.0");
        var github = new FakeGitHub("v1.3.0", zip, Sha(zip));
        var install = Install(github);

        await install.UpdateAsync(() => false);
        await install.UpdateAsync(() => false);

        Assert.Equal(1, github.Downloads);
    }

    [Fact]
    public async Task A_newer_release_replaces_the_old_one_when_nothing_is_running()
    {
        var old = Zip("1.2.0");
        await Install(new FakeGitHub("v1.2.0", old, Sha(old))).UpdateAsync(() => false);

        var zip = Zip("1.3.0");
        var installed = await Install(new FakeGitHub("v1.3.0", zip, Sha(zip))).UpdateAsync(() => false);

        Assert.Equal(new Version(1, 3, 0), installed!.Version);
        Assert.Equal(["1.3.0"], _root.GetDirectories().Select(d => d.Name));
    }

    [Fact]
    public async Task The_old_version_is_kept_while_blish_hud_is_running()
    {
        var old = Zip("1.2.0");
        await Install(new FakeGitHub("v1.2.0", old, Sha(old))).UpdateAsync(() => false);

        var zip = Zip("1.3.0");
        var install = Install(new FakeGitHub("v1.3.0", zip, Sha(zip)));
        await install.UpdateAsync(running: () => true);

        // Both on disk; the newer one is what starts next.
        Assert.Equal(2, _root.GetDirectories().Length);
        Assert.Equal(new Version(1, 3, 0), install.Current()!.Version);
    }

    [Fact]
    public void Versions_are_ordered_as_versions_not_as_text()
    {
        foreach (var version in new[] { "1.9.0", "1.10.0" })
        {
            var dir = Directory.CreateDirectory(System.IO.Path.Combine(_root.FullName, version));
            File.WriteAllText(System.IO.Path.Combine(dir.FullName, BlishHud.ExeName), version);
        }

        // Half-made and foreign folders are never a version.
        Directory.CreateDirectory(System.IO.Path.Combine(_root.FullName, "2.0.0.partial"));
        Directory.CreateDirectory(System.IO.Path.Combine(_root.FullName, "3.0.0"));

        Assert.Equal(new Version(1, 10, 0), new BlishHudInstall(_root.FullName).Current()!.Version);
    }
}
