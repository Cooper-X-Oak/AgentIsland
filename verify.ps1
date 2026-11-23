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

$hookCases = @(
    @{ Name = "PreInvocation"; Arg = "PreInvocation"; Expect = "{}"; Payload = '{"conversationId":"c1","workspacePaths":["D:/proj/demo"],"hookEventName":"PreInvocation","lastUserInput":"hello"}' },
    @{ Name = "AskQuestion";   Arg = "AskQuestion";   Expect = '{"decision":"allow"}'; Payload = '{"conversationId":"c2","workspacePaths":["D:/proj/demo"],"hookEventName":"PreToolUse","toolCall":{"id":"t1","name":"ask_question","args":{"questions":[{"question":"Confirm deploy?"}]}}}' },
    @{ Name = "StopReply";     Arg = "Stop";          Expect = '{"decision":"allow"}'; Payload = '{"conversationId":"c3","workspacePaths":["D:/proj/demo"],"hookEventName":"Stop","terminationReason":"NO_TOOL_CALL","lastUserInput":"fix rounded corners"}' },
    @{ Name = "StopError";     Arg = "Stop";          Expect = '{"decision":"allow"}'; Payload = '{"conversationId":"c4","workspacePaths":["D:/proj/demo"],"hookEventName":"Stop","terminationReason":"ERROR","error":"timeout"}' }
)

Write-Host "=== [1/2] Hook & CLI Contract Checks ==="
$hookAllPass = $true
foreach ($tc in $hookCases) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $out = $tc.Payload | & $HookExe --event $tc.Arg
    $code = $LASTEXITCODE
    $sw.Stop()
    $ok = ($code -eq 0) -and ($out -eq $tc.Expect)
    if (-not $ok) { $hookAllPass = $false }
    $status = if ($ok) { "PASS" } else { "FAIL" }
    Write-Host ("  [{0}] {1,-14} exit={2} out={3} ({4}ms)" -f $status, $tc.Name, $code, $out, $sw.ElapsedMilliseconds)
}

$pushOut = & $PushExe --source "ClaudeCode" --project "demo-web-app" --title "Build Ready" --body "All unit tests passed" --kind info
$pushOk = ($LASTEXITCODE -eq 0) -and ($pushOut -like "PUSHED:*")
if (-not $pushOk) { $hookAllPass = $false }
Write-Host ("  [{0}] IslandPushCLI  exit={1} out={2}" -f ($(if($pushOk){"PASS"}else{"FAIL"}), $LASTEXITCODE, $pushOut))

$inboxFiles = (Get-ChildItem $TestInbox -Filter "*.json" | Measure-Object).Count
$inboxOk = ($inboxFiles -eq 5)
if (-not $inboxOk) { $hookAllPass = $false }
Write-Host ("  [{0}] InboxFiles     count={1} (expect 5)" -f ($(if($inboxOk){"PASS"}else{"FAIL"}), $inboxFiles))
Remove-Item Env:ISLAND_INBOX_DIR -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "=== [2/2] Host / DirectComposition / Click-Through / RGBA Pixel Checks ==="
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
