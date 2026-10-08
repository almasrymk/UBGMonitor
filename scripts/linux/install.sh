#!/usr/bin/env bash
# MonitorAgent install / update for Linux (systemd). Run from the extracted package: sudo bash install.sh
set -euo pipefail

if [ "$(id -u)" -ne 0 ]; then
    exec sudo bash "$0" "$@"
fi

here="$(cd "$(dirname "$0")" && pwd)"
target=/opt/monitoragent
unit=/etc/systemd/system/monitoragent.service
settings="$target/appsettings.json"

getent group monitoragent >/dev/null || groupadd --system monitoragent
getent group monitoragent-admin >/dev/null || groupadd --system monitoragent-admin
if [ -n "${SUDO_USER:-}" ] && [ "$SUDO_USER" != root ]; then
    usermod -aG monitoragent-admin,monitoragent "$SUDO_USER"
    echo "Sign out and in to apply MonitorAgent group membership."
fi

if systemctl is-active --quiet monitoragent 2>/dev/null; then
    echo "Stopping monitoragent..."
    systemctl stop monitoragent
fi

# The app saves its settings into the "Setting" section of appsettings.json; an update must not wipe them.
saved=""
if [ -f "$settings" ]; then
    saved="$(mktemp)"
    cp "$settings" "$saved"
fi

echo "Copying the agent to $target..."
mkdir -p "$target"
cp -a "$here/app/." "$target/"
chmod +x "$target/MonitorAgent.Service"

if [ -n "$saved" ]; then
    if command -v python3 >/dev/null 2>&1; then
        python3 - "$saved" "$settings" <<'EOF'
import json, sys
old = json.load(open(sys.argv[1], encoding="utf-8-sig"))
new = json.load(open(sys.argv[2], encoding="utf-8-sig"))
if "Setting" in old:
    new["Setting"] = old["Setting"]
    with open(sys.argv[2], "w", encoding="utf-8") as f:
        json.dump(new, f, indent=2)
    print("Kept the saved settings.")
EOF
    else
        cp "$saved" "$settings"
        echo "Kept the previous settings file (python3 is not installed to merge it with the new one)."
    fi
    rm -f "$saved"
fi

# The settings hold the protected database passwords and the access key.
chmod 600 "$settings"

if [ -d "$here/desktop" ]; then
    echo "Copying the MonitorAgent app to /opt/monitoragent-desktop..."
    rm -rf /opt/monitoragent-desktop
    mkdir -p /opt/monitoragent-desktop
    cp -a "$here/desktop/." /opt/monitoragent-desktop/
    chmod +x /opt/monitoragent-desktop/MonitorAgent
    install -D -m 644 "$here/monitoragent.png" /usr/share/pixmaps/monitoragent.png
    install -D -m 644 "$here/monitoragent.desktop" /usr/share/applications/monitoragent.desktop
    ln -sf /opt/monitoragent-desktop/MonitorAgent /usr/local/bin/monitoragent
    if command -v update-desktop-database >/dev/null 2>&1; then
        update-desktop-database -q /usr/share/applications || true
    fi
fi

install -m 644 "$here/monitoragent.service" "$unit"
systemctl daemon-reload
systemctl enable --now monitoragent
systemctl --no-pager --lines=0 status monitoragent || true
echo "monitoragent installed and running."
echo "Logs: /var/log/monitoragent and 'journalctl -u monitoragent'. Data: /var/lib/monitoragent."
if [ -d "$here/desktop" ]; then
    echo "Open MonitorAgent from the applications menu, or run: monitoragent"
fi
