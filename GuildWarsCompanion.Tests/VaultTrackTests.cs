using System.Text.Json;
using GuildWarsCompanion.Gw2;
using GuildWarsCompanion.Services;
using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// Objectives that count to nothing. The Special track reported "Tenebral Ward" as 0 of 0 and
/// unclaimed, which lit the vault's dot for a reward the game does not have.
/// </summary>
public class VaultTrackTests
{
    private const string Special = """
        {
          "meta_progress_current": 0,
          "meta_progress_complete": 0,
          "meta_reward_astral": 0,
          "meta_reward_claimed": false,
          "objectives": [
            { "id": 1, "title": "Tenebral Ward", "track": "Special", "acclaim": 0,
              "progress_current": 0, "progress_complete": 0, "claimed": false },
            { "id": 2, "title": "Defeat a world boss", "track": "Special", "acclaim": 50,
              "progress_current": 0, "progress_complete": 1, "claimed": false }
          ]
        }
        """;

    private static VaultTrack Read(string json) =>
        JsonSerializer.Deserialize(json, Gw2JsonContext.Default.VaultTrack)!;

    [Fact]
    public void An_objective_that_counts_to_zero_is_left_out()
    {
        var track = Read(Special);

        Assert.Equal(["Defeat a world boss"], track.Objectives.Select(o => o.Title));
    }

    [Fact]
    public void A_meta_that_counts_to_zero_has_nothing_to_claim()
    {
        Assert.False(Read(Special).IsMetaUnclaimed);
    }

    [Fact]
    public void The_vault_dot_stays_dark_for_a_zero_of_zero_objective()
    {
        var signal = new VaultSignal();

        signal.Saw("special", Read(Special), stale: false);

        Assert.False(signal.Waiting);
    }

    [Fact]
    public void A_real_completion_still_lights_the_dot()
    {
        var done = Read(Special.Replace(
            "\"progress_current\": 0, \"progress_complete\": 1", "\"progress_current\": 1, \"progress_complete\": 1"));
        var signal = new VaultSignal();

        signal.Saw("special", done, stale: false);

        Assert.True(signal.Waiting);
    }

    [Fact]
    public void A_snapshot_written_back_reads_the_same()
    {
        // Snapshots store the payload re-serialised from this record, so the property has to
        // keep its JSON name.
        var track = Read(Special);
        var again = Read(JsonSerializer.Serialize(track, Gw2JsonContext.Default.VaultTrack));

        Assert.Equal(track.Objectives, again.Objectives);
    }
}
