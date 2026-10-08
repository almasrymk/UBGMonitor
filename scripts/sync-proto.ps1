# Copies agent.proto from a MonitorAgentPlatform checkout and records its hash in proto/VERSION (AG-4).
#   pwsh scripts/sync-proto.ps1 -Platform ../MonitorAgentPlatform
# The test ProtocolFileTests fails when proto/monitor/agent/v1/agent.proto and proto/VERSION disagree.
param(
    [Parameter(Mandatory = $true)][string]$Platform
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$source = Join-Path $Platform 'proto/monitor/agent/v1/agent.proto'
if (-not (Test-Path $source)) {
    throw "Not found: $source"
}

$target = Join-Path $root 'proto/monitor/agent/v1/agent.proto'
New-Item -ItemType Directory -Force (Split-Path -Parent $target) | Out-Null
$text = [IO.File]::ReadAllText($source).Replace("`r`n", "`n")
[IO.File]::WriteAllText($target, $text, [Text.UTF8Encoding]::new($false))

# The hash is taken over the LF form so it is the same on every system.
$hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($text))).ToLowerInvariant()
$version = "agent.proto sha256 $hash`nsource almasrymk/MonitorAgentPlatform proto/monitor/agent/v1/agent.proto (hash of the file with LF line endings)`n"
[IO.File]::WriteAllText((Join-Path $root 'proto/VERSION'), $version, [Text.UTF8Encoding]::new($false))
Write-Output "agent.proto synced, sha256 $hash"
