$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$Exe = Join-Path $Root "bin\AgentIsland.exe"
$StartupDir = [Environment]::GetFolderPath("Startup")
$ProgramsDir = [Environment]::GetFolderPath("Programs")

$StartupLnk = Join-Path $StartupDir "AgentIsland.lnk"
$StartMenuLnk = Join-Path $ProgramsDir "AgentIsland.lnk"

$WshShell = New-Object -ComObject WScript.Shell
foreach ($p in @($StartupLnk, $StartMenuLnk)) {
    $Shortcut = $WshShell.CreateShortcut($p)
    $Shortcut.TargetPath = $Exe
    $Shortcut.WorkingDirectory = Split-Path -Parent $Exe
    $Shortcut.Description = "AgentIsland — Windows Dynamic Island for AI Coding Agents & Posture Coach"
    $Shortcut.Save()
}

Write-Host "AUTOSTART_INSTALLED: $StartupLnk"
Write-Host "STARTMENU_INSTALLED: $StartMenuLnk"
