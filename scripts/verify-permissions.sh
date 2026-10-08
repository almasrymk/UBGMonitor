#!/usr/bin/env bash
set -euo pipefail
failed=0
if [ "$(uname -s)" = Darwin ]; then
    roots=(/usr/local/monitoragent /Applications/MonitorAgent.app "/Library/Application Support/MonitorAgent" /Library/Logs/MonitorAgent)
    state="/Library/Application Support/MonitorAgent"
else
    roots=(/opt/monitoragent /opt/monitoragent-desktop /var/lib/monitoragent /var/log/monitoragent)
    state=/var/lib/monitoragent
fi
if [ "$#" -gt 0 ]; then
    if [ "$#" != 4 ]; then echo 'Usage: verify-permissions.sh [install desktop state logs]'; exit 2; fi
    roots=("$1" "$2" "$3" "$4"); state="$3"
    for root in "${roots[@]}"; do
        if [[ "$root" != /* || "$root" = / ]]; then echo 'FAIL roots must be absolute directories below /'; exit 2; fi
    done
fi
logs="${roots[3]}"
for root in "${roots[@]}"; do
    if [ ! -d "$root" ]; then echo "FAIL missing $root"; failed=1; continue; fi
    while IFS= read -r -d '' path; do
        if [ -L "$path" ]; then echo "FAIL symlink $path"; failed=1; continue; fi
        if [ "$(uname -s)" = Darwin ]; then
            uid=$(stat -f '%u' "$path"); mode=$(stat -f '%Lp' "$path")
        else
            uid=$(stat -c '%u' "$path"); mode=$(stat -c '%a' "$path")
        fi
        mask=$((8#$mode))
        if [ "$uid" != 0 ] || ((mask & 0022)) || { [[ "$path" = "$state" || "$path" = "$state/"* ]] && ((mask & 0077)); } || { [[ "$path" = "$logs" || "$path" = "$logs/"* ]] && ((mask & 0007)); }; then
            echo "FAIL $path"; failed=1
        else echo "PASS $path"; fi
    done < <(find "$root" -print0)
done
exit "$failed"
