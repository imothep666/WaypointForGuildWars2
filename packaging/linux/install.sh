#!/usr/bin/env bash
# Installs Waypoint from a folder made by packaging/linux/build.ps1, for the current user only:
# no root, nothing outside the home folder.
#
#   bash install.sh               install or update
#   bash install.sh --software    the same, drawing without the GPU (for a GL driver that fails,
#                                 as the Tegra X1 under L4T may)
#   bash install.sh --uninstall   remove the app, its launcher and its menu entry; keeps your data
#
# Where things go:
#   ~/.local/share/gw2companion/            the app
#   ~/.local/bin/gw2companion               the launcher
#   ~/.local/share/applications/gw2companion.desktop   the menu entry
# Your data (API key, caches, goals) is the app's own, in ~/.local/share/GuildWarsCompanion and
# ~/.cache/GuildWarsCompanion, and is never touched here.

set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
target="$HOME/.local/share/gw2companion"
launcher="$HOME/.local/bin/gw2companion"
entry="$HOME/.local/share/applications/gw2companion.desktop"

software=false
uninstall=false

for arg in "$@"; do
    case "$arg" in
        --software) software=true ;;
        --uninstall) uninstall=true ;;
        -h|--help) sed -n '2,16p' "$0"; exit 0 ;;
        *) echo "Unknown option: $arg (try --help)" >&2; exit 2 ;;
    esac
done

refresh_menu() {
    # KDE and GNOME notice a new entry on their own sooner or later; these make it sooner.
    command -v update-desktop-database >/dev/null && update-desktop-database "$HOME/.local/share/applications" 2>/dev/null || true
    command -v kbuildsycoca6 >/dev/null && kbuildsycoca6 2>/dev/null || true
    command -v kbuildsycoca5 >/dev/null && kbuildsycoca5 2>/dev/null || true
}

if $uninstall; then
    rm -rf "$target"
    rm -f "$launcher" "$entry"
    refresh_menu
    echo "Removed. Your data is still in ~/.local/share/GuildWarsCompanion and ~/.cache/GuildWarsCompanion."
    exit 0
fi

if [[ ! -f "$here/BUILD" || ! -f "$here/app/GuildWarsCompanion.Desktop" ]]; then
    echo "This is not a build folder: run install.sh from the folder build.ps1 made." >&2
    exit 1
fi

rid="$(sed -n 's/^rid=//p' "$here/BUILD")"
version="$(sed -n 's/^version=//p' "$here/BUILD")"

# The build has to match the processor, or the app fails with "Exec format error" and says no more.
case "$(uname -m)" in
    aarch64|arm64) machine=linux-arm64 ;;
    x86_64|amd64) machine=linux-x64 ;;
    *) machine="unknown ($(uname -m))" ;;
esac

if [[ "$rid" != "$machine" ]]; then
    echo "This build is $rid, but this machine is $machine. Build again with: .\\packaging\\linux\\build.ps1 -Rid $machine" >&2
    exit 1
fi

# .NET needs ICU for text, Avalonia needs fontconfig. Kubuntu has both; a slimmer image may not.
missing=()
ldconfig -p 2>/dev/null | grep 'libicuuc\.so' >/dev/null || missing+=("libicu (sudo apt install libicu74)")
ldconfig -p 2>/dev/null | grep 'libfontconfig\.so' >/dev/null || missing+=("fontconfig (sudo apt install libfontconfig1)")

if (( ${#missing[@]} > 0 )); then
    echo "Missing, install these first:" >&2
    printf '  %s\n' "${missing[@]}" >&2
    exit 1
fi

# Replaced as a whole, so nothing from an older version lingers next to the new one.
rm -rf "$target"
mkdir -p "$target" "$(dirname "$launcher")" "$(dirname "$entry")"
cp -r "$here/app" "$target/app"
cp "$here/gw2companion.png" "$target/gw2companion.png"
cp "$here/BUILD" "$target/BUILD"

# A copy over a USB stick or from Windows loses the execute bit; give it back.
chmod +x "$target/app/GuildWarsCompanion.Desktop"
[[ -f "$target/app/createdump" ]] && chmod +x "$target/app/createdump"

flags=""
$software && flags="--software"

cat > "$launcher" <<EOF
#!/bin/sh
# Written by install.sh. Starts Waypoint; any options are passed on.
exec "$target/app/GuildWarsCompanion.Desktop" $flags "\$@"
EOF
chmod +x "$launcher"

cat > "$entry" <<EOF
[Desktop Entry]
Type=Application
Name=Waypoint
Comment=Guild Wars 2 companion
Exec=$launcher
Icon=$target/gw2companion.png
Terminal=false
Categories=Game;Utility;
EOF

refresh_menu

echo "Installed Waypoint $version ($rid)$($software && echo ', software rendering')."
echo "Start it from the menu, or run: $launcher"
case ":$PATH:" in
    *":$HOME/.local/bin:"*) ;;
    *) echo "(~/.local/bin is not on your PATH yet; it usually is after logging in again.)" ;;
esac
