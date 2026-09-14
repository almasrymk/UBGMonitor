# UBG Monitor Windows Service install (run as Administrator)
$ErrorActionPreference = "Stop"
$publish = "C:\ProgramData\ClientAgent\publish"
$root = Split-Path -Parent $PSScriptRoot
if (-not $PSScriptRoot) { $root = Get-Location }

dotnet publish "$root\src\ClientAgent.Service\ClientAgent.Service.csproj" -c Release -o $publish
sc.exe create ClientAgentService binPath= "$publish\ClientAgent.Service.exe" start= auto
sc.exe description ClientAgentService "UBG Monitor client agent"
sc.exe start ClientAgentService
Write-Host "ClientAgentService installed and started."
