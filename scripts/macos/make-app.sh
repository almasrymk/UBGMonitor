#!/bin/bash
# Builds "MonitorAgent.app" from the published desktop files: bash make-app.sh <package folder> <path of the .app>
set -euo pipefail

here="$1"
app="$2"

rm -rf "$app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
cp -R "$here/desktop/." "$app/Contents/MacOS/"
chmod +x "$app/Contents/MacOS/MonitorAgent"
cp "$here/Info.plist" "$app/Contents/Info.plist"
sips -s format icns "$here/monitoragent.png" --out "$app/Contents/Resources/MonitorAgent.icns" >/dev/null 2>&1 || true

# A downloaded package is quarantined and arm64 code must be signed; an ad-hoc signature is enough on this Mac.
xattr -dr com.apple.quarantine "$app" 2>/dev/null || true
codesign --force --deep --sign - "$app" 2>/dev/null \
    || codesign --force --sign - "$app/Contents/MacOS/MonitorAgent" 2>/dev/null \
    || true
