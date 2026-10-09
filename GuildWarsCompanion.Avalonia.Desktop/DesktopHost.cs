using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using GuildWarsCompanion.Host;

namespace GuildWarsCompanion.Ava.Desktop;

/// <summary>
/// Settings as one small JSON file in the data folder, rewritten whole on every change.
///
/// Whole-file and atomic — written beside itself and moved over — because these are a handful of
/// switches and a list of paired PCs, and a half-written file would lose all of them at once.
/// </summary>
public sealed class FileSettings : ISettings
{
    private readonly string _path;
    private readonly JsonObject _values;
    private readonly Lock _gate = new();

    public FileSettings(string folder)
    {
        _path = Path.Combine(folder, "settings.json");
        _values = Load(_path);
    }

    public T Get<T>(string key, T defaultValue)
    {
        lock (_gate)
        {
            if (_values[key] is not { } node) return defaultValue;

            try
            {
                return node.Deserialize<T>() ?? defaultValue;
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or NotSupportedException)
            {
                // Stored as another type by an older build. The default is the honest answer.
                return defaultValue;
            }
        }
    }

    public void Set<T>(string key, T value)
    {
        lock (_gate)
        {
            _values[key] = JsonSerializer.SerializeToNode(value);
            Save();
        }
    }

    public void Remove(string key)
    {
        lock (_gate)
        {
            if (_values.Remove(key)) Save();
        }
    }

    private void Save() => AtomicFile.Write(_path, _values.ToJsonString());

    private static JsonObject Load(string path)
    {
        try
        {
            return File.Exists(path) && JsonNode.Parse(File.ReadAllText(path)) is JsonObject stored
                ? stored
                : [];
        }
        catch (JsonException)
        {
            // Unreadable is treated as empty rather than fatal, as the phone's stores do.
            return [];
        }
    }
}

/// <summary>
/// The API key and pairing secrets, in a file of their own.
///
/// On Windows each value is sealed with DPAPI for the current user, so the file is useless copied
/// to another account or machine. Linux has no equivalent that works without a desktop keyring
/// running, so there the file is plain text that only the user can read (mode 600) — no better
/// than the API key in a password manager's export, and said here rather than pretended
/// otherwise. A Secret Service backend is the upgrade, where a keyring exists.
/// </summary>
public sealed class FileSecrets : ISecrets
{
    private static readonly byte[] Entropy = "GuildWarsCompanion.Secrets"u8.ToArray();

    private readonly string _path;
    private readonly Dictionary<string, string> _sealed;
    private readonly Lock _gate = new();

    public FileSecrets(string folder)
    {
        _path = Path.Combine(folder, "secrets.json");
        _sealed = Load(_path);
    }

    public Task<string?> GetAsync(string key)
    {
        lock (_gate)
        {
            return Task.FromResult(_sealed.TryGetValue(key, out var stored) ? Open(stored) : null);
        }
    }

    public Task SetAsync(string key, string value)
    {
        lock (_gate)
        {
            _sealed[key] = Seal(value);
            Save();
        }

        return Task.CompletedTask;
    }

    public bool Remove(string key)
    {
        lock (_gate)
        {
            if (!_sealed.Remove(key)) return false;

            Save();
            return true;
        }
    }

    private void Save()
    {
        AtomicFile.Write(_path, JsonSerializer.Serialize(_sealed));

        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(_path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private static string Seal(string value)
    {
        if (!OperatingSystem.IsWindows()) return value;

        var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(value), Entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(bytes);
    }

    private static string? Open(string stored)
    {
        if (!OperatingSystem.IsWindows()) return stored;

        try
        {
            var bytes = ProtectedData.Unprotect(Convert.FromBase64String(stored), Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            // Sealed for another user or machine. Treated as absent: the cost is entering the key.
            return null;
        }
    }

    private static Dictionary<string, string> Load(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? []
                : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}

internal static class AtomicFile
{
    public static void Write(string path, string text)
    {
        var temporary = path + ".tmp";

        File.WriteAllText(temporary, text);
        File.Move(temporary, path, overwrite: true);
    }
}
