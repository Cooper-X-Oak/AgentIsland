param(
    [ValidateSet("agy", "health", "both")]
    [string]$Mode = "both"
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$PushExe = Join-Path $Root "bin\island-push.exe"

if ($Mode -eq "agy" -or $Mode -eq "both") {
    & $PushExe --source "antigravity" --project "demo-web-app" --title "等你拍板" --body "测试推送：AgentIsland 已就绪，点按钮直达对应会话" --kind decision --id "test-agy-01"
}

if ($Mode -eq "health" -or $Mode -eq "both") {
    Start-Sleep -Milliseconds 350
    & $PushExe --source "health" --kind health
}
