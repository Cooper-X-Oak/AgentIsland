param(
    [ValidateSet("Notification", "Stop", "UserPromptSubmit")]
    [string]$Event = "Notification"
)

$ErrorActionPreference = "SilentlyContinue"
$Root = Resolve-Path (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) "..\..")
$PushExe = Join-Path $Root "bin\island-push.exe"
if (-not (Test-Path $PushExe)) { exit 0 }

$raw = [Console]::In.ReadToEnd()
$project = "ClaudeCode"
$summary = "Claude Code needs your attention"
$convId = "claude-default"

if (-not [string]::IsNullOrWhiteSpace($raw)) {
    try {
        $obj = $raw | ConvertFrom-Json
        if ($obj.session_id) { $convId = "cc-" + $obj.session_id }
        if ($obj.cwd) { $project = Split-Path -Leaf $obj.cwd }
        if ($obj.message) { $summary = [string]$obj.message }
        elseif ($obj.notification_type) { $summary = "Event: " + $obj.notification_type }
    } catch {}
}

switch ($Event) {
    "UserPromptSubmit" {
        & $PushExe --source "ClaudeCode" --project $project --title "思考中" --body "Claude Code 正在处理任务" --kind working --id $convId | Out-Null
    }
    "Notification" {
        & $PushExe --source "ClaudeCode" --project $project --title "等你介入" --body $summary --kind decision --id $convId | Out-Null
    }
    "Stop" {
        & $PushExe --source "ClaudeCode" --project $project --title "等你回复" --body "Claude Code 本轮回复已完成" --kind info --id $convId | Out-Null
    }
}
exit 0
