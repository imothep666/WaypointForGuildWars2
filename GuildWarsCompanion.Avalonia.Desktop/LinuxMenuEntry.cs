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
///
/// The PC Bridge compiles this same file and writes its own entry with it.
/// </summary>
internal static class LinuxMenuEntry
{
    /// <summary>The app's entry: the file name install.sh used, so the AppImage takes it over.</summary>
    public static void Refresh(string dataDirectory, string[] args) =>
        Refresh(dataDirectory, args, "gw2companion.desktop", "Waypoint", "Guild Wars 2 companion",
            "waypoint.png", "Game;Utility;");

    /// <param name="icon">An embedded resource of the calling assembly, copied out under the same name.</param>
    public static void Refresh(string dataDirectory, string[] args, string fileName, string name, string comment,
        string icon, string categories)
    {
        if (!OperatingSystem.IsLinux()) return;

        // Set by the AppImage runtime to the file itself; absent for any other way of starting.
        if (System.Environment.GetEnvironmentVariable("APPIMAGE") is not { Length: > 0 } appImage) return;

        try
        {
            var iconPath = Path.Combine(dataDirectory, icon);
            using (var source = typeof(LinuxMenuEntry).Assembly.GetManifestResourceStream(icon))
            using (var target = File.Create(iconPath))
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
                Name={name}
                Comment={comment}
                Exec="{appImage}"{options}
                Icon={iconPath}
                Terminal=false
                Categories={categories}

                """;

            var path = Path.Combine(applications, fileName);
            if (!File.Exists(path) || File.ReadAllText(path) != entry) File.WriteAllText(path, entry);
        }
        catch (Exception)
        {
            // A menu entry is a convenience; the app runs without one.
        }
    }
}
