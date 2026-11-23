param(
    [switch]$Rebuild,
    [string]$OutDir = ""
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$BinDir = Join-Path $Root "bin"
$AppExe = Join-Path $BinDir "AgentIsland.exe"
$HookExe = Join-Path $BinDir "island-hook.exe"
$PushExe = Join-Path $BinDir "island-push.exe"

if ([string]::IsNullOrWhiteSpace($OutDir)) {
    $OutDir = Join-Path $env:LOCALAPPDATA "AgentIsland\verify-out"
}
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

if ($Rebuild -or -not (Test-Path $AppExe) -or -not (Test-Path $HookExe) -or -not (Test-Path $PushExe)) {
    & (Join-Path $Root "build.ps1")
}

$TestInbox = Join-Path $OutDir "hook-test-inbox"
Remove-Item $TestInbox -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $TestInbox | Out-Null
$env:ISLAND_INBOX_DIR = $TestInbox

Write-Host "=== [1/3] Antigravity + Claude Code + Codex CLI Hook & Push Contract Checks ==="
$hookAllPass = $true

function Test-HookCase([string]$Name, [string[]]$ArgsList, [string]$StdinJson, [string]$ExpectOut, [string]$ExpectSource, [string]$ExpectKind) {
    $before = @(Get-ChildItem $TestInbox -Filter "*.json" | Select-Object -ExpandProperty Name)
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    if ($null -ne $StdinJson) {
        $out = $StdinJson | & $HookExe @ArgsList
    } else {
        $out = & $HookExe @ArgsList
    }
    $code = $LASTEXITCODE
    $sw.Stop()
    $outStr = if ($null -eq $out) { "" } else { ($out -join "") }
    $afterFiles = @(Get-ChildItem $TestInbox -Filter "*.json" | Where-Object { $before -notcontains $_.Name })
    $payloadOk = $false
    $detail = ""
    if ($afterFiles.Count -eq 1) {
        $evt = Get-Content $afterFiles[0].FullName -Raw -Encoding UTF8 | ConvertFrom-Json
        $payloadOk = ($evt.source -eq $ExpectSource) -and ($evt.kind -eq $ExpectKind)
        $detail = "src=$($evt.source) kind=$($evt.kind) proj=$($evt.project)"
    } else {
        $detail = "newFiles=$($afterFiles.Count)"
    }
    $ok = ($code -eq 0) -and ($outStr -eq $ExpectOut) -and $payloadOk
    if (-not $ok) { $script:hookAllPass = $false }
    $status = if ($ok) { "PASS" } else { "FAIL" }
    Write-Host ("  [{0}] {1,-22} exit={2} out='{3}' {4} ({5}ms)" -f $status, $Name, $code, $outStr, $detail, $sw.ElapsedMilliseconds)
}

# Antigravity (4 cases)
Test-HookCase "AGY.PreInvocation" @("--adapter","antigravity","--event","PreInvocation") '{"conversationId":"c1","workspacePaths":["D:/proj/demo"],"hookEventName":"PreInvocation","lastUserInput":"hello"}' "{}" "antigravity" "working"
Test-HookCase "AGY.AskQuestion"   @("--adapter","antigravity","--event","AskQuestion")   '{"conversationId":"c2","workspacePaths":["D:/proj/demo"],"hookEventName":"PreToolUse","toolCall":{"id":"t1","name":"ask_question","args":{"questions":[{"question":"Confirm deploy?"}]}}}' '{"decision":"allow"}' "antigravity" "needs_decision"
Test-HookCase "AGY.StopReply"     @("--adapter","antigravity","--event","Stop")          '{"conversationId":"c3","workspacePaths":["D:/proj/demo"],"hookEventName":"Stop","terminationReason":"NO_TOOL_CALL","lastUserInput":"fix corners"}' '{"decision":"allow"}' "antigravity" "needs_reply"
Test-HookCase "AGY.StopError"     @("--adapter","antigravity","--event","Stop")          '{"conversationId":"c4","workspacePaths":["D:/proj/demo"],"hookEventName":"Stop","terminationReason":"ERROR","error":"timeout"}' '{"decision":"allow"}' "antigravity" "error"

# Claude Code (4 cases - stdout MUST be empty so PermissionRequest is never auto-approved!)
Test-HookCase "Claude.PromptSubmit" @("--adapter","claude","--event","UserPromptSubmit")  '{"session_id":"s1","cwd":"D:/work/api-app","hook_event_name":"UserPromptSubmit","prompt":"refactor auth"}' "" "ClaudeCode" "working"
Test-HookCase "Claude.Permission"   @("--adapter","claude","--event","PermissionRequest") '{"session_id":"s1","cwd":"D:/work/api-app","hook_event_name":"PermissionRequest","tool_name":"Bash","tool_input":{"command":"npm run deploy"}}' "" "ClaudeCode" "needs_approval"
Test-HookCase "Claude.Notification" @("--adapter","claude","--event","Notification")      '{"session_id":"s1","cwd":"D:/work/api-app","hook_event_name":"Notification","notification_type":"idle_prompt","message":"Claude is waiting for input"}' "" "ClaudeCode" "needs_decision"
Test-HookCase "Claude.Stop"         @("--adapter","claude","--event","Stop")              '{"session_id":"s1","cwd":"D:/work/api-app","hook_event_name":"Stop","stop_hook_active":false}' "" "ClaudeCode" "needs_reply"

# Codex CLI (4 cases: hooks.json stdin + legacy config.toml notify argv via direct CreateProcess)
Test-HookCase "Codex.PromptSubmit"  @("--adapter","codex","--event","UserPromptSubmit")   '{"session_id":"t1","cwd":"D:/work/web-app","hook_event_name":"UserPromptSubmit","prompt":"add unit tests"}' "" "Codex" "working"
Test-HookCase "Codex.Permission"    @("--adapter","codex","--event","PermissionRequest")  '{"session_id":"t1","cwd":"D:/work/web-app","hook_event_name":"PermissionRequest","tool_name":"shell","tool_input":{"command":"git push"}}' "" "Codex" "needs_approval"
Test-HookCase "Codex.Stop"          @("--adapter","codex","--event","Stop")               '{"session_id":"t1","cwd":"D:/work/web-app","hook_event_name":"Stop","last-assistant-message":"All 18 tests passed"}' "" "Codex" "needs_reply"

$legacyBefore = @(Get-ChildItem $TestInbox -Filter "*.json" | Select-Object -ExpandProperty Name)
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $HookExe
$psi.Arguments = '--adapter codex "{\"type\":\"agent-turn-complete\",\"thread-id\":\"th9\",\"cwd\":\"D:/work/web-app\",\"last-assistant-message\":\"Done refactoring\"}"'
$psi.UseShellExecute = $false
$psi.RedirectStandardOutput = $true
$psi.CreateNoWindow = $true
$lp = [System.Diagnostics.Process]::Start($psi)
$lOut = $lp.StandardOutput.ReadToEnd()
$lp.WaitForExit(5000) | Out-Null
$legacyAfter = @(Get-ChildItem $TestInbox -Filter "*.json" | Where-Object { $legacyBefore -notcontains $_.Name })
$legacyOk = $false
$legacyDetail = "newFiles=$($legacyAfter.Count)"
if ($legacyAfter.Count -eq 1) {
    $levt = Get-Content $legacyAfter[0].FullName -Raw -Encoding UTF8 | ConvertFrom-Json
    $legacyOk = ($lp.ExitCode -eq 0) -and ($lOut -eq "") -and ($levt.source -eq "Codex") -and ($levt.kind -eq "needs_reply")
    $legacyDetail = "src=$($levt.source) kind=$($levt.kind) proj=$($levt.project)"
}
if (-not $legacyOk) { $hookAllPass = $false }
Write-Host ("  [{0}] {1,-22} exit={2} out='{3}' {4}" -f ($(if($legacyOk){"PASS"}else{"FAIL"}), "Codex.LegacyNotify", $lp.ExitCode, $lOut, $legacyDetail))

# Universal Push CLI
$pushOut = & $PushExe --source "CLI" --project "demo-web-app" --title "Build Ready" --body "All unit tests passed" --kind info
$pushOk = ($LASTEXITCODE -eq 0) -and ($pushOut -like "PUSHED:*")
if (-not $pushOk) { $hookAllPass = $false }
Write-Host ("  [{0}] {1,-22} exit={2} out={3}" -f ($(if($pushOk){"PASS"}else{"FAIL"}), "IslandPushCLI", $LASTEXITCODE, $pushOut))

Remove-Item Env:ISLAND_INBOX_DIR -ErrorAction SilentlyContinue

# [2/3] Non-destructive adapter install/uninstall merge tests on isolated temp configs
Write-Host ""
Write-Host "=== [2/3] Adapter Non-Destructive Install/Uninstall Tests ==="
$tmpClaudeCfg = Join-Path $OutDir "test-claude-settings.json"
$tmpCodexCfg = Join-Path $OutDir "test-codex-hooks.json"
'{"theme":"dark","hooks":{"Stop":[{"matcher":"","hooks":[{"type":"command","command":"echo existing-user-hook"}]}]}}' | Set-Content $tmpClaudeCfg -Encoding UTF8
'{"hooks":{"Stop":[{"hooks":[{"type":"command","command":"echo existing-codex-hook"}]}]}}' | Set-Content $tmpCodexCfg -Encoding UTF8

& (Join-Path $Root "adapters\claude-code\install.ps1") -ConfigPath $tmpClaudeCfg | Out-Null
$cInstalled = Get-Content $tmpClaudeCfg -Raw -Encoding UTF8
$cInstallOk = ($cInstalled -match "existing-user-hook") -and ($cInstalled -match "island-hook\.exe") -and ($cInstalled -match "PermissionRequest")
& (Join-Path $Root "adapters\claude-code\uninstall.ps1") -ConfigPath $tmpClaudeCfg | Out-Null
$cRemoved = Get-Content $tmpClaudeCfg -Raw -Encoding UTF8
$cRemoveOk = ($cRemoved -match "existing-user-hook") -and ($cRemoved -notmatch "island-hook\.exe")
if (-not ($cInstallOk -and $cRemoveOk)) { $hookAllPass = $false }
Write-Host ("  [{0}] {1,-22} installMerge={2} cleanUninstall={3}" -f ($(if($cInstallOk -and $cRemoveOk){"PASS"}else{"FAIL"}), "ClaudeAdapterMerge", $cInstallOk, $cRemoveOk))

& (Join-Path $Root "adapters\codex-cli\install.ps1") -ConfigPath $tmpCodexCfg | Out-Null
$xInstalled = Get-Content $tmpCodexCfg -Raw -Encoding UTF8
$xInstallOk = ($xInstalled -match "existing-codex-hook") -and ($xInstalled -match "island-hook\.exe") -and ($xInstalled -match "PermissionRequest")
& (Join-Path $Root "adapters\codex-cli\uninstall.ps1") -ConfigPath $tmpCodexCfg | Out-Null
$xRemoved = Get-Content $tmpCodexCfg -Raw -Encoding UTF8
$xRemoveOk = ($xRemoved -match "existing-codex-hook") -and ($xRemoved -notmatch "island-hook\.exe")
if (-not ($xInstallOk -and $xRemoveOk)) { $hookAllPass = $false }
Write-Host ("  [{0}] {1,-22} installMerge={2} cleanUninstall={3}" -f ($(if($xInstallOk -and $xRemoveOk){"PASS"}else{"FAIL"}), "CodexAdapterMerge", $xInstallOk, $xRemoveOk))

# [3/3] Host / DirectComposition / Click-Through / RGBA Pixel Checks
Write-Host ""
Write-Host "=== [3/3] Host / DirectComposition / Click-Through / RGBA Pixel Checks ==="
$ReportPath = Join-Path $OutDir "verify-report.json"
Remove-Item $ReportPath -Force -ErrorAction SilentlyContinue

$proc = Start-Process -FilePath $AppExe -ArgumentList "--self-test", "`"$OutDir`"" -WorkingDirectory $BinDir -PassThru
$exited = $proc.WaitForExit(20000)
if (-not $exited) {
    $proc | Stop-Process -Force -ErrorAction SilentlyContinue
    throw "Self-test timed out after 20s"
}

if (-not (Test-Path $ReportPath)) {
    throw "Missing verify-report.json at $ReportPath"
}

$report = Get-Content $ReportPath -Raw -Encoding UTF8 | ConvertFrom-Json
foreach ($c in $report.checks) {
    $tag = if ($c.pass) { "PASS" } else { "FAIL" }
    Write-Host ("  [{0}] {1,-32} {2}" -f $tag, $c.name, $c.detail)
}

Write-Host ""
Write-Host "REPORT_JSON: $ReportPath"
Write-Host "VERIFY_SHEET: $($report.sheetPath)"

if ($hookAllPass -and $report.allPass) {
    Write-Host "OVERALL: ALL_PASS"
    exit 0
} else {
    Write-Host "OVERALL: FAILED"
    exit 1
}
