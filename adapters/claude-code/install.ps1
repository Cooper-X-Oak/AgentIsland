$ErrorActionPreference = "Stop"

$AdapterDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$HookScript = Join-Path $AdapterDir "claude-hook.ps1"
$ClaudeDir = Join-Path $env:USERPROFILE ".claude"
$SettingsPath = Join-Path $ClaudeDir "settings.json"
New-Item -ItemType Directory -Force -Path $ClaudeDir | Out-Null

$rootObj = @{}
if (Test-Path $SettingsPath) {
    $raw = Get-Content $SettingsPath -Raw -Encoding UTF8
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

function Add-OrReplaceClaudeHook($Map, [string]$EventName) {
    $cmd = "powershell -NoProfile -ExecutionPolicy Bypass -File `"$HookScript`" -Event $EventName"
    $existing = @()
    if ($Map.ContainsKey($EventName)) {
        $existing = @($Map[$EventName] | Where-Object { ($_ | ConvertTo-Json -Depth 5 -Compress) -notmatch "claude-hook\.ps1" })
    }
    $entry = @{
        matcher = ""
        hooks   = @(
            @{
                type    = "command"
                command = $cmd
            }
        )
    }
    $Map[$EventName] = @($existing + $entry)
}

Add-OrReplaceClaudeHook $hooksObj "UserPromptSubmit"
Add-OrReplaceClaudeHook $hooksObj "Notification"
Add-OrReplaceClaudeHook $hooksObj "Stop"

$rootObj["hooks"] = $hooksObj
$json = $rootObj | ConvertTo-Json -Depth 10
[System.IO.File]::WriteAllText($SettingsPath, $json + "`n", (New-Object System.Text.UTF8Encoding($false)))
Write-Host "CLAUDE_CODE_ADAPTER_INSTALLED: $SettingsPath"
