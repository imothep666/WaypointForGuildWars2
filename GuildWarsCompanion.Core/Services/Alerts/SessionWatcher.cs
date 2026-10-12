using GuildWarsCompanion.Gw2;

namespace GuildWarsCompanion.Services.Alerts;

/// <summary>
/// Keeps the ledger on the background poll, and sends a recap once a session has ended.
///
/// "Ended" is the first point that sees no playtime rise after points that did, so the recap comes
/// half an hour or so after logging out — the price of a poll every thirty minutes, and the right
/// side to be late on: a recap of a session still going would be a wrong one.
/// </summary>
public sealed class SessionWatcher(LedgerService ledger) : IAccountWatcher
{
    public string Name => "sessions";

    public IReadOnlyList<string> RequiredPermissions { get; } =
        [Gw2Permissions.Characters, Gw2Permissions.Wallet];

    /// <summary>A recap is about one session that just ended, never a backlog.</summary>
    public bool NeedsBaseline => false;

    public async Task<IReadOnlyList<AccountAlert>> CheckAsync(CancellationToken ct)
    {
        await ledger.RecordAsync(ct);

        // An ended session, noticed recently. Its key makes it once per session, whichever of the
        // poll or Today took the quiet point; the two hours keep a session from last week out of a
        // first run.
        if (await ledger.LatestAsync(ct) is not { After: { } quiet } played ||
            DateTimeOffset.UtcNow - quiet.At > TimeSpan.FromHours(2))
            return [];

        var text = await ledger.DescribeAsync(played, ct);

        var who = played.Characters.Count switch
        {
            0 => null,
            1 => played.Characters[0].Name,
            _ => L.F("{0} and {1} more", played.Characters[0].Name, played.Characters.Count - 1)
        };

        return
        [
            new AccountAlert(
                $"session:{played.Before.At.ToUnixTimeSeconds()}",
                L.F("Session recap · {0}", SessionRecap.Duration(played.PlayedSeconds)),
                who is null ? text.Wallet : $"{who} · {text.Wallet}")
        ];
    }
}
