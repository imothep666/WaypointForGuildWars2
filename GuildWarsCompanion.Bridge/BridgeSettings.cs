using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;

using GuildWarsCompanion.Bridge.Protocol;

namespace GuildWarsCompanion.Bridge;

/// <summary>What survives a restart: the port, the code the phone was told, and whether
/// closing the window should leave the bridge running.</summary>
/// <param name="MinimiseToTray">
/// Null means nobody has said, which is not the same as saying no — a config written before
/// this setting existed must still get the platform's answer rather than a silent "off".
/// </param>
/// <param name="Id">
/// This install's stable name on the network. Null in a config written before pairing had any
/// identity beyond the code; <see cref="BridgeSettings.Load"/> fills it in and saves, the same
/// way <paramref name="MinimiseToTray"/> is allowed to arrive unset.
/// </param>
/// <param name="Secret">
/// The high-entropy half of pairing, handed over by QR rather than read out. Null on an install
/// that has only ever been paired by typing, and that is a supported state, not a broken one —
/// see <see cref="BridgeProtocol.IsStrong"/> for what it buys.
/// </param>
public sealed record BridgeConfig(
    int Port, string PairingCode, bool? MinimiseToTray = null,
    string? Id = null, string? Name = null, string? Secret = null,
    bool? BossReminders = null,
    bool? StartBlishHud = null,
    bool? PhoneSeen = null)
{
    /// <summary>
    /// Whether a world boss panel may appear over the game.
    ///
    /// Nullable and defaulted on, like <see cref="MinimiseToTray"/>: a settings file written
    /// before this existed has nothing to say about it, and the answer for somebody who has never
    /// been asked is the feature working. Off is a deliberate act and is written down as one.
    /// </summary>
    public bool RemindsAboutBosses => BossReminders ?? true;

    /// <summary>
    /// Whether to start Blish HUD when the game starts. Off unless asked for, unlike the boss
    /// panel: this launches somebody else's program, and nobody who has not chosen it wants that.
    /// </summary>
    public bool StartsBlishHud => StartBlishHud ?? false;

    /// <summary>
    /// What the phone calls this PC. The hostname is right nearly always and editable when it
    /// is not — "DESKTOP-4F2K1" is a worse label than "Desktop" but a much better one than the
    /// address it replaces.
    /// </summary>
    public string DisplayName =>
        string.IsNullOrWhiteSpace(Name) ? Environment.MachineName : Name.Trim();

    /// <summary>
    /// What a phone is told when it asks who is there. Carries no secret: this answers an
    /// unauthenticated question, and <see cref="BridgeProtocol.Proof"/> is what a caller has to
    /// ask for on purpose.
    /// </summary>
    public BridgeIdentity Identity(string? nonce = null) =>
        new(Id ?? string.Empty, DisplayName, Port, BridgeProtocol.Proof(Secret, nonce));

    /// <summary>Everything the phone needs to pair, and no address — see <see cref="BridgeCard"/>.</summary>
    public BridgeCard? Card =>
        Id is null || Secret is null ? null : new BridgeCard(Id, DisplayName, Secret, Port);

    /// <summary>
    /// What to do when nobody has chosen.
    ///
    /// Windows always has a notification area. Linux may not — a tray icon needs
    /// StatusNotifierItem, which KDE implements and GNOME does not support at all — so
    /// defaulting this on everywhere would let a GNOME user close their only window and lose the
    /// app to the background. They can still turn it on, and launching again brings it back.
    /// </summary>
    public static bool TrayByDefault => OperatingSystem.IsWindows();

    public bool KeepInTray => MinimiseToTray ?? TrayByDefault;
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(BridgeConfig))]
internal partial class ConfigJson : JsonSerializerContext;

/// <summary>
/// Reading and writing the one config file.
///
/// <see cref="Environment.SpecialFolder.ApplicationData"/> lands in %APPDATA% on Windows and
/// ~/.config on Linux, which is the right place on both without either being named here.
/// </summary>
public static class BridgeSettings
{
    private static string Path => System.IO.Path.Combine(Folder, "bridge.json");

    /// <summary>Beside the config, so "where is my setup" and "where is my log" have one answer.</summary>
    public static string LogPath => System.IO.Path.Combine(Folder, "bridge.log");

    private static string Folder => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GuildWarsCompanion");

    /// <summary>
    /// The stored config, or a fresh one. A first run pairs itself: there is no state in which
    /// the bridge is listening without a code, because that state accepts anything.
    /// </summary>
    public static BridgeConfig Load()
    {
        try
        {
            if (File.Exists(Path) &&
                JsonSerializer.Deserialize(File.ReadAllText(Path), ConfigJson.Default.BridgeConfig)
                    is { PairingCode.Length: > 0 } stored)
                return Complete(stored);
        }
        catch (Exception)
        {
            // A corrupt or unreadable file is replaced rather than fatal.
        }

        var made = Complete(
            new BridgeConfig(BridgeProtocol.DefaultPort, BridgeProtocol.NewPairingCode()));

        Save(made);

        return made;
    }

    /// <summary>
    /// Fills in anything a stored config predates, and writes it back so the answer is stable.
    ///
    /// The identity in particular has to survive restarts or it is worthless: the phone looks a
    /// bridge up by <see cref="BridgeConfig.Id"/> precisely because the address will not hold
    /// still, and an id regenerated every launch would be no better than the address.
    /// </summary>
    private static BridgeConfig Complete(BridgeConfig config)
    {
        if (config.Id is not null && config.Secret is not null) return config;

        var filled = config with
        {
            Id = config.Id ?? BridgeProtocol.NewBridgeId(),
            Secret = config.Secret ?? BridgeProtocol.NewPairingSecret()
        };

        Save(filled);

        return filled;
    }

    public static void Save(BridgeConfig config)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(Path, JsonSerializer.Serialize(config, ConfigJson.Default.BridgeConfig));
        }
        catch (Exception)
        {
            // Not being able to persist is not a reason to stop working this session.
        }
    }
}

/// <summary>
/// Which addresses this PC can be reached on.
///
/// Shown rather than discovered, because the phone has to be told something and an MVP that
/// asks for an IP once beats one that ships mDNS to save the typing. Loopback and down
/// interfaces are excluded: an address the phone cannot reach is worse than no address, since
/// it looks like an answer.
/// </summary>
public static class LanAddresses
{
    public static IReadOnlyList<string> Find()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(nic => nic.OperationalStatus == OperationalStatus.Up)
                .Where(nic => nic.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(nic => nic.GetIPProperties().UnicastAddresses)
                .Select(address => address.Address)
                .Where(address => address.AddressFamily == AddressFamily.InterNetwork)
                .Where(address => !IPAddress.IsLoopback(address))
                .Select(address => address.ToString())
                .Distinct()
                .ToList();
        }
        catch (Exception)
        {
            return [];
        }
    }
}
