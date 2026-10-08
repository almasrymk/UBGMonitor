param([string]$Source, [string]$DesktopSource, [switch]$BuildFromSource)
# Run from an elevated PowerShell. Customers install prebuilt packages; SDK is optional.
$ErrorActionPreference = 'Stop'
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run this installer from an Administrator PowerShell.' }
$serviceName = 'MonitorAgent'
$state = Join-Path $env:ProgramData 'MonitorAgent'
$installRoot = Join-Path $env:ProgramFiles 'MonitorAgent'
$target = Join-Path $installRoot 'Service'
$legacy = Join-Path $state 'publish'
if ($BuildFromSource) {
    if ($Source) { throw 'Use either -Source or -BuildFromSource.' }
    $Source = Join-Path $env:TEMP ('MonitorAgent.Publish.' + [guid]::NewGuid().ToString('N'))
    dotnet publish (Join-Path (Split-Path $PSScriptRoot -Parent) 'src/MonitorAgent.Service/MonitorAgent.Service.csproj') -c Release -o $Source
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
}
if (-not $Source -or -not (Test-Path -LiteralPath (Join-Path $Source 'MonitorAgent.Service.exe'))) { throw 'Specify -Source pointing to a prebuilt Windows service folder.' }
foreach ($candidate in @($Source, $DesktopSource) | Where-Object { $_ }) {
    if (Get-ChildItem -LiteralPath $candidate -Recurse -Force | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }) { throw 'Package cannot contain reparse points.' }
}
function Protect-Tree([string]$Path, [bool]$PublicRead) {
    if (Test-Path -LiteralPath $Path) {
        if ((Get-Item -LiteralPath $Path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Protected path is a reparse point: $Path" }
        if (Get-ChildItem -LiteralPath $Path -Recurse -Force | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }) { throw "Protected tree contains a reparse point: $Path" }
    } else { New-Item -ItemType Directory -Path $Path | Out-Null }
    $items = @((Get-Item -LiteralPath $Path)) + @(Get-ChildItem -LiteralPath $Path -Recurse -Force)
    foreach ($item in $items) {
        $acl = if ($item.PSIsContainer) { [Security.AccessControl.DirectorySecurity]::new() } else { [Security.AccessControl.FileSecurity]::new() }
        $acl.SetAccessRuleProtection($true, $false)
        $acl.SetOwner([Security.Principal.SecurityIdentifier]::new('S-1-5-32-544'))
        $inheritance = if ($item.PSIsContainer) { [Security.AccessControl.InheritanceFlags]'ContainerInherit,ObjectInherit' } else { [Security.AccessControl.InheritanceFlags]::None }
        foreach ($sid in @('S-1-5-18', 'S-1-5-32-544')) {
            $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new([Security.Principal.SecurityIdentifier]::new($sid), 'FullControl', $inheritance, 'None', 'Allow'))
        }
        if ($PublicRead) { $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new([Security.Principal.SecurityIdentifier]::new('S-1-5-32-545'), 'ReadAndExecute', $inheritance, 'None', 'Allow')) }
        Set-Acl -LiteralPath $item.FullName -AclObject $acl
    }
}
foreach ($group in @('MonitorAgent Admins', 'MonitorAgent Viewers')) {
    if (-not (Get-LocalGroup -Name $group -ErrorAction SilentlyContinue)) { New-LocalGroup -Name $group | Out-Null }
}
if (-not (Get-LocalGroupMember -Group 'MonitorAgent Admins' | Where-Object SID -eq $identity.User)) { Add-LocalGroupMember -Group 'MonitorAgent Admins' -Member $identity.User.Value }
Write-Host 'Sign out and in after installation to apply MonitorAgent group membership.'
$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($existing -and $existing.Status -ne 'Stopped') { Stop-Service $serviceName; $existing.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30)) }
Protect-Tree $state $false
Protect-Tree $installRoot $true
Protect-Tree $target $true
# Preserve settings from installations already using ProgramFiles before replacing binaries.
$oldSettings = Join-Path $target 'appsettings.json'
$previous = Join-Path $state 'appsettings.previous.json'
if ((Test-Path -LiteralPath $oldSettings) -and -not (Test-Path -LiteralPath (Join-Path $state 'settings.json')) -and -not (Test-Path -LiteralPath $previous)) { Copy-Item -LiteralPath $oldSettings -Destination $previous }
Copy-Item -Path (Join-Path $Source '*') -Destination $target -Recurse -Force
Protect-Tree $target $true
if ($DesktopSource) {
    $desktopTarget = Join-Path $installRoot 'Desktop'
    Protect-Tree $desktopTarget $true
    Copy-Item -Path (Join-Path $DesktopSource '*') -Destination $desktopTarget -Recurse -Force
    Protect-Tree $desktopTarget $true
}
Protect-Tree $state $false
& (Join-Path $PSScriptRoot 'verify-permissions.ps1') -InstallRoot $installRoot -StateRoot $state
if ($LASTEXITCODE -ne 0) { throw 'Permission verification failed.' }
$binary = '"' + (Join-Path $target 'MonitorAgent.Service.exe') + '"'
if ($existing) { sc.exe config $serviceName binPath= $binary start= delayed-auto | Out-Null }
else { sc.exe create $serviceName binPath= $binary start= delayed-auto DisplayName= 'MonitorAgent' | Out-Null }
if ($LASTEXITCODE -ne 0) { throw 'Service registration failed.' }
sc.exe description $serviceName 'MonitorAgent background service' | Out-Null
sc.exe failure $serviceName reset= 86400 actions= restart/5000/restart/10000/restart/30000 | Out-Null
$marker = Join-Path $state 'layout-migrated'
if (Test-Path -LiteralPath $marker) { Remove-Item -LiteralPath $marker }
Start-Service $serviceName
for ($attempt = 0; $attempt -lt 30 -and -not (Test-Path -LiteralPath $marker); $attempt++) { Start-Sleep -Seconds 1 }
if (-not (Test-Path -LiteralPath $marker)) { throw 'Migration did not complete. Legacy files were preserved; inspect service logs.' }
if (Test-Path -LiteralPath $legacy) {
    $resolvedLegacy = [IO.Path]::GetFullPath($legacy)
    if ($resolvedLegacy -ne [IO.Path]::GetFullPath((Join-Path $state 'publish'))) { throw 'Unsafe legacy cleanup path.' }
    Remove-Item -LiteralPath $resolvedLegacy -Recurse -Force
}
Write-Host 'MonitorAgent installed. State and backups remain in ProgramData; binaries are in ProgramFiles.'
