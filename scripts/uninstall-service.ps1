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
$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if (-not $existing) {
    Write-Host "$serviceName is not installed."
    exit
}

if ($existing.Status -ne "Stopped") {
    Stop-Service -Name $serviceName -Force
    $existing.WaitForStatus("Stopped", [TimeSpan]::FromSeconds(30))
}
sc.exe delete $serviceName | Out-Null
if ($Purge) {
    foreach ($group in @("MonitorAgent Admins", "MonitorAgent Viewers")) {
        if (Get-LocalGroup -Name $group -ErrorAction SilentlyContinue) { Remove-LocalGroup -Name $group }
    }
}
Write-Host "$serviceName removed. Files in C:\ProgramData\MonitorAgent were left in place."
