param([switch]$Purge)
# MonitorAgent Windows Service uninstall (elevates itself to Administrator)
$ErrorActionPreference = "Stop"

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    $purgeArgument = if ($Purge) { " -Purge" } else { "" }
    Start-Process powershell -WindowStyle Hidden -Verb RunAs -ArgumentList "-NoProfile -File `"$PSCommandPath`"$purgeArgument"
    exit
}

$serviceName = "MonitorAgent"
Get-NetFirewallRule -DisplayName 'MonitorAgent API' -ErrorAction SilentlyContinue | Remove-NetFirewallRule
$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if (-not $existing) {
    Write-Host "$serviceName is not installed."
}

if ($existing -and $existing.Status -ne "Stopped") {
    Stop-Service -Name $serviceName -Force
    $existing.WaitForStatus("Stopped", [TimeSpan]::FromSeconds(30))
}
if ($existing) { sc.exe delete $serviceName | Out-Null }
if ($Purge) {
    foreach ($group in @("MonitorAgent Admins", "MonitorAgent Viewers")) {
        if (Get-LocalGroup -Name $group -ErrorAction SilentlyContinue) { Remove-LocalGroup -Name $group }
    }
    $state = [IO.Path]::GetFullPath((Join-Path $env:ProgramData 'MonitorAgent'))
    if ((Split-Path $state -Leaf) -ne 'MonitorAgent' -or (Split-Path $state -Parent) -ne [IO.Path]::GetFullPath($env:ProgramData).TrimEnd('\')) { throw 'Unsafe purge path.' }
    if (Test-Path -LiteralPath $state) { Remove-Item -LiteralPath $state -Recurse -Force }
}
Write-Host "$serviceName removed. Files in C:\ProgramData\MonitorAgent were left in place."
