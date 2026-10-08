#!/bin/bash
# Removes the MonitorAgent. Add --purge to also delete its data and logs.
set -euo pipefail

if [ "$(id -u)" -ne 0 ]; then
    exec sudo bash "$0" "$@"
fi

label=com.ubg.monitoragent
launchctl bootout "system/$label" 2>/dev/null || true
rm -f "/Library/LaunchDaemons/$label.plist"
/usr/libexec/ApplicationFirewall/socketfilterfw --remove /usr/local/monitoragent/MonitorAgent.Service >/dev/null 2>&1 || true
rm -rf /usr/local/monitoragent "/Applications/MonitorAgent.app"

if [ "${1:-}" = "--purge" ]; then
    dseditgroup -o delete monitoragent-admin 2>/dev/null || true
    dseditgroup -o delete monitoragent 2>/dev/null || true
    rm -rf "/Library/Application Support/MonitorAgent" /Library/Logs/MonitorAgent
    echo "monitoragent removed with its data and logs."
else
    echo "monitoragent removed. Its data is still in /Library/Application Support/MonitorAgent (run with --purge to delete it)."
fi
