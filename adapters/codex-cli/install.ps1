param(
    [string]$ConfigPath = ""
)

$ErrorActionPreference = "Stop"

$Root = Resolve-Path (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) "..\..")
$HookExe = Join-Path $Root "bin\island-hook.exe"
if (-not (Test-Path $HookExe)) {
    throw "island-hook.exe not found at $HookExe. Please run .\build.ps1 first."
}

$HooksPath = if ([string]::IsNullOrWhiteSpace($ConfigPath)) {
    Join-Path $env:USERPROFILE ".codex\hooks.json"
} else {
    $ConfigPath
}
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $HooksPath) | Out-Null

$rootObj = @{}
if (Test-Path $HooksPath) {
    $raw = Get-Content $HooksPath -Raw -Encoding UTF8
    if (-not [string]::IsNullOrWhiteSpace($raw)) {
        $parsed = $raw | ConvertFrom-Json
        foreach ($prop in $parsed.PSObject.Properties) {
            $rootObj[$prop.Name] = $prop.Value
        }
    }
}

$hooksObj = @{}
if ($rootObj.ContainsKey("hooks") -and $rootObj["hooks"]) {
    foreach ($prop in $rootObj["hooks"].PSObject.Properties) {
        $hooksObj[$prop.Name] = @($prop.Value)
    }
}

function Add-OrReplaceCodexHook($Map, [string]$EventName) {
    $cmd = "`"$HookExe`" --adapter codex --event $EventName"
    $existing = @()
    if ($Map.ContainsKey($EventName)) {
        $existing = @($Map[$EventName] | Where-Object { ($_ | ConvertTo-Json -Depth 6 -Compress) -notmatch "island-hook\.exe" })
    }
    $entry = @{
        hooks = @(
            @{
                type    = "command"
                command = $cmd
                timeout = 5
            }
        )
    }
    $Map[$EventName] = @($existing + $entry)
}

foreach ($ev in @("UserPromptSubmit", "PermissionRequest", "Stop", "Interrupt")) {
    Add-OrReplaceCodexHook $hooksObj $ev
}

$rootObj["hooks"] = $hooksObj
$json = $rootObj | ConvertTo-Json -Depth 12
[System.IO.File]::WriteAllText($HooksPath, $json + "`n", (New-Object System.Text.UTF8Encoding($false)))
Write-Host "CODEX_ADAPTER_INSTALLED: $HooksPath"
