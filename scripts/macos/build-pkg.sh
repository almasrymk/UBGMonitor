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
    cp /usr/local/monitoragent/appsettings.json "/Library/Application Support/MonitorAgent/appsettings.previous.json"
fi
launchctl bootout system/$label 2>/dev/null || true
exit 0
EOF

cat > "$scripts/postinstall" <<EOF
#!/bin/bash
settings=/usr/local/monitoragent/appsettings.json
saved="/Library/Application Support/MonitorAgent/appsettings.previous.json"
mkdir -p /Library/Logs/MonitorAgent "/Library/Application Support/MonitorAgent"
if [ -f "\$saved" ]; then
    /usr/bin/python3 - "\$saved" "\$settings" <<'PY' 2>/dev/null || cp "\$saved" "\$settings"
import json, sys
old = json.load(open(sys.argv[1], encoding="utf-8-sig"))
new = json.load(open(sys.argv[2], encoding="utf-8-sig"))
if "Setting" in old:
    new["Setting"] = old["Setting"]
    with open(sys.argv[2], "w", encoding="utf-8") as f:
        json.dump(new, f, indent=2)
PY
    rm -f "\$saved"
fi
chmod 600 "\$settings"
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
