$procs = Get-Process -Name "AgentIsland" -ErrorAction SilentlyContinue
if ($procs) {
    $procs | Stop-Process -Force
    Write-Host "STOPPED: AgentIsland"
} else {
    Write-Host "NOT_RUNNING"
}
