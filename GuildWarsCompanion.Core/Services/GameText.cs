using GuildWarsCompanion.Bridge.Protocol;

namespace GuildWarsCompanion.Services;

/// <summary>
/// What the bridge says about the game, in the phone's language.
///
/// The bridge writes its report in English: it is compiled from the same files as this app but
/// has no translations, and it does not know what language any phone is in. Its notes are a
/// handful of fixed sentences and its professions a fixed list, so the phone translates them as
/// it shows them. A note it does not recognise, a newer bridge's, stays English.
/// </summary>
public static class GameText
{
    /// <summary>"Imothep · Waldläufer" for "Imothep · Ranger".</summary>
    public static string Line(GameState state)
    {
        var profession = L.T(state.Profession);

        return state.CharacterName.Length == 0 ? profession : $"{state.CharacterName} · {profession}";
    }

    /// <summary>The report's note, such as "Guild Wars 2 is not running", translated.</summary>
    public static string Note(GameReport report) => L.T(report.Note);
}
