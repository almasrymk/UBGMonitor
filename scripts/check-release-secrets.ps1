#requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$PublishPath)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $PublishPath).Path
if (-not (Test-Path -LiteralPath $root -PathType Container)) { throw 'PublishPath must be a directory.' }
$settingsFiles = @(Get-ChildItem -LiteralPath $root -Filter 'appsettings*.json' -File -Recurse)
if (-not ($settingsFiles | Where-Object Name -eq 'appsettings.json')) { throw 'Release has no appsettings.json; scan failed closed.' }
$violations = [Collections.Generic.List[string]]::new()

function Test-SettingsNode($Node, [string]$FieldPath, [string]$FileName) {
    if ($Node -is [Collections.IDictionary]) {
        foreach ($name in $Node.Keys) {
            $value = $Node[$name]
            $field = "$FieldPath$name"
            if ($name -match '^(Setting|Ui|MonitorPoints)$') {
                $violations.Add("${FileName}:$field (installation settings)")
            }
            if ($name -match '(?i)(password|secret|token|accesskey|connectionstring|privatekey)$|^ClientId$' -and
                $null -ne $value -and -not [string]::IsNullOrWhiteSpace([string]$value)) {
                $violations.Add("${FileName}:$field (non-empty credential field)")
            }
            if ($value -is [string] -and $value -match '(?:\b(?:lcs|lc)_[A-Za-z0-9_-]{16,}|(?:dpapi2?|aes):[A-Za-z0-9+/=_-]{16,})') {
                $violations.Add("${FileName}:$field (credential blob)")
            }
            Test-SettingsNode $value "$field." $FileName
        }
    } elseif ($Node -is [Collections.IEnumerable] -and $Node -isnot [string]) {
        $index = 0
        foreach ($item in $Node) { Test-SettingsNode $item "$FieldPath[$index]." $FileName; $index++ }
    }
}

foreach ($file in $settingsFiles) {
    try { $settings = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json -AsHashtable }
    catch { throw "Invalid settings JSON: $($file.Name)" }
    if ($settings -isnot [Collections.IDictionary]) { throw "Settings must be an object: $($file.Name)" }
    Test-SettingsNode $settings '' $file.Name
}
if ($violations.Count -gt 0) {
    foreach ($violation in ($violations | Select-Object -Unique)) { Write-Error $violation -ErrorAction Continue }
    throw 'Release secret scan failed. Values are deliberately omitted.'
}
Write-Output "Release secret scan passed ($($settingsFiles.Count) settings files)."
