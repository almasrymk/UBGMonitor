#!/bin/bash
# Builds a macOS installer package (.pkg) from this extracted folder. Run on a Mac: bash build-pkg.sh [version]
# The .pkg installs the agent (launchd daemon) and "MonitorAgent.app"; double-click it or: sudo installer -pkg <file> -target /
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
version="${1:-$(/usr/libexec/PlistBuddy -c 'Print CFBundleShortVersionString' "$here/Info.plist")}"
label=com.ubg.monitoragent
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

root="$work/root"
scripts="$work/scripts"
mkdir -p "$root/usr/local/monitoragent" "$root/Library/LaunchDaemons" "$root/Applications" "$scripts"
cp -R "$here/app/." "$root/usr/local/monitoragent/"
chmod +x "$root/usr/local/monitoragent/MonitorAgent.Service"
codesign --force --sign - "$root/usr/local/monitoragent/MonitorAgent.Service" 2>/dev/null || true
cp "$here/$label.plist" "$root/Library/LaunchDaemons/$label.plist"
bash "$here/make-app.sh" "$here" "$root/Applications/MonitorAgent.app"

cat > "$scripts/preinstall" <<EOF
#!/bin/bash
# Keep the settings the app saved; the package replaces appsettings.json.
if [ -f /usr/local/monitoragent/appsettings.json ]; then
    mkdir -p "/Library/Application Support/MonitorAgent"
    umask 0077
    chown root:wheel "/Library/Application Support/MonitorAgent"
    chmod 700 "/Library/Application Support/MonitorAgent"
    if [ ! -f "/Library/Application Support/MonitorAgent/settings.json" ] && [ ! -f "/Library/Application Support/MonitorAgent/appsettings.previous.json" ]; then
        install -o root -g wheel -m 600 /usr/local/monitoragent/appsettings.json "/Library/Application Support/MonitorAgent/appsettings.previous.json"
    fi
fi
launchctl bootout system/$label 2>/dev/null || true
exit 0
EOF

cat > "$scripts/postinstall" <<EOF
#!/bin/bash
set -e
for group in monitoragent monitoragent-admin; do
    dscl . -read "/Groups/\$group" >/dev/null 2>&1 || dseditgroup -o create "\$group"
    if [ -n "\${SUDO_USER:-}" ] && [ "\$SUDO_USER" != root ]; then
        dseditgroup -o edit -a "\$SUDO_USER" -t user "\$group"
    fi
done
echo "Sign out and in to apply MonitorAgent group membership."
umask 0077
mkdir -p /Library/Logs/MonitorAgent "/Library/Application Support/MonitorAgent"
chown -R root:wheel /usr/local/monitoragent /Applications/MonitorAgent.app "/Library/Application Support/MonitorAgent" /Library/Logs/MonitorAgent
chmod -R go+rX,go-w /usr/local/monitoragent /Applications/MonitorAgent.app
chmod 700 "/Library/Application Support/MonitorAgent"
chmod 750 /Library/Logs/MonitorAgent
find "/Library/Application Support/MonitorAgent" -type f -exec chmod 600 {} +
chown root:wheel /Library/LaunchDaemons/$label.plist
chmod 644 /Library/LaunchDaemons/$label.plist
launchctl bootstrap system /Library/LaunchDaemons/$label.plist
launchctl enable system/$label
exit 0
EOF
chmod +x "$scripts/preinstall" "$scripts/postinstall"

output="$here/../MonitorAgent-$version-$(basename "$here" | sed 's/^monitoragent-//').pkg"
pkgbuild --root "$root" --scripts "$scripts" --identifier com.ubg.monitoragent.app --version "$version" \
    --install-location / "$output"
echo "Created $output"
