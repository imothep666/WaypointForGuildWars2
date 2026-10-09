# Waypoint for Guild Wars 2

A companion app for Guild Wars 2, on a phone or a second screen beside the game: what is left to do
today, the Wizard's Vault, characters and their builds, the Trading Post, crafting, achievements,
a map and a compass that points at the next marker.

- **Android**, and a **desktop app** for Linux and Windows, both built with
  [Avalonia](https://avaloniaui.net).
- **PC Bridge**, a small Windows and Linux program beside the game. It reads the game's live state
  (MumbleLink) and passes it to the app over the local network, so the app knows which character
  is played and where.
- **Blish HUD module**, which shows the app's route in the game.

It reads your account through the official Guild Wars 2 API with an API key you create yourself.
The key stays on the device.

## Download

Linux builds are on the [releases page](https://github.com/imothep666/WaypointForGuildWars2/releases):

- `Waypoint-linux-arm64.AppImage`: an ARM board, a Switch under L4T, a Raspberry Pi
- `Waypoint-linux-x64.AppImage`: an ordinary PC

```bash
chmod +x Waypoint-linux-*.AppImage
./Waypoint-linux-*.AppImage            # add --software if the window stays black
```

The first start adds Waypoint to the application menu. From then on the app updates itself: when a
new version has been downloaded, the title bar offers **RESTART TO UPDATE**.

Or let a script do it, which picks the right file for the processor and also replaces an older
install made by `packaging/linux/install.sh`, keeping your data and its `--software` choice:

```bash
curl -fsSL https://raw.githubusercontent.com/imothep666/WaypointForGuildWars2/main/packaging/linux/install-appimage.sh | bash
```

Add `-s -- --software` after `bash` for drawing without the GPU, or `-s -- --uninstall` to remove it.

The PC Bridge, for the PC the game runs on, is in the same release:

- `WaypointBridge-bridge-win-Setup.exe`: Windows. Installs for your user only, no administrator
  needed. It is not signed, so Windows SmartScreen asks once: "More info", then "Run anyway".
- `WaypointBridge-bridge-linux-x64.AppImage`: Linux, for the game under Proton or Wine.

It updates itself the same way as the app.

## Building

Needs the .NET 10 SDK, and the Android workload for the Android app.

```bash
dotnet build GuildWarsCompanion.Avalonia.Desktop     # desktop app
dotnet run --project GuildWarsCompanion.Avalonia.Desktop
dotnet test GuildWarsCompanion.Tests
```

`publish-apk.ps1` builds the Android APK, and `packaging/linux/build.ps1` a Linux folder build.
`azure-pipelines.yml` makes the releases. [ROADMAP.md](ROADMAP.md) records what was built, why,
and what was learned along the way.

## Licence

The code is under the GNU General Public License, version 3 or later; see [LICENSE](LICENSE).
The game's artwork, the fonts and the libraries keep their own terms; see
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

Waypoint is an unofficial fan project, not made, endorsed, sponsored or approved by ArenaNet or
NCSOFT. Guild Wars 2 and all associated marks are trademarks of NCSOFT Corporation.
