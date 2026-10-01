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

if launchctl print "system/$label" >/dev/null 2>&1; then
    echo "Stopping $label..."
    launchctl bootout "system/$label" || true
fi

# The app saves its settings into the "Setting" section of appsettings.json; an update must not wipe them.
saved=""
if [ -f "$settings" ]; then
    saved="$(mktemp)"
    cp "$settings" "$saved"
fi

echo "Copying the agent to $target..."
mkdir -p "$target" /Library/Logs/MonitorAgent "/Library/Application Support/MonitorAgent"
cp -R "$here/app/." "$target/"
chmod +x "$target/MonitorAgent.Service"
# A downloaded package is quarantined and the arm64 build must be signed; an ad-hoc signature is enough.
xattr -dr com.apple.quarantine "$target" 2>/dev/null || true
codesign --force --sign - "$target/MonitorAgent.Service" 2>/dev/null || true

if [ -n "$saved" ]; then
    /usr/bin/python3 - "$saved" "$settings" <<'EOF' 2>/dev/null || cp "$saved" "$settings"
import json, sys
old = json.load(open(sys.argv[1], encoding="utf-8-sig"))
new = json.load(open(sys.argv[2], encoding="utf-8-sig"))
if "Setting" in old:
    new["Setting"] = old["Setting"]
    with open(sys.argv[2], "w", encoding="utf-8") as f:
        json.dump(new, f, indent=2)
    print("Kept the saved settings.")
EOF
    rm -f "$saved"
fi

# The settings hold the protected database passwords and the access key.
chmod 600 "$settings"

if [ -d "$here/desktop" ]; then
    echo "Installing /Applications/MonitorAgent.app..."
    bash "$here/make-app.sh" "$here" "/Applications/MonitorAgent.app"
fi

install -m 644 "$here/$label.plist" "$plist"
chown root:wheel "$plist"
launchctl bootstrap system "$plist"
launchctl enable "system/$label"
echo "monitoragent installed and running."
echo "Logs: /Library/Logs/MonitorAgent. Data: /Library/Application Support/MonitorAgent."
