param([string]$ReportPath = 'TestResults/dependency-vulnerabilities.json')
$ErrorActionPreference = 'Stop'
$reportDirectory = Split-Path -Parent $ReportPath
if ($reportDirectory) { New-Item -ItemType Directory -Force $reportDirectory | Out-Null }
$output = & dotnet list MonitorAgent.sln package --vulnerable --include-transitive --format json --output-version 1
if ($LASTEXITCODE -ne 0) { throw 'Dependency audit failed.' }
$output | Set-Content $ReportPath -Encoding utf8
$report = ($output -join "`n") | ConvertFrom-Json
$failures = @()
foreach ($project in $report.projects) {
    foreach ($framework in $project.frameworks) {
        foreach ($package in @($framework.topLevelPackages) + @($framework.transitivePackages)) {
            foreach ($vulnerability in $package.vulnerabilities) {
                if ($vulnerability.severity -in @('High', 'Critical')) {
                    $failures += "$($package.id) $($package.resolvedVersion): $($vulnerability.severity) $($vulnerability.advisoryurl)"
                }
            }
        }
    }
}
if ($failures.Count) { throw ($failures -join "`n") }
Write-Output 'PASS: no reported High or Critical dependency vulnerabilities.'
