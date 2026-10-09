using System.Text.Json;

using GuildWarsCompanion.Gw2;
using GuildWarsCompanion.Host;

namespace GuildWarsCompanion.Services;

/// <summary>
/// Owns the API key for the app's lifetime: loads it from encrypted storage, validates it
/// against /v2/tokeninfo, and answers "may I call this endpoint?" so screens can degrade
/// gracefully instead of surfacing a 403.
/// </summary>
public sealed class ApiKeySession(Gw2Client client, ISecrets secrets)
{
    private const string StorageKey = "gw2_api_key";

    /// <summary>
    /// The last accepted /v2/tokeninfo, kept beside the key it belongs to.
    ///
    /// Without this the app is useless offline: validating a key needs the network, so a
    /// failed restore leaves every screen saying "add your API key" to someone who has one.
    /// Cached in <see cref="ISecrets"/> rather than the snapshot database precisely because it lives
    /// and dies with the key — signing out or swapping keys must not leave it behind.
    /// </summary>
    private const string TokenKey = "gw2_token_info";

    private readonly object _restoreGate = new();
    private Task<TokenInfo?>? _restore;

    /// <summary>The key currently on the client, so a rejected one can be rolled back.</summary>
    private string? _activeKey;

    /// <summary>Non-null once a key has been accepted by /v2/tokeninfo.</summary>
    public TokenInfo? Token { get; private set; }

    public bool IsAuthenticated => Token is not null;

    public bool HasPermission(string permission) =>
        Token?.Permissions.Contains(permission) == true;

    /// <summary>
    /// Awaits the one restore attempt, starting it if nobody has yet.
    ///
    /// Restoring hits the network to validate the key, so a screen that merely checks
    /// <see cref="IsAuthenticated"/> on startup wins the race and wrongly tells the user to
    /// add a key they already have. Every screen awaits this first instead.
    /// </summary>
    public Task<TokenInfo?> EnsureRestoredAsync(CancellationToken ct = default)
    {
        if (Token is not null) return Task.FromResult<TokenInfo?>(Token);

        lock (_restoreGate)
            return _restore ??= RestoreOnceAsync(ct);
    }

    private async Task<TokenInfo?> RestoreOnceAsync(CancellationToken ct)
    {
        var token = await RestoreAsync(ct);

        // A failed restore (offline, API down) must not be remembered as final, or the app
        // stays signed out until it is killed. Let the next refresh try again.
        if (token is null)
            lock (_restoreGate)
                _restore = null;

        return token;
    }

    /// <summary>
    /// Loads and re-validates a previously stored key. Returns null when there is none or
    /// when it no longer works — the caller then routes the user to Settings.
    /// </summary>
    public async Task<TokenInfo?> RestoreAsync(CancellationToken ct = default)
    {
        var stored = await secrets.GetAsync(StorageKey);
        if (string.IsNullOrWhiteSpace(stored)) return null;

        try
        {
            return await SignInAsync(stored, ct);
        }
        catch (Gw2ApiException ex) when (ex.Kind == Gw2ErrorKind.InvalidKey)
        {
            // Key was deleted on account.arena.net since we stored it.
            await SignOutAsync();
            return null;
        }
        catch (Gw2ApiException)
        {
            // Offline or API down. The key cannot be checked, but it was good the last time
            // it could be — so trust that rather than telling the user to add a key they
            // already have. Screens then fall through to their own cached data.
            client.UseApiKey(stored);
            _activeKey = stored.Trim();

            Token = await RecallAsync();
            return Token;
        }
    }

    /// <summary>Validates a key, and persists it only once the API has accepted it.</summary>
    public async Task<TokenInfo> SignInAsync(string apiKey, CancellationToken ct = default)
    {
        var previousKey = _activeKey;
        client.UseApiKey(apiKey);

        TokenInfo token;
        try
        {
            token = await client.GetTokenInfoAsync(ct);
        }
        catch
        {
            // A rejected key must not stay on the shared client, or every other screen
            // starts failing until the app is restarted. Put the working one back.
            client.UseApiKey(previousKey);
            throw;
        }

        // ISecrets is backed by the Android Keystore — never SharedPreferences.
        await secrets.SetAsync(StorageKey, apiKey.Trim());
        await RememberAsync(token);
        _activeKey = apiKey.Trim();
        Token = token;
        return token;
    }

    public Task SignOutAsync()
    {
        lock (_restoreGate)
            _restore = null;

        secrets.Remove(StorageKey);
        secrets.Remove(TokenKey);
        client.UseApiKey(null);
        _activeKey = null;
        Token = null;
        return Task.CompletedTask;
    }

    private async Task RememberAsync(TokenInfo token)
    {
        try
        {
            await secrets.SetAsync(
                TokenKey, JsonSerializer.Serialize(token, Gw2JsonContext.Default.TokenInfo));
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            // Losing the offline copy is not worth failing a successful sign-in.
        }
    }

    /// <summary>The remembered token, or null if there is none or it can no longer be read.</summary>
    private async Task<TokenInfo?> RecallAsync()
    {
        try
        {
            var stored = await secrets.GetAsync(TokenKey);

            return string.IsNullOrWhiteSpace(stored)
                ? null
                : JsonSerializer.Deserialize(stored, Gw2JsonContext.Default.TokenInfo);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return null;
        }
    }
}
