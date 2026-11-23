$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$Exe = Join-Path $Root "bin\AgentIsland.exe"

if (-not (Test-Path $Exe)) {
    throw "Not found: $Exe. Please run .\build.ps1 first."
}

Get-Process -Name "AgentIsland" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 250
Start-Process -FilePath "explorer.exe" -ArgumentList "`"$Exe`""
Start-Sleep -Milliseconds 800
$p = Get-Process -Name "AgentIsland" -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $p) {
    throw "Failed to launch AgentIsland."
}
Write-Host "STARTED: $Exe (PID=$($p.Id))"
