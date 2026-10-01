#!/usr/bin/env bash
# Removes the MonitorAgent. Add --purge to also delete its data and logs.
set -euo pipefail

if [ "$(id -u)" -ne 0 ]; then
    exec sudo bash "$0" "$@"
fi

systemctl disable --now monitoragent 2>/dev/null || true
rm -f /etc/systemd/system/monitoragent.service
systemctl daemon-reload

# Close the API port the agent opened in the firewall.
port_file=/var/lib/monitoragent/firewall-port
if [ -f "$port_file" ]; then
    port="$(cat "$port_file")"
    if command -v ufw >/dev/null 2>&1; then
        ufw delete allow "$port/tcp" >/dev/null 2>&1 || true
    fi
    if command -v firewall-cmd >/dev/null 2>&1; then
        firewall-cmd --permanent --remove-port="$port/tcp" >/dev/null 2>&1 && firewall-cmd --reload >/dev/null 2>&1 || true
    fi
    rm -f "$port_file"
fi

rm -rf /opt/monitoragent /opt/monitoragent-desktop
rm -f /usr/share/applications/monitoragent.desktop /usr/share/pixmaps/monitoragent.png /usr/local/bin/monitoragent
if [ "${1:-}" = "--purge" ]; then
    rm -rf /var/lib/monitoragent /var/log/monitoragent
    echo "monitoragent removed with its data and logs."
else
    echo "monitoragent removed. Its data is still in /var/lib/monitoragent (run with --purge to delete it)."
fi
