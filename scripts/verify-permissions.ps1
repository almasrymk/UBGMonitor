param([string]$InstallRoot = (Join-Path $env:ProgramFiles 'MonitorAgent'), [string]$StateRoot = (Join-Path $env:ProgramData 'MonitorAgent'))
$ErrorActionPreference = 'Stop'
$failed = $false
$trusted = @('S-1-5-18', 'S-1-5-32-544', 'S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464')
$writes = [Security.AccessControl.FileSystemRights]'WriteData,AppendData,WriteExtendedAttributes,WriteAttributes,Delete,DeleteSubdirectoriesAndFiles,ChangePermissions,TakeOwnership'
foreach ($root in @($InstallRoot, $StateRoot)) {
    if (-not (Test-Path -LiteralPath $root)) { Write-Host "FAIL missing $root"; $failed = $true; continue }
    foreach ($item in @((Get-Item -LiteralPath $root -Force)) + @(Get-ChildItem -LiteralPath $root -Recurse -Force)) {
        $safe = -not ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)
        $acl = Get-Acl -LiteralPath $item.FullName
        if ($acl.Owner -and ([Security.Principal.NTAccount]::new($acl.Owner).Translate([Security.Principal.SecurityIdentifier]).Value -notin $trusted)) { $safe = $false }
        foreach ($rule in $acl.Access) {
            if ($rule.AccessControlType -ne 'Allow') { continue }
            $sid = $rule.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value
            if ($sid -notin $trusted -and ($root -eq $StateRoot -or ($rule.FileSystemRights -band $writes))) { $safe = $false }
        }
        if ($safe) { Write-Host "PASS $($item.FullName)" } else { Write-Host "FAIL $($item.FullName)"; $failed = $true }
    }
}
if ($failed) { exit 1 }
exit 0
