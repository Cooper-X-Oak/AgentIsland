$ErrorActionPreference = "Stop"
$HooksPath = Join-Path $env:USERPROFILE ".gemini\config\hooks.json"
if (-not (Test-Path $HooksPath)) {
    Write-Host "NOT_FOUND: $HooksPath"
    exit 0
}

$raw = Get-Content $HooksPath -Raw -Encoding UTF8
if ([string]::IsNullOrWhiteSpace($raw)) { exit 0 }

$parsed = $raw | ConvertFrom-Json
$rootObj = @{}
foreach ($prop in $parsed.PSObject.Properties) {
    if ($prop.Name -ne "agent-island") {
        $rootObj[$prop.Name] = $prop.Value
    }
}

$json = $rootObj | ConvertTo-Json -Depth 10
[System.IO.File]::WriteAllText($HooksPath, $json + "`n", (New-Object System.Text.UTF8Encoding($false)))
Write-Host "ANTIGRAVITY_ADAPTER_REMOVED"
