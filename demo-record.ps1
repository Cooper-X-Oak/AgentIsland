param(
    [int]$LeadInSeconds = 4,
    [string]$TargetQuery = "AgentIsland",
    [switch]$WithHealth
)

$ErrorActionPreference = "Stop"
$InboxDir = Join-Path $env:LOCALAPPDATA "AgentIsland\inbox"
New-Item -ItemType Directory -Force -Path $InboxDir | Out-Null

function Push-DemoEvent($Obj) {
    $json = $Obj | ConvertTo-Json -Depth 4 -Compress
    $id = "$(Get-Date -Format 'yyyyMMddHHmmssfff')-$([guid]::NewGuid().ToString('N').Substring(0,6))"
    $tmp = Join-Path $InboxDir "$id.tmp"
    $final = Join-Path $InboxDir "$id.json"
    [System.IO.File]::WriteAllText($tmp, $json, (New-Object System.Text.UTF8Encoding($false)))
    Move-Item -Path $tmp -Destination $final -Force
}

Write-Host "=== AgentIsland Demo Script ==="
Write-Host "Switch to another window/session now. Demo starts in ${LeadInSeconds}s..."
Start-Sleep -Seconds $LeadInSeconds

Push-DemoEvent @{
    source         = "antigravity"
    kind           = "working"
    conversationId = "demo-01"
    project        = "AgentIsland"
    title          = "思考中"
    summary        = "Building and verifying DirectComposition island..."
    lastUserInput  = $TargetQuery
    timestamp      = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
}
Start-Sleep -Seconds 2

Push-DemoEvent @{
    source         = "antigravity"
    kind           = "needs_decision"
    conversationId = "demo-01"
    project        = "AgentIsland"
    title          = "等你拍板"
    summary        = "All 22/22 checks passed. Ready to publish v0.1.0?"
    lastUserInput  = $TargetQuery
    timestamp      = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
}

if ($WithHealth) {
    Start-Sleep -Seconds 7
    Push-DemoEvent @{
        source    = "health"
        kind      = "health_test"
        timestamp = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
    }
}
Write-Host "Demo events pushed."
