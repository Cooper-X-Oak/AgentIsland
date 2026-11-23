param(
    [string]$Version = "0.1.0"
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
& (Join-Path $Root "build.ps1")

$DistDir = Join-Path $Root "dist"
$StageDir = Join-Path $DistDir "AgentIsland-v$Version-win-x64"
$ZipPath = "$StageDir.zip"

Remove-Item $StageDir, $ZipPath -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $StageDir | Out-Null

$copyDirs = @("bin", "ui", "config", "adapters")
foreach ($d in $copyDirs) {
    Copy-Item (Join-Path $Root $d) (Join-Path $StageDir $d) -Recurse -Force
}

$copyFiles = @("README.md", "README.zh-CN.md", "LICENSE", "start.ps1", "stop.ps1", "install-autostart.ps1", "uninstall-autostart.ps1", "test-push.ps1", "demo-record.ps1")
foreach ($f in $copyFiles) {
    $src = Join-Path $Root $f
    if (Test-Path $src) { Copy-Item $src (Join-Path $StageDir $f) -Force }
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($StageDir, $ZipPath)
Remove-Item $StageDir -Recurse -Force -ErrorAction SilentlyContinue

$sizeKB = [math]::Round((Get-Item $ZipPath).Length / 1KB, 1)
Write-Host "RELEASE_ZIP_OK: $ZipPath (${sizeKB} KB)"
