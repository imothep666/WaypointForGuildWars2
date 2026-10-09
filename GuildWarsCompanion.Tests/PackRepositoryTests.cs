using GuildWarsCompanion.Services;
using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// Reading a marker pack repository. The entries are excerpts of the real lists, fetched on
/// 2026-10-06: Blish HUD's mp-repo and TacO's MarkerPacks.json.
/// </summary>
public class PackRepositoryTests
{
    private const string Blish = """
        [
          {
            "Name": "Metal Marker Myriad (MMM)",
            "Description": "Chests, jumping puzzles and more.",
            "Download": "https://github.com/Metallis/Metal-Marker-Myriad/releases/latest/download/Metal-Marker-Myriad.taco",
            "Categories": "Jumping Puzzles, Achievements",
            "Size": 6.773546,
            "AuthorName": "Metallis",
            "LastUpdate": "2025-06-07T17:00:11",
            "MapIds": [15, 17, 19]
          },
          {
            "Name": "ReActif EN",
            "Download": "https://www.heinze.fr/taco/download.php?f=3",
            "Size": 0.0,
            "LastUpdate": "0001-01-01T00:00:00",
            "MapIds": []
          },
          {
            "Name": "The Tryhard Marker Pack",
            "Download": "http://cap.blishhud.com/DeroirsTryhardMarkers.zip",
            "Size": 1.7306452
          },
          {
            "Name": "[fast] TacO Markers",
            "Download": "https://fast.farming-community.eu/assets/downloads/fast_TacO_pack.taco",
            "LastUpdate": "2024-08-27T13:15:17.1130223Z"
          }
        ]
        """;

    private const string TacO = """
        {
          "markerpacks":
          [
            {
              "name": "Lady Elyssa's Guides",
              "id": "LadyElyssa",
              "downloadurl": "https://github.com/LadyElyssa/LadyElyssaTacoTrails/releases/latest/download/LadyElyssa.taco",
              "enabledbydefault": false
            }
          ]
        }
        """;

    [Fact]
    public void ReadsBlishHudsListing()
    {
        var metal = PackRepository.Parse(Blish).Packs.Single(p => p.Name.StartsWith("Metal"));

        Assert.Equal("Metallis", metal.Author);
        Assert.Equal(["Jumping Puzzles", "Achievements"], metal.Categories);
        Assert.Equal(6.77, metal.SizeMb!.Value, 2);
        Assert.Equal(new DateTimeOffset(2025, 6, 7, 17, 0, 11, TimeSpan.Zero), metal.Updated);
        Assert.Equal(3, metal.Maps);
    }

    [Fact]
    public void LeavesOutPlainHttpAndSaysSo()
    {
        var catalogue = PackRepository.Parse(Blish);

        Assert.DoesNotContain(catalogue.Packs, p => p.Name.StartsWith("The Tryhard"));
        Assert.Equal(1, catalogue.Skipped);
    }

    [Fact]
    public void AZeroSizeAndTheYearOneAreUnknown()
    {
        var reactif = PackRepository.Parse(Blish).Packs.Single(p => p.Name == "ReActif EN");

        Assert.Null(reactif.SizeMb);
        Assert.Null(reactif.Updated);
    }

    [Fact]
    public void ReadsTacOsOwnList()
    {
        var pack = Assert.Single(PackRepository.Parse(TacO).Packs);

        Assert.Equal("Lady Elyssa's Guides", pack.Name);
        Assert.EndsWith("LadyElyssa.taco", pack.Download);
        Assert.Null(pack.SizeMb);
    }

    [Fact]
    public void ADescriptionIsOneRunOfPlainText()
    {
        const string json = """
            [{ "Name": "uL", "Download": "https://x.example/p.taco",
               "Description": "quality‑of‑life tweaks.\r\n\r\n[RACE] Discord Link:  here" }]
            """;

        Assert.Equal("quality-of-life tweaks. [RACE] Discord Link: here",
            PackRepository.Parse(json).Packs[0].Description);
    }

    [Fact]
    public void SomethingElseIsNoList() =>
        Assert.Empty(PackRepository.Parse("""{ "error": "not found" }""").Packs);

    [Fact]
    public void KeepsTheListsOwnOrder() =>
        Assert.Equal(["Metal Marker Myriad (MMM)", "ReActif EN", "[fast] TacO Markers"],
            PackRepository.Parse(Blish).Packs.Select(p => p.Name));
}
