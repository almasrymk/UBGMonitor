# MonitorAgent Windows Service install / update (elevates itself to Administrator)
$ErrorActionPreference = "Stop"

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Start-Process powershell -Verb RunAs -ArgumentList "-NoProfile -ExecutionPolicy Bypass -NoExit -File `"$PSCommandPath`""
    exit
}

$serviceName = "MonitorAgent"
$publish = "C:\ProgramData\MonitorAgent\publish"
$root = Split-Path -Parent $PSScriptRoot
$settingsPath = Join-Path $publish "appsettings.json"

# Before the rename the service was "ClientAgentService" in C:\ProgramData\ClientAgent: remove it and move its data here.
$legacy = Get-Service -Name "ClientAgentService" -ErrorAction SilentlyContinue
if ($legacy) {
    Write-Host "Removing the old ClientAgentService..."
    if ($legacy.Status -ne "Stopped") {
        Stop-Service -Name "ClientAgentService" -Force
        $legacy.WaitForStatus("Stopped", [TimeSpan]::FromSeconds(30))
    }
    sc.exe delete "ClientAgentService" | Out-Null
    while (Get-Service -Name "ClientAgentService" -ErrorAction SilentlyContinue) { Start-Sleep -Milliseconds 500 }
}
netsh advfirewall firewall delete rule name="UBG Monitor Agent API" | Out-Null
$legacyFolder = "C:\ProgramData\ClientAgent"
if ((Test-Path $legacyFolder) -and -not (Test-Path "C:\ProgramData\MonitorAgent")) {
    Write-Host "Moving $legacyFolder to C:\ProgramData\MonitorAgent..."
    Move-Item $legacyFolder "C:\ProgramData\MonitorAgent"
    Get-ChildItem $publish -Filter "ClientAgent.*" -File -ErrorAction SilentlyContinue | Remove-Item -Force
}

$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($existing) {
    if ($existing.Status -ne "Stopped") {
        Write-Host "Stopping $serviceName..."
        Stop-Service -Name $serviceName -Force
        $existing.WaitForStatus("Stopped", [TimeSpan]::FromSeconds(30))
    }
    sc.exe delete $serviceName | Out-Null
    while (Get-Service -Name $serviceName -ErrorAction SilentlyContinue) { Start-Sleep -Milliseconds 500 }
}

# The app saves its settings into the "Setting" section of the published appsettings.json; publishing must not wipe them.
$savedSetting = $null
if (Test-Path $settingsPath) {
    $savedSetting = (Get-Content $settingsPath -Raw | ConvertFrom-Json).Setting
}

Write-Host "Publishing to $publish..."
dotnet publish "$root\src\MonitorAgent.Service\MonitorAgent.Service.csproj" -c Release -o $publish --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

if ($savedSetting) {
    $settings = Get-Content $settingsPath -Raw | ConvertFrom-Json
    $settings | Add-Member -NotePropertyName Setting -NotePropertyValue $savedSetting -Force
    $settings | ConvertTo-Json -Depth 100 | Set-Content $settingsPath -Encoding UTF8
    Write-Host "Kept the saved settings."
}

sc.exe create $serviceName binPath= "`"$publish\MonitorAgent.Service.exe`"" start= delayed-auto DisplayName= "MonitorAgent" | Out-Null
sc.exe description $serviceName "MonitorAgent background service" | Out-Null
# Restart automatically if the service crashes or exits with an error.
sc.exe failure $serviceName reset= 86400 actions= restart/5000/restart/10000/restart/30000 | Out-Null
sc.exe failureflag $serviceName 1 | Out-Null

Start-Service -Name $serviceName
Get-Service -Name $serviceName | Format-Table -AutoSize Name, DisplayName, Status, StartType
Write-Host "$serviceName installed and running. Manage it from services.msc."
