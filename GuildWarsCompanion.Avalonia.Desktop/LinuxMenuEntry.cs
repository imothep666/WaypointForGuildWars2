namespace GuildWarsCompanion.Ava.Desktop;

/// <summary>
/// The menu entry for the AppImage, which brings none of its own.
///
/// Written at every start from an AppImage, so it follows the file wherever it is moved and
/// takes over the entry packaging/linux/install.sh used to write: same file name, so the menu
/// shows one Waypoint, not two. The options this run was started with go into it, so starting
/// the AppImage once with --software from a terminal makes the menu start it that way too.
///
/// The icon is copied out next to the app's data, because an AppImage's own files are mounted
/// somewhere new on every run and nothing outside it can point at them.
/// </summary>
internal static class LinuxMenuEntry
{
    private const string FileName = "gw2companion.desktop";

    public static void Refresh(string dataDirectory, string[] args)
    {
        if (!OperatingSystem.IsLinux()) return;

        // Set by the AppImage runtime to the file itself; absent for any other way of starting.
        if (System.Environment.GetEnvironmentVariable("APPIMAGE") is not { Length: > 0 } appImage) return;

        try
        {
            var icon = Path.Combine(dataDirectory, "waypoint.png");
            using (var source = typeof(LinuxMenuEntry).Assembly.GetManifestResourceStream("waypoint.png"))
            using (var target = File.Create(icon))
                source?.CopyTo(target);

            var options = string.Concat(args.Where(arg => arg is "--software" or "--wayland").Select(arg => " " + arg));

            var applications = Path.Combine(
                System.Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } xdg
                    ? xdg
                    : Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), ".local", "share"),
                "applications");
            Directory.CreateDirectory(applications);

            var entry = $"""
                [Desktop Entry]
                Type=Application
                Name=Waypoint
                Comment=Guild Wars 2 companion
                Exec="{appImage}"{options}
                Icon={icon}
                Terminal=false
                Categories=Game;Utility;

                """;

            var path = Path.Combine(applications, FileName);
            if (!File.Exists(path) || File.ReadAllText(path) != entry) File.WriteAllText(path, entry);
        }
        catch (Exception)
        {
            // A menu entry is a convenience; the app runs without one.
        }
    }
}
