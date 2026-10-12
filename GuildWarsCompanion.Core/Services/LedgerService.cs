using GuildWarsCompanion.Data;
using GuildWarsCompanion.Gw2;

namespace GuildWarsCompanion.Services;

/// <summary>The last session in words, for Today and for the notification.</summary>
/// <param name="Heading">"Last session · 1 h 50 min" or "This session · 40 min so far".</param>
/// <param name="When">"ended about 23:10" or "playing now".</param>
public sealed record SessionText(string Heading, string When, IReadOnlyList<string> Characters, string Wallet);

/// <summary>
/// Keeps the ledger and reads sessions out of it.
///
/// A point is recorded by the half-hourly background poll and whenever Today is refreshed, never
/// more often than every five minutes: one request for the names, one per character for its core,
/// one for the wallet. It needs the characters and wallet scopes; without them there is no recap,
/// and nothing pretends there is.
/// </summary>
public sealed class LedgerService(Gw2Client client, LedgerStore store, ApiKeySession session, Gw2Cache cache)
{
    private static readonly TimeSpan Spacing = TimeSpan.FromMinutes(5);

    public bool CanRecord =>
        session.HasPermission(Gw2Permissions.Characters) && session.HasPermission(Gw2Permissions.Wallet);

    /// <summary>Takes a point unless one was taken in the last five minutes. False when it did not.</summary>
    public async Task<bool> RecordAsync(CancellationToken ct = default)
    {
        if (!CanRecord) return false;

        var now = DateTimeOffset.UtcNow;

        if (await store.LatestAtAsync(ct) is { } latest && now - latest < Spacing) return false;

        var characters = new Dictionary<string, (long, int)>();

        foreach (var name in await client.GetCharacterNamesAsync(ct))
        {
            var core = await client.GetCharacterCoreAsync(name, ct);
            characters[core.Name] = (core.AgeSeconds, core.Level);
        }

        var wallet = (await client.GetWalletAsync(ct)).ToDictionary(entry => entry.Id, entry => entry.Value);

        await store.SaveAsync(new LedgerPoint(now, characters, wallet), ct);

        return true;
    }

    public async Task<Session?> LatestAsync(CancellationToken ct = default) =>
        SessionRecap.Latest(await store.LoadAsync(ct));

    public async Task<SessionText> DescribeAsync(Session played, CancellationToken ct = default)
    {
        var currencies = await cache.GetCurrenciesAsync(ct);

        if (currencies.Count == 0)
        {
            try
            {
                currencies = await client.GetCurrenciesAsync(ct);
                await cache.SaveCurrenciesAsync(currencies, ct);
            }
            catch (Gw2ApiException)
            {
                // Numbered rather than named; the amounts are still right.
            }
        }

        var names = currencies.ToDictionary(currency => currency.Id, currency => currency.Name);

        var duration = SessionRecap.Duration(played.PlayedSeconds);

        return new SessionText(
            played.IsOngoing ? L.F("This session · {0} so far", duration) : L.F("Last session · {0}", duration),
            // When the last poll still saw playtime rising: the session ended after it, before the next.
            played.IsOngoing
                ? L.T("playing now")
                : L.F("last seen playing {0:ddd HH:mm}", played.LastSeen.At.ToLocalTime()),
            played.Characters.Select(SessionRecap.Line).ToList(),
            SessionRecap.Wallet(played.WalletChange, id => names.GetValueOrDefault(id, L.F("currency {0}", id))));
    }
}
