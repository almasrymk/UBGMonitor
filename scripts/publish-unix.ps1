# Builds the Linux and macOS packages: the agent service and the MonitorAgent desktop app, self-contained
# (the computer does not need .NET installed).
# Output in artifacts\:
#   monitoragent-<rid>.tar.gz               every runtime: tar -xzf <file> && sudo bash monitoragent-<rid>/install.sh
#   monitoragent_<version>_<arch>.deb  Debian / Ubuntu: sudo apt install ./<file>
# RHEL / Fedora / SUSE use the tar.gz. On a Mac, monitoragent-osx-<arch>/build-pkg.sh turns the tar.gz into a .pkg.
param(
    [string[]]$Runtimes = @("linux-x64", "linux-arm64", "osx-x64", "osx-arm64"),
    [string]$Version = "1.0.0"
)
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root "artifacts"
New-Item -ItemType Directory -Force -Path $out | Out-Null

$packagerDir = Join-Path $out ".packager"
dotnet build "$root\tools\Packager\Packager.csproj" -c Release -o $packagerDir --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "Building the packager failed." }
$packager = Join-Path $packagerDir "Packager.dll"

function Invoke-Packager([string[]]$Arguments) {
    dotnet $packager @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Packager failed: $Arguments" }
}

function Copy-Text([string]$Source, [string]$Target, [hashtable]$Values = @{}) {
    $text = [IO.File]::ReadAllText($Source)
    foreach ($key in $Values.Keys) { $text = $text.Replace("{$key}", $Values[$key]) }
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Target) | Out-Null
    [IO.File]::WriteAllText($Target, $text.Replace("`r`n", "`n"), (New-Object System.Text.UTF8Encoding($false)))
}

$icon = "$root\src\MonitorAgent.UI\Assets\monitoragent.png"

foreach ($rid in $Runtimes) {
    $name = "monitoragent-$rid"
    $stage = Join-Path $out $name
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }

    Write-Host "Publishing $rid..."
    dotnet publish "$root\src\MonitorAgent.Service\MonitorAgent.Service.csproj" -c Release -r $rid --self-contained true -o "$stage\app" --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "Publishing the service failed for $rid." }
    dotnet publish "$root\src\MonitorAgent.Desktop\MonitorAgent.Desktop.csproj" -c Release -r $rid --self-contained true -o "$stage\desktop" --nologo -v q -p:Version=$Version
    if ($LASTEXITCODE -ne 0) { throw "Publishing the desktop app failed for $rid." }
    Copy-Item $icon "$stage\monitoragent.png"

    if ($rid.StartsWith("linux")) {
        foreach ($file in "install.sh", "uninstall.sh", "monitoragent.service", "monitoragent.desktop") {
            Copy-Text "$PSScriptRoot\linux\$file" "$stage\$file"
        }

        $arch = if ($rid -eq "linux-arm64") { "arm64" } else { "amd64" }
        $deb = Join-Path $out "deb-$rid"
        if (Test-Path $deb) { Remove-Item $deb -Recurse -Force }
        Copy-Text "$PSScriptRoot\linux\deb\control" "$deb\DEBIAN\control" @{ VERSION = $Version; ARCH = $arch }
        foreach ($script in "preinst", "postinst", "prerm", "postrm") {
            Copy-Text "$PSScriptRoot\linux\deb\$script" "$deb\DEBIAN\$script"
        }
        Copy-Item "$stage\app" "$deb\opt\monitoragent" -Recurse
        Copy-Item "$stage\desktop" "$deb\opt\monitoragent-desktop" -Recurse
        Copy-Text "$PSScriptRoot\linux\monitoragent.service" "$deb\lib\systemd\system\monitoragent.service"
        Copy-Text "$PSScriptRoot\linux\monitoragent.desktop" "$deb\usr\share\applications\monitoragent.desktop"
        New-Item -ItemType Directory -Force -Path "$deb\usr\share\pixmaps" | Out-Null
        Copy-Item $icon "$deb\usr\share\pixmaps\monitoragent.png"
        Invoke-Packager @("deb", $deb, (Join-Path $out "monitoragent_$($Version)_$arch.deb"))
        Remove-Item $deb -Recurse -Force
    }
    else {
        foreach ($file in "install.sh", "uninstall.sh", "make-app.sh", "build-pkg.sh", "com.ubg.monitoragent.plist") {
            Copy-Text "$PSScriptRoot\macos\$file" "$stage\$file"
        }
        Copy-Text "$PSScriptRoot\macos\Info.plist" "$stage\Info.plist" @{ VERSION = $Version }
    }

    $archive = Join-Path $out "$name.tar.gz"
    if (Test-Path $archive) { Remove-Item $archive -Force }
    Invoke-Packager @("tar", $stage, $archive, $name)
}
