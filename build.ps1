param(
    [string]$WebView2Version = "1.0.2903.40"
)

$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$SrcDir = Join-Path $Root "src"
$LibDir = Join-Path $Root "lib"
$BinDir = Join-Path $Root "bin"
New-Item -ItemType Directory -Force -Path $LibDir, $BinDir | Out-Null

$Csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $Csc)) {
    throw "System C# compiler not found at $Csc (.NET Framework 4.x required)."
}

$CoreDllLib = Join-Path $LibDir "Microsoft.Web.WebView2.Core.dll"
$LoaderDllLib = Join-Path $LibDir "WebView2Loader.dll"

# 1. Resolve WebView2 assemblies (lib/ -> local NuGet/Programs cache -> auto-download official NuGet package)
if (-not (Test-Path $CoreDllLib) -or -not (Test-Path $LoaderDllLib)) {
    $probeDirs = @(
        (Join-Path $env:USERPROFILE ".nuget\packages\microsoft.web.webview2"),
        (Join-Path $env:LOCALAPPDATA "Programs")
    )
    foreach ($dir in $probeDirs) {
        if (-not (Test-Path $dir)) { continue }
        $foundCore = Get-ChildItem $dir -Filter "Microsoft.Web.WebView2.Core.dll" -Recurse -Depth 5 -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($foundCore) {
            $base = Split-Path -Parent $foundCore.FullName
            $candLoader = Get-ChildItem (Split-Path -Parent $base) -Filter "WebView2Loader.dll" -Recurse -Depth 4 -ErrorAction SilentlyContinue |
                Where-Object { $_.FullName -match "win-x64" } | Select-Object -First 1
            if (-not $candLoader) {
                $candLoader = Get-ChildItem $base -Filter "WebView2Loader.dll" -Recurse -Depth 4 -ErrorAction SilentlyContinue |
                    Where-Object { $_.FullName -match "win-x64" } | Select-Object -First 1
            }
            if ($candLoader) {
                Copy-Item $foundCore.FullName $CoreDllLib -Force
                Copy-Item $candLoader.FullName $LoaderDllLib -Force
                Write-Host "Found local WebView2 assemblies."
                break
            }
        }
    }
}

if (-not (Test-Path $CoreDllLib) -or -not (Test-Path $LoaderDllLib)) {
    Write-Host "Downloading official Microsoft.Web.WebView2 ($WebView2Version) from NuGet..."
    $nupkg = Join-Path $LibDir "webview2.nupkg"
    $extractDir = Join-Path $LibDir "_extract"
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -Uri "https://www.nuget.org/api/v2/package/Microsoft.Web.WebView2/$WebView2Version" -OutFile $nupkg -UseBasicParsing

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    Remove-Item $extractDir -Recurse -Force -ErrorAction SilentlyContinue
    [System.IO.Compression.ZipFile]::ExtractToDirectory($nupkg, $extractDir)

    $coreSrc = Get-ChildItem (Join-Path $extractDir "lib") -Filter "Microsoft.Web.WebView2.Core.dll" -Recurse |
        Where-Object { $_.FullName -match "net462|net45" } | Select-Object -First 1
    $loaderSrc = Join-Path $extractDir "runtimes\win-x64\native\WebView2Loader.dll"

    if (-not $coreSrc -or -not (Test-Path $loaderSrc)) {
        throw "Failed to extract required WebView2 DLLs from NuGet package."
    }

    Copy-Item $coreSrc.FullName $CoreDllLib -Force
    Copy-Item $loaderSrc $LoaderDllLib -Force
    Remove-Item $nupkg, $extractDir -Recurse -Force -ErrorAction SilentlyContinue
}

$CoreDll = Join-Path $BinDir "Microsoft.Web.WebView2.Core.dll"
$LoaderDll = Join-Path $BinDir "WebView2Loader.dll"
Copy-Item $CoreDllLib $CoreDll -Force
Copy-Item $LoaderDllLib $LoaderDll -Force

# 2. Stop running AgentIsland before overwriting binary
Get-Process -Name "AgentIsland" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 250

# 3. Compile Antigravity hook forwarder (island-hook.exe)
$IconPath = Join-Path $Root "assets\app.ico"
$HasIcon = Test-Path $IconPath

$HookOut = Join-Path $BinDir "island-hook.exe"
$HookSrc = Join-Path $SrcDir "IslandHook.cs"
Write-Host "[1/3] Compiling island-hook.exe ..."
$hookArgs = @(
    "/nologo", "/optimize+", "/platform:x64", "/target:exe", "/utf8output",
    "/r:System.dll", "/r:System.Core.dll", "/r:System.Web.Extensions.dll",
    "/out:$HookOut", "$HookSrc"
)
if ($HasIcon) { $hookArgs += "/win32icon:$IconPath" }
& $Csc @hookArgs
if ($LASTEXITCODE -ne 0) { throw "island-hook.exe build failed" }

# 4. Compile universal CLI pusher (island-push.exe)
$PushOut = Join-Path $BinDir "island-push.exe"
$PushSrc = Join-Path $SrcDir "IslandPush.cs"
Write-Host "[2/3] Compiling island-push.exe ..."
$pushArgs = @(
    "/nologo", "/optimize+", "/platform:x64", "/target:exe", "/utf8output",
    "/r:System.dll", "/r:System.Core.dll", "/r:System.Web.Extensions.dll",
    "/out:$PushOut", "$PushSrc"
)
if ($HasIcon) { $pushArgs += "/win32icon:$IconPath" }
& $Csc @pushArgs
if ($LASTEXITCODE -ne 0) { throw "island-push.exe build failed" }

# 5. Compile DirectComposition + WebView2 host (AgentIsland.exe)
$AppOut = Join-Path $BinDir "AgentIsland.exe"
$AppSrc = Join-Path $SrcDir "AgentIsland.cs"
Write-Host "[3/3] Compiling AgentIsland.exe ..."
$appArgs = @(
    "/nologo", "/optimize+", "/platform:x64", "/target:winexe", "/utf8output",
    "/r:System.dll", "/r:System.Core.dll", "/r:System.Drawing.dll",
    "/r:System.Windows.Forms.dll", "/r:System.Web.Extensions.dll",
    "/r:$CoreDll", "/out:$AppOut", "$AppSrc"
)
if ($HasIcon) { $appArgs += "/win32icon:$IconPath" }
& $Csc @appArgs
if ($LASTEXITCODE -ne 0) { throw "AgentIsland.exe build failed" }

Write-Host "BUILD_OK: $BinDir"
Get-ChildItem $BinDir | Select-Object Name, Length | Format-Table -AutoSize
