using GuildWarsCompanion.Gw2;

namespace GuildWarsCompanion.Services;

/// <summary>
/// Whether the Wizard's Vault has a reward waiting to be claimed, for the dot on the vault's entry
/// in the bar.
///
/// Fed by whatever reads a track — Today at start-up, the vault screen when it is opened — so the
/// answer is only ever as fresh as the last read, and costs no request of its own. A track left
/// over from before a reset says nothing: the account has not turned over, and what it lists can
/// no longer be claimed as listed.
/// </summary>
public sealed class VaultSignal
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, bool> _waiting = [];

    /// <summary>Raised from whichever thread read the track, when <see cref="Waiting"/> changes.</summary>
    public event Action? Changed;

    public bool Waiting
    {
        get
        {
            lock (_gate) return _waiting.Values.Any(waiting => waiting);
        }
    }

    /// <summary>One track as just read, and whether it is a cycle the account has not left yet.</summary>
    public void Saw(string track, VaultTrack data, bool stale)
    {
        var waiting = !stale && (data.IsMetaUnclaimed || data.Objectives.Any(o => o.IsUnclaimedCompletion));

        bool before, after;

        lock (_gate)
        {
            before = _waiting.Values.Any(w => w);
            _waiting[track] = waiting;
            after = _waiting.Values.Any(w => w);
        }

        if (before != after) Changed?.Invoke();
    }
}
