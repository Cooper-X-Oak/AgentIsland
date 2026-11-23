$ErrorActionPreference = "Stop"
$SettingsPath = Join-Path $env:USERPROFILE ".claude\settings.json"
if (-not (Test-Path $SettingsPath)) { exit 0 }

$raw = Get-Content $SettingsPath -Raw -Encoding UTF8
if ([string]::IsNullOrWhiteSpace($raw)) { exit 0 }

$parsed = $raw | ConvertFrom-Json
$rootObj = @{}
foreach ($prop in $parsed.PSObject.Properties) {
    $rootObj[$prop.Name] = $prop.Value
}

if ($rootObj.ContainsKey("hooks") -and $rootObj["hooks"]) {
    $hooksObj = @{}
    foreach ($prop in $rootObj["hooks"].PSObject.Properties) {
        $filtered = @($prop.Value | Where-Object { ($_ | ConvertTo-Json -Depth 5 -Compress) -notmatch "claude-hook\.ps1" })
        if ($filtered.Count -gt 0) {
            $hooksObj[$prop.Name] = $filtered
        }
    }
    $rootObj["hooks"] = $hooksObj
}

$json = $rootObj | ConvertTo-Json -Depth 10
[System.IO.File]::WriteAllText($SettingsPath, $json + "`n", (New-Object System.Text.UTF8Encoding($false)))
Write-Host "CLAUDE_CODE_ADAPTER_REMOVED"
