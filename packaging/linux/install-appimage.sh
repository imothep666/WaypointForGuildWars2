#!/usr/bin/env bash
# Installs Waypoint as the self-updating AppImage from the GitHub releases, for the current user
# only, and moves an install made by install.sh over to it. No root.
#
#   bash install-appimage.sh               install, or replace the old install.sh copy
#   bash install-appimage.sh --software    the same, drawing without the GPU (for a GL driver that
#                                          fails, as the Tegra X1 under L4T may)
#   bash install-appimage.sh --uninstall   remove the AppImage and its menu entry; keeps your data
#
# Where things go:
#   ~/Applications/Waypoint.AppImage                   the app, which updates itself in place
#   ~/.local/share/applications/gw2companion.desktop   the menu entry (the app rewrites it at start)
# An old install.sh copy (~/.local/share/gw2companion, ~/.local/bin/gw2companion) is removed, and
# its --software choice carried over. Your data (API key, caches, goals) is in
# ~/.local/share/GuildWarsCompanion and ~/.cache/GuildWarsCompanion, and is never touched here.

set -euo pipefail

releases="https://github.com/imothep666/WaypointForGuildWars2/releases/latest/download"
appimage="$HOME/Applications/Waypoint.AppImage"
entry="$HOME/.local/share/applications/gw2companion.desktop"
old_app="$HOME/.local/share/gw2companion"
old_launcher="$HOME/.local/bin/gw2companion"

software=false
uninstall=false

for arg in "$@"; do
    case "$arg" in
        --software) software=true ;;
        --uninstall) uninstall=true ;;
        -h|--help) sed -n '2,17p' "$0"; exit 0 ;;
        *) echo "Unknown option: $arg (try --help)" >&2; exit 2 ;;
    esac
done

refresh_menu() {
    # KDE and GNOME notice a new entry on their own sooner or later; these make it sooner.
    command -v update-desktop-database >/dev/null && update-desktop-database "$HOME/.local/share/applications" 2>/dev/null || true
    command -v kbuildsycoca6 >/dev/null && kbuildsycoca6 2>/dev/null || true
    command -v kbuildsycoca5 >/dev/null && kbuildsycoca5 2>/dev/null || true
}

# The Waypoint processes started from one file: the old install's program, or the AppImage. An
# AppImage's program runs from a mount that changes every time, so it is found by the APPIMAGE
# variable its runtime sets; killing by the AppImage's path would only hit that mount. Never by
# name alone: another copy of Waypoint elsewhere on the machine is none of this script's business.
pids_of() {
    local pid exe
    for pid in $(pgrep -f GuildWarsCompanion.Desktop || true); do
        exe=$(readlink "/proc/$pid/exe" 2>/dev/null || true)
        if [[ "$exe" == "$1" ]] || tr '\0' '\n' < "/proc/$pid/environ" 2>/dev/null | grep -qx "APPIMAGE=$1"; then
            echo "$pid"
        fi
    done
}

# Ends a running copy, so its file can be replaced or removed: politely, then for certain.
stop() {
    local pids
    pids=$(pids_of "$1")
    [[ -z "$pids" ]] && return 0

    echo "Closing the running Waypoint."
    kill $pids 2>/dev/null || true
    sleep 2
    pids=$(pids_of "$1")
    [[ -n "$pids" ]] && kill -9 $pids 2>/dev/null || true
}

if $uninstall; then
    stop "$appimage"
    rm -f "$appimage" "$entry"
    rm -rf /var/tmp/velopack/Waypoint
    refresh_menu
    echo "Removed. Your data is still in ~/.local/share/GuildWarsCompanion and ~/.cache/GuildWarsCompanion."
    exit 0
fi

# The AppImage has to match the processor, or it fails with "Exec format error" and says no more.
case "$(uname -m)" in
    aarch64|arm64) arch=linux-arm64 ;;
    x86_64|amd64) arch=linux-x64 ;;
    *) echo "There is no Waypoint build for this processor ($(uname -m))." >&2; exit 1 ;;
esac

# The choice of software drawing already made, so neither the move from the old install nor a
# second run brings back a black window: the old launcher has it, and so does the menu entry.
for made in "$old_launcher" "$entry"; do
    if [[ -f "$made" ]] && grep -q -- '--software' "$made"; then software=true; fi
done

download() {
    if command -v curl >/dev/null; then curl -fL --progress-bar -o "$2" "$1"
    elif command -v wget >/dev/null; then wget -q --show-progress -O "$2" "$1"
    else echo "Needs curl or wget to download Waypoint." >&2; exit 1
    fi
}

mkdir -p "$(dirname "$appimage")" "$(dirname "$entry")"

echo "Downloading Waypoint for $arch…"
partial="$appimage.part"
download "$releases/Waypoint-$arch.AppImage" "$partial"
chmod +x "$partial"

# Whatever is running goes before its file is replaced: the old copy and, on a second run, this one.
[[ -d "$old_app" ]] && stop "$old_app/app/GuildWarsCompanion.Desktop"
stop "$appimage"
mv -f "$partial" "$appimage"

if [[ -d "$old_app" || -f "$old_launcher" ]]; then
    rm -rf "$old_app"
    rm -f "$old_launcher"
    echo "Removed the old install from $old_app."
fi

flags=""
$software && flags=" --software"

# The app writes this entry itself at every start, with the icon; this one is there so the menu
# works before the first start, and replaces the old install's entry, which pointed at its launcher.
cat > "$entry" <<EOF
[Desktop Entry]
Type=Application
Name=Waypoint
Comment=Guild Wars 2 companion
Exec="$appimage"$flags
Terminal=false
Categories=Game;Utility;
EOF

refresh_menu

echo "Installed Waypoint ($arch$($software && echo ', software rendering')) at $appimage."
echo "Start it from the menu, or run: \"$appimage\"$flags"
echo "From now on it updates itself: the title bar offers RESTART TO UPDATE when a new version is ready."
