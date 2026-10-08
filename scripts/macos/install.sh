#!/bin/bash
# MonitorAgent install / update for macOS (launchd daemon). Run from the extracted package: sudo bash install.sh
set -euo pipefail

if [ "$(id -u)" -ne 0 ]; then
    exec sudo bash "$0" "$@"
fi

here="$(cd "$(dirname "$0")" && pwd)"
label=com.ubg.monitoragent
target=/usr/local/monitoragent
plist="/Library/LaunchDaemons/$label.plist"
settings="$target/appsettings.json"

for group in monitoragent monitoragent-admin; do
    dscl . -read "/Groups/$group" >/dev/null 2>&1 || dseditgroup -o create "$group"
    if [ -n "${SUDO_USER:-}" ] && [ "$SUDO_USER" != root ]; then
        dseditgroup -o edit -a "$SUDO_USER" -t user "$group"
    fi
done
echo "Sign out and in to apply MonitorAgent group membership."

if launchctl print "system/$label" >/dev/null 2>&1; then
    echo "Stopping $label..."
    launchctl bootout "system/$label" || true
fi

umask 0077
mkdir -p "/Library/Application Support/MonitorAgent" /Library/Logs/MonitorAgent
chown root:wheel "/Library/Application Support/MonitorAgent" /Library/Logs/MonitorAgent
chmod 700 "/Library/Application Support/MonitorAgent"
chmod 750 /Library/Logs/MonitorAgent
if [ -f "$settings" ] && [ ! -f "/Library/Application Support/MonitorAgent/settings.json" ] && [ ! -f "/Library/Application Support/MonitorAgent/appsettings.previous.json" ]; then
    install -o root -g wheel -m 600 "$settings" "/Library/Application Support/MonitorAgent/appsettings.previous.json"
fi

echo "Copying the agent to $target..."
mkdir -p "$target" /Library/Logs/MonitorAgent "/Library/Application Support/MonitorAgent"
cp -R "$here/app/." "$target/"
chmod +x "$target/MonitorAgent.Service"
# A downloaded package is quarantined and the arm64 build must be signed; an ad-hoc signature is enough.
xattr -dr com.apple.quarantine "$target" 2>/dev/null || true
codesign --force --sign - "$target/MonitorAgent.Service" 2>/dev/null || true

chown -R root:wheel "$target"
chmod -R go+rX,go-w "$target"

if [ -d "$here/desktop" ]; then
    echo "Installing /Applications/MonitorAgent.app..."
    bash "$here/make-app.sh" "$here" "/Applications/MonitorAgent.app"
    chown -R root:wheel /Applications/MonitorAgent.app
    chmod -R go+rX,go-w /Applications/MonitorAgent.app
fi

install -m 644 "$here/$label.plist" "$plist"
chown root:wheel "$plist"
launchctl bootstrap system "$plist"
launchctl enable "system/$label"
echo "monitoragent installed and running."
echo "Logs: /Library/Logs/MonitorAgent. Data: /Library/Application Support/MonitorAgent."
