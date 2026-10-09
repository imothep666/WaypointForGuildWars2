#!/bin/sh
# Starts the app inside Ubuntu Touch's confinement.
#
# AppArmor lets a click write only below folders named after its package, so the XDG homes are
# pointed there: the app keeps its own "GuildWarsCompanion" folder, one level further down.
# APP_ID is "<package>_<app>_<version>"; the package is the part before the first underscore.
package="${APP_ID%%_*}"
export XDG_DATA_HOME="$HOME/.local/share/$package"
export XDG_CACHE_HOME="$HOME/.cache/$package"
export XDG_CONFIG_HOME="$HOME/.config/$package"
mkdir -p "$XDG_DATA_HOME" "$XDG_CACHE_HOME" "$XDG_CONFIG_HOME"

# The runtime's diagnostics socket wants a place under /tmp that confinement does not grant.
export DOTNET_EnableDiagnostics=0

# Lomiri is a Mir compositor: native Wayland, which 12.1 only uses when asked by name.
exec "$(dirname "$0")/app/GuildWarsCompanion.Desktop" --wayland "$@"
