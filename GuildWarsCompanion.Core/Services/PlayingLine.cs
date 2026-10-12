using GuildWarsCompanion.Bridge.Protocol;

namespace GuildWarsCompanion.Services;

/// <summary>
/// Who is in the game and where, in one line for the top of Today.
///
/// Only a live report says anything. MumbleLink keeps the last character and map after the game
/// closes, so a report that is not live names somebody who logged out last night — and "Imothep
/// in Wayfarer Foothills" above today's list would read as now. No bridge, an unreachable PC and
/// a closed game all give no line: the screen is what it was before a bridge existed.
/// </summary>
public static class PlayingLine
{
    public static string? Describe(GameReport? report) =>
        report is { IsLive: true, State: { CharacterName.Length: > 0 } state }
            ? L.F("{0} · in {1}", GameText.Line(state), report.MapLabel)
            : null;
}
