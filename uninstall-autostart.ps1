$StartupDir = [Environment]::GetFolderPath("Startup")
$ProgramsDir = [Environment]::GetFolderPath("Programs")
foreach ($p in @((Join-Path $StartupDir "AgentIsland.lnk"), (Join-Path $ProgramsDir "AgentIsland.lnk"))) {
    if (Test-Path $p) {
        Remove-Item $p -Force
        Write-Host "REMOVED: $p"
    }
}
