param(
    [switch]$RuntimeOnly
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$buildDir = Join-Path $repoRoot "build/runtime"
$runtimeSrc = Join-Path $repoRoot "runtime/cpp/main.cpp"
$runtimeBin = Join-Path $buildDir "gameforge_runtime.exe"
$editorProject = Join-Path $repoRoot "editor/csharp/GameForge.Editor.csproj"
$jsonHeader = Join-Path $repoRoot "runtime/cpp/external/nlohmann/json.hpp"
$jsonUrl = "https://raw.githubusercontent.com/nlohmann/json/v3.11.3/single_include/nlohmann/json.hpp"

$requiredPaths = @(
    (Join-Path $repoRoot "app"),
    (Join-Path $repoRoot "editor/csharp"),
    (Join-Path $repoRoot "runtime/cpp"),
    (Join-Path $repoRoot "ai-orchestration/python"),
    (Join-Path $repoRoot "docs"),
    (Join-Path $repoRoot "scripts")
)

Write-Host "GameForge V1 bootstrap (Windows)"
Write-Host "Mode: local-first, single-player, no-code-first"
Write-Host "== Repository Structure =="

$missingPaths = @()
foreach ($path in $requiredPaths) {
    $display = $path.Replace($repoRoot + [IO.Path]::DirectorySeparatorChar, "")
    if (Test-Path $path -PathType Container) {
        Write-Host "OK - $display"
    }
    else {
        Write-Host "MISSING - $display"
        $missingPaths += $display
    }
}
if ($missingPaths.Count -gt 0) {
    Write-Host ""
    Write-Host "ERROR: Repository structure is incomplete. Missing directories:"
    foreach ($p in $missingPaths) { Write-Host "  - $p" }
    Write-Host "  This usually means the repository was not cloned correctly."
    Write-Host "  Fix: re-clone the full repository and retry."
    exit 1
}

Write-Host "== Runtime JSON Header =="
if (Test-Path $jsonHeader -PathType Leaf) {
    Write-Host "OK - runtime/cpp/external/nlohmann/json.hpp"
}
else {
    New-Item -ItemType Directory -Force -Path (Split-Path $jsonHeader -Parent) | Out-Null
    Write-Host "Downloading nlohmann/json single header..."
    try {
        Invoke-WebRequest -Uri $jsonUrl -OutFile $jsonHeader -UseBasicParsing
    }
    catch {
        Write-Host "ERROR: Failed to download nlohmann/json header."
        Write-Host "  URL: $jsonUrl"
        Write-Host "  Error: $($_.Exception.Message)"
        Write-Host "  Fix options:"
        Write-Host "    1. Check your network connection and retry: pwsh -f scripts/bootstrap.ps1"
        Write-Host "    2. Download manually and place at: runtime\cpp\external\nlohmann\json.hpp"
        exit 1
    }
    Write-Host "Installed - runtime/cpp/external/nlohmann/json.hpp"
}

$gpp = Get-Command g++ -ErrorAction SilentlyContinue
if (-not $gpp) {
    Write-Host "ERROR: Missing required compiler: g++"
    Write-Host "  If you ran Setup-Alpha.ps1, try opening a new PowerShell window and retrying."
    Write-Host "  Or run the full setup again: pwsh -f scripts/Setup-Alpha.ps1"
    exit 1
}

New-Item -ItemType Directory -Force -Path $buildDir | Out-Null

Write-Host "== Building Runtime Entrypoint (C++) =="
$runtimeBuildOk = $true
& $gpp.Source "-std=c++17" $runtimeSrc "-o" $runtimeBin
if ($LASTEXITCODE -ne 0) {
    $runtimeBuildOk = $false
    Write-Host "WARNING: Runtime build failed (Vulkan/GLFW dependencies may be missing)."
    Write-Host "Continuing bootstrap in degraded mode."
}

if ($RuntimeOnly) {
    if ($runtimeBuildOk -and (Test-Path $runtimeBin)) {
        Write-Host "== Starting Runtime Only =="
        & $runtimeBin $repoRoot
    }
    else {
        Write-Host "== Runtime-only launch skipped (runtime binary unavailable) =="
    }
    Write-Host "Bootstrap completed successfully (runtime-only)."
    exit 0
}

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) {
    Write-Host "WARNING: dotnet SDK not found; cannot start C# app entrypoint."
    Write-Host "Run: pwsh -f scripts/bootstrap.ps1 -RuntimeOnly"
    exit 2
}

Write-Host "== Starting C# App Entrypoint =="
& $dotnet.Source "run" "--project" $editorProject "--" $runtimeBin

Write-Host "Bootstrap completed successfully."
