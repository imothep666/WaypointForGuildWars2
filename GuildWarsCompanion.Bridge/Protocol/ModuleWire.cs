using System;
using System.Globalization;
using System.IO;

namespace GuildWarsCompanion.Bridge.Protocol;

/// <summary>What one line on the module link says.</summary>
public enum ModuleLineKind
{
    Hello,
    Ok,
    Emote,
    Done,
    Busy,

    /// <summary>A <see cref="GuideWire"/> block follows, up to <see cref="End"/>.</summary>
    Trail,

    End,

    /// <summary>The character reached the objective of the trail named in <see cref="ModuleLine.Text"/>,
    /// and the module has taken the trail down.</summary>
    Arrived
}

/// <summary>One parsed line. A class rather than a record: this file is compiled into the
/// Blish HUD module too, which is net48 and has no init-only setters.</summary>
public sealed class ModuleLine
{
    public ModuleLine(ModuleLineKind kind, int id, string text)
    {
        Kind = kind;
        Id = id;
        Text = text;
    }

    public ModuleLineKind Kind { get; }

    /// <summary>Which emote a done or busy answers. Zero on lines that answer nothing.</summary>
    public int Id { get; }

    /// <summary>The token on a hello, the command on an emote, the reason on a busy, the
    /// objective on an arrived.</summary>
    public string Text { get; }
}

/// <summary>
/// The wire between the bridge and its Blish HUD module, both on this PC.
///
/// <b>Why there is a module at all.</b> The bridge must not type into the game itself; see
/// ROADMAP.md. Blish HUD is where GW2 chat input already lives, as one of its own services, used
/// by modules in its official repository — EmoteTome, Emotes, Chat Shorts — on the same rule:
/// one click, one chat line. The module is the smallest possible use of that service: it receives
/// one emote the phone asked for and hands it to Blish HUD's chat, and does nothing else.
///
/// <b>The wire is deliberately primitive.</b> Newline-separated text over a loopback socket:
///
///   module → bridge   hello &lt;token&gt;
///   bridge → module   ok
///   bridge → module   emote &lt;id&gt; &lt;command&gt;
///   module → bridge   done &lt;id&gt;   |   busy &lt;id&gt; &lt;reason&gt;
///   bridge → module   trail, then a <see cref="GuideWire"/> body line by line, then end
///   module → bridge   arrived &lt;map&gt; &lt;x&gt; &lt;z&gt;
///
/// <c>arrived</c> names the objective by its map and where it stands, so the bridge can tell it
/// apart from a trail the phone has chosen since: an arrival for the last objective must not
/// clear the next one.
///
/// Compiled into net10 and net48 alike, which rules out everything clever and costs nothing,
/// since a handful of kinds of line are the whole conversation.
///
/// <b>How the module finds the bridge.</b> The bridge writes <see cref="CardFileName"/> beside
/// its config: the port and a token made fresh each run. Loopback only, so nothing on the network
/// can reach it; the token keeps other programs on this PC from typing into chat through it.
/// </summary>
public static class ModuleWire
{
    /// <summary>Beside bridge.json in %APPDATA%\GuildWarsCompanion.</summary>
    public const string CardFileName = "blish-module.txt";

    public const string Folder = "GuildWarsCompanion";

    /// <summary>A line longer than this is not a line this conversation has.</summary>
    public const int MaxLine = 512;

    /// <summary>
    /// Three past the bridge's port: TCP for requests, UDP for discovery, TCP for the compass
    /// stream, and this. Loopback only, so it never appears in a firewall dialog at all.
    /// </summary>
    public static int PortFor(int bridgePort) => bridgePort + 3;

    public static string CardPath(string appData) => Path.Combine(appData, Folder, CardFileName);

    public static string Card(int port, string token) => $"{port} {token}";

    public static bool TryReadCard(string? text, out int port, out string token)
    {
        port = 0;
        token = string.Empty;

        var parts = (text ?? string.Empty).Trim().Split(' ');

        if (parts.Length != 2 || !int.TryParse(parts[0], out port) || port <= 0 || port > 65535)
            return false;

        token = parts[1];

        return token.Length > 0;
    }

    public static string Hello(string token) => $"hello {token}";

    public const string Ok = "ok";

    public const string Trail = "trail";

    public const string End = "end";

    public static string Emote(int id, string command) => $"emote {id} {command}";

    public static string Done(int id) => $"done {id}";

    public const string ArrivedWord = "arrived";

    /// <summary>
    /// "arrived 1015 1204.5 -8812.1": the trail's map and objective, x and z, written the way
    /// <see cref="GuideWire"/> writes numbers. Both ends build it from the same parsed trail, so
    /// the bridge compares the line it is sent with the one it builds itself, and they match
    /// exactly when they are about the same objective.
    /// </summary>
    public static string Arrived(GuidedTrail trail) =>
        $"{ArrivedWord} {trail.MapId.ToString(CultureInfo.InvariantCulture)} " +
        $"{trail.Target.X.ToString("0.##", CultureInfo.InvariantCulture)} " +
        $"{trail.Target.Z.ToString("0.##", CultureInfo.InvariantCulture)}";

    /// <summary>Reasons are one line, always: a newline in one would be a second message.</summary>
    public static string Busy(int id, string reason) =>
        $"busy {id} {reason.Replace('\r', ' ').Replace('\n', ' ')}";

    /// <summary>The line, or null if it is not one this conversation has.</summary>
    public static ModuleLine? Parse(string? line)
    {
        if (line is null || line.Length == 0 || line.Length > MaxLine) return null;

        var space = line.IndexOf(' ');
        var word = space < 0 ? line : line.Substring(0, space);
        var rest = space < 0 ? string.Empty : line.Substring(space + 1);

        switch (word)
        {
            case "ok":
                return rest.Length == 0 ? new ModuleLine(ModuleLineKind.Ok, 0, string.Empty) : null;

            case Trail:
                return rest.Length == 0 ? new ModuleLine(ModuleLineKind.Trail, 0, string.Empty) : null;

            case End:
                return rest.Length == 0 ? new ModuleLine(ModuleLineKind.End, 0, string.Empty) : null;

            case ArrivedWord:
                return rest.Length > 0 ? new ModuleLine(ModuleLineKind.Arrived, 0, rest) : null;

            case "hello":
                return rest.Length > 0 ? new ModuleLine(ModuleLineKind.Hello, 0, rest) : null;

            case "emote":
            case "busy":
                {
                    var gap = rest.IndexOf(' ');
                    if (gap <= 0 || !int.TryParse(rest.Substring(0, gap), out var id) || id <= 0) return null;

                    var text = rest.Substring(gap + 1);
                    if (text.Length == 0) return null;

                    return new ModuleLine(word == "emote" ? ModuleLineKind.Emote : ModuleLineKind.Busy, id, text);
                }

            case "done":
                return int.TryParse(rest, out var done) && done > 0
                    ? new ModuleLine(ModuleLineKind.Done, done, string.Empty)
                    : null;

            default:
                return null;
        }
    }
}
