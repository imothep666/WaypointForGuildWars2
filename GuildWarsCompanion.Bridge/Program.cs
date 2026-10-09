using System.Net.Sockets;
using System.Text;

using Avalonia;

using GuildWarsCompanion.Bridge.Protocol;

namespace GuildWarsCompanion.Bridge;

internal static class Program
{
    // Avalonia needs this before any SynchronizationContext exists, so it must stay the entry
    // point and stay free of anything that touches the toolkit.
    [STAThread]
    public static void Main(string[] args)
    {
        // Launching it again is how you get the window back. That matters most where the tray
        // is least reliable: a GNOME session shows no icon to click, so without this the only
        // way out of "keep running in the tray" would be to kill the process.
        if (RaisedRunningCopy()) return;

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>
    /// Asks a bridge already listening on this machine to show itself, and reports whether one
    /// did. Loopback only, and it never blocks for long: a stale port with something else on it
    /// must not stop the app starting.
    /// </summary>
    private static bool RaisedRunningCopy()
    {
        try
        {
            var port = BridgeSettings.Load().Port;

            using var client = new TcpClient();
            if (!client.ConnectAsync("127.0.0.1", port).Wait(TimeSpan.FromMilliseconds(400)))
                return false;

            using var stream = client.GetStream();
            stream.ReadTimeout = 800;

            var request = $"GET {BridgeProtocol.ShowPath} HTTP/1.1\r\nHost: localhost\r\n" +
                          "Connection: close\r\n\r\n";

            stream.Write(Encoding.ASCII.GetBytes(request));

            var answer = new byte[64];
            var read = stream.Read(answer, 0, answer.Length);

            // Only a bridge answers 204 here. Anything else on that port is not ours, and this
            // copy should start normally and report the clash itself.
            return read > 0 && Encoding.ASCII.GetString(answer, 0, read).Contains("204");
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Also called by the Avalonia designer, which is why it is public.</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            // Win32 on Windows, X11 on Linux. Wayland has its own backend as of 12.1 but it is
            // still experimental and opt-in; under a Wayland session this runs on XWayland,
            // which is what the clipboard code is written against.
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
