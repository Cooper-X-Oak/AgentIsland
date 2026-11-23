$ErrorActionPreference = "Stop"

$Root = Resolve-Path (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) "..\..")
$HookExe = Join-Path $Root "bin\island-hook.exe"
if (-not (Test-Path $HookExe)) {
    throw "island-hook.exe not found at $HookExe. Please run .\build.ps1 first."
}

$ConfigDir = Join-Path $env:USERPROFILE ".gemini\config"
$HooksPath = Join-Path $ConfigDir "hooks.json"
New-Item -ItemType Directory -Force -Path $ConfigDir | Out-Null

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

$rootObj["agent-island"] = @{
    PreInvocation = @(
        @{
            type    = "command"
            command = "$HookExe --event PreInvocation"
            timeout = 5
        }
    )
    PreToolUse = @(
        @{
            matcher = "ask_question"
            hooks   = @(
                @{
                    type    = "command"
                    command = "$HookExe --event AskQuestion"
                    timeout = 5
                }
            )
        }
    )
    Stop = @(
        @{
            type    = "command"
            command = "$HookExe --event Stop"
            timeout = 5
        }
    )
}

$json = $rootObj | ConvertTo-Json -Depth 10
[System.IO.File]::WriteAllText($HooksPath, $json + "`n", (New-Object System.Text.UTF8Encoding($false)))
Write-Host "ANTIGRAVITY_ADAPTER_INSTALLED: $HooksPath"
