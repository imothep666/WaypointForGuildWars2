using System.Net;
using System.Text;

using GuildWarsCompanion.Gw2;

using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// A map whose default floor does not hold it.
///
/// Crystal Oasis names floor 1 as its default, and floor 1 answers 404 "no such id" for it; its
/// hearts and waypoints are on floor 49. The compass and the map screen asked only the default
/// floor, and showed no icons on the map at all. No sockets: the handler answers from a script.
/// </summary>
public sealed class MapDetailFloorTests
{
    private const string OnFloor49 =
        """{ "tasks": {}, "points_of_interest": { "1": { "id": 1, "name": "Amnoon Waypoint", "type": "waypoint", "coord": [59000, 43000] } }, "skill_challenges": [] }""";

    private static readonly MapInfo CrystalOasis =
        new(1210, "Crystal Oasis", 12, 1, 1, null, Floors: [1, 3, 49, 2, 0]);

    [Fact]
    public async Task The_other_floors_are_tried_when_the_default_has_no_such_map()
    {
        var asked = new List<string>();
        var client = Client(asked, path => path.Contains("/floors/49/") ? OnFloor49 : null);

        var detail = await client.GetMapDetailAsync(CrystalOasis);

        Assert.NotNull(detail.PointsOfInterest);
        Assert.Contains(detail.PointsOfInterest!.Values, poi => poi.Name == "Amnoon Waypoint");

        // The default first, then the map's own list in order, stopping at the one that answers.
        Assert.Equal(["/floors/1/", "/floors/3/", "/floors/49/"], asked.Select(Floor));
    }

    [Fact]
    public async Task The_default_floor_alone_is_asked_when_it_holds_the_map()
    {
        var asked = new List<string>();
        var client = Client(asked, _ => OnFloor49);

        await client.GetMapDetailAsync(CrystalOasis);

        Assert.Equal(["/floors/1/"], asked.Select(Floor));
    }

    [Fact]
    public async Task A_map_on_no_floor_at_all_still_fails_as_not_found()
    {
        var client = Client([], _ => null);

        var failure = await Assert.ThrowsAsync<Gw2ApiException>(() => client.GetMapDetailAsync(CrystalOasis));

        Assert.Equal(Gw2ErrorKind.NotFound, failure.Kind);
    }

    private static string Floor(string path) => path[path.IndexOf("/floors/", StringComparison.Ordinal)..(path.IndexOf("/regions/", StringComparison.Ordinal) + 1)];

    private static Gw2Client Client(List<string> asked, Func<string, string?> answer) =>
        new(new HttpClient(new Scripted(asked, answer)) { BaseAddress = new Uri("https://api.guildwars2.com/v2/") });

    private sealed class Scripted(List<string> asked, Func<string, string?> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;

            if (path.Contains("/floors/")) asked.Add(path);

            return Task.FromResult(answer(path) is { } json
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") }
                : new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("""{ "text": "no such id" }""", Encoding.UTF8, "application/json")
                });
        }
    }
}
