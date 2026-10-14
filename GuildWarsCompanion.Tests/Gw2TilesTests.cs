using System.Net;

using GuildWarsCompanion.Host;
using GuildWarsCompanion.Services;

using Microsoft.Maui.Graphics;

using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// What the tile service does when a fetch fails.
///
/// A failed tile used to be asked for again only on the next redraw, and a map nobody touched has
/// no next redraw, so a square lost to a bad connection stayed a hole. No sockets here: the
/// handler answers from a script, and the wait before a retry is one the test lets go of.
/// </summary>
public sealed class Gw2TilesTests : IDisposable
{
    private static readonly TileKey Square = new(1, 1, 3, 4, 5);

    private readonly string _cache =
        Path.Combine(Path.GetTempPath(), "gw2tiles-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Removes the test's cache, waiting for a write still under way. The service tells the screen
    /// about a tile before it writes the tile to disk, so a test that ends on that signal can reach
    /// here while the file is still open. Under the whole suite's load that happened now and then,
    /// and the cleanup's "The process cannot access the file" failed a test that had passed.
    /// </summary>
    public void Dispose()
    {
        for (var attempt = 0; attempt < 20 && Directory.Exists(_cache); attempt++)
        {
            try
            {
                Directory.Delete(_cache, recursive: true);
            }
            catch (IOException)
            {
                Thread.Sleep(50);
            }
        }
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 4)]
    [InlineData(3, 8)]
    [InlineData(5, 32)]
    [InlineData(6, 60)]
    [InlineData(40, 60)]
    public void The_wait_doubles_and_stops_at_a_minute(int failures, int seconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(seconds), Gw2Tiles.RetryAfter(failures));
    }

    [Fact]
    public async Task A_tile_lost_to_the_network_is_asked_for_again_after_the_wait()
    {
        var handler = new Script(_ => throw new HttpRequestException("no network"), Picture);
        var wait = new Gate();
        var tiles = Tiles(handler, wait);
        var redrawn = Signal(tiles);

        tiles.Want(Square);
        Assert.Equal(TimeSpan.FromSeconds(2), await wait.Asked);

        // Frames drawn during the wait do not ask again.
        tiles.Want(Square);
        Assert.Equal(1, handler.Requests);

        wait.Release();
        await redrawn.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // The redraw is the frame that asks; this time the service answers.
        var arrived = Signal(tiles);
        tiles.Want(Square);
        await arrived.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(2, handler.Requests);
        Assert.NotNull(tiles.Held(Square));
    }

    [Fact]
    public async Task Bytes_that_are_not_a_picture_are_retried_and_not_cached()
    {
        var handler = new Script(_ => Bytes("<html>log in to the Wi-Fi</html>"u8.ToArray()));
        var wait = new Gate();
        var tiles = Tiles(handler, wait, decodes: false);

        tiles.Want(Square);
        await wait.Asked;

        Assert.Null(tiles.Held(Square));
        Assert.False(File.Exists(Path.Combine(_cache, "tiles", Square.FileName)));
    }

    [Fact]
    public async Task A_square_the_service_never_rendered_is_not_asked_for_again()
    {
        var answered = new TaskCompletionSource();
        var handler = new Script(_ =>
        {
            answered.TrySetResult();

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var wait = new Gate();
        var tiles = Tiles(handler, wait);

        tiles.Want(Square);
        await answered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // The catch runs just after the answer; give it the moment it needs.
        await Task.Delay(100);
        tiles.Want(Square);

        Assert.Equal(1, handler.Requests);
        Assert.False(wait.Asked.IsCompleted);
    }

    private Gw2Tiles Tiles(Script handler, Gate wait, bool decodes = true) =>
        new(new Decoder(decodes), new AppEnvironment(_cache, _cache, "test"), handler, wait.Wait);

    private static TaskCompletionSource Signal(Gw2Tiles tiles)
    {
        var raised = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        void Once()
        {
            tiles.Redraw -= Once;
            raised.TrySetResult();
        }

        tiles.Redraw += Once;

        return raised;
    }

    private static HttpResponseMessage Picture(HttpRequestMessage _) => Bytes([0xFF, 0xD8, 0xFF]);

    private static HttpResponseMessage Bytes(byte[] body) =>
        new(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };

    /// <summary>Answers each request with the next step, repeating the last one.</summary>
    private sealed class Script(params Func<HttpRequestMessage, HttpResponseMessage>[] steps)
        : HttpMessageHandler
    {
        private int _requests;

        public int Requests => Volatile.Read(ref _requests);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var step = Interlocked.Increment(ref _requests) - 1;

            return Task.FromResult(steps[Math.Min(step, steps.Length - 1)](request));
        }
    }

    /// <summary>A retry wait that records how long it was asked for and ends when told to.</summary>
    private sealed class Gate
    {
        private readonly TaskCompletionSource<TimeSpan> _asked =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly TaskCompletionSource _released =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<TimeSpan> Asked => _asked.Task.WaitAsync(TimeSpan.FromSeconds(5));

        public Task Wait(TimeSpan length)
        {
            _asked.TrySetResult(length);

            return _released.Task;
        }

        public void Release() => _released.TrySetResult();
    }

    private sealed class Decoder(bool decodes) : IImageDecoder
    {
        public IImage? Decode(Stream stream) => decodes ? new BlankImage() : null;
    }
}
