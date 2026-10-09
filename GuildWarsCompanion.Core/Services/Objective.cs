namespace GuildWarsCompanion.Services;

/// <summary>
/// What the player is currently walking towards, shared by every screen that has an opinion
/// about it.
///
/// It lived inside the compass while the compass was the only screen that could choose one. The
/// map can now choose too, and a second copy of the answer would mean the two screens could
/// disagree about the one thing they both exist to show — so the objective moved out here and
/// both read it.
///
/// Deliberately not persisted. An objective is what you are doing this minute, not a setting; the
/// screen that outlives it is the one that has gone stale.
/// </summary>
public sealed class Objective
{
    /// <summary>The chosen target, or null when nothing is chosen.</summary>
    public CompassTarget? Chosen { get; private set; }

    /// <summary>
    /// Raised when the objective changes, whoever changed it.
    ///
    /// Listeners must tolerate being told about a choice they made themselves — see
    /// <see cref="Choose"/> for why that cannot loop.
    /// </summary>
    public event Action<CompassTarget?>? Changed;

    /// <summary>
    /// Sets the objective, or clears it with null.
    ///
    /// A no-op when nothing moves, which is what stops two screens bouncing the same choice back
    /// and forth: the second screen assigns the value it was just handed, that assignment matches
    /// what is already stored, and the chain stops there.
    /// </summary>
    public void Choose(CompassTarget? target)
    {
        if (ReferenceEquals(Chosen, target)) return;

        Chosen = target;

        Changed?.Invoke(target);
    }
}
