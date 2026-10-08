#requires -Version 7.0
$ErrorActionPreference = 'Stop'
$scanner = Join-Path $PSScriptRoot 'check-release-secrets.ps1'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('monitor-release-scan-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
$fixture = 'TEST-ONLY-DO-NOT-PRINT'
$cases = @(
    @{ Name = 'clean'; Json = '{"LicensingClient":{"ClientId":"","ClientSecret":""}}'; Pass = $true },
    @{ Name = 'client-secret'; Json = '{"LicensingClient":{"ClientSecret":"TEST-ONLY-DO-NOT-PRINT"}}'; Pass = $false },
    @{ Name = 'nested-password'; Json = '{"Nested":{"Password":"TEST-ONLY-DO-NOT-PRINT"}}'; Pass = $false },
    @{ Name = 'api-key'; Json = '{"Nested":{"RemoteAccessKey":"TEST-ONLY-DO-NOT-PRINT"}}'; Pass = $false },
    @{ Name = 'settings'; Json = '{"Setting":{}}'; Pass = $false },
    @{ Name = 'points'; Json = '{"MonitorPoints":[]}'; Pass = $false },
    @{ Name = 'blob'; Json = '{"Other":"aes:AAAAAAAAAAAAAAAAAAAA"}'; Pass = $false },
    @{ Name = 'malformed'; Json = '{'; Pass = $false }
)
try {
    foreach ($case in $cases) {
        $directory = Join-Path $testRoot $case.Name
        New-Item -ItemType Directory -Path $directory | Out-Null
        $case.Json | Set-Content -LiteralPath (Join-Path $directory 'appsettings.json') -Encoding utf8
        $output = & pwsh -NoProfile -File $scanner -PublishPath $directory 2>&1
        $passed = $LASTEXITCODE -eq 0
        if ($passed -ne $case.Pass) { throw "Unexpected scanner result for $($case.Name)." }
        if (($output | Out-String).Contains($fixture)) { throw 'Scanner exposed a fixture secret.' }
    }
    # A secret in a secondary environment settings file must also be detected.
    $secondary = Join-Path $testRoot 'clean'
    '{"ClientSecret":"TEST-ONLY-DO-NOT-PRINT"}' | Set-Content -LiteralPath (Join-Path $secondary 'appsettings.Development.json') -Encoding utf8
    $output = & pwsh -NoProfile -File $scanner -PublishPath $secondary 2>&1
    if ($LASTEXITCODE -eq 0 -or ($output | Out-String).Contains($fixture)) { throw 'Secondary settings protection failed.' }
    Write-Output 'Release scanner tests passed (9 cases; secret values omitted).'
}
finally {
    $resolved = [IO.Path]::GetFullPath($testRoot)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -or
        -not [IO.Path]::GetFileName($resolved).StartsWith('monitor-release-scan-')) { throw 'Unsafe cleanup path.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
