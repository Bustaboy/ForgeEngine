param(
    [switch]$RuntimeOnly
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$buildDir = Join-Path $repoRoot "build/runtime"
$runtimeSrc = Join-Path $repoRoot "runtime/cpp/main.cpp"
$runtimeBin = Join-Path $buildDir "gameforge_runtime.exe"
$editorProject = Join-Path $repoRoot "editor/csharp/GameForge.Editor.csproj"

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

foreach ($path in $requiredPaths) {
    if (Test-Path $path -PathType Container) {
        $display = $path.Replace($repoRoot + [IO.Path]::DirectorySeparatorChar, "")
        Write-Host "OK - $display"
    }
    else {
        $display = $path.Replace($repoRoot + [IO.Path]::DirectorySeparatorChar, "")
        Write-Host "MISSING - $display"
        exit 1
    }
}

$gpp = Get-Command g++ -ErrorAction SilentlyContinue
if (-not $gpp) {
    Write-Host "Missing required compiler: g++"
    Write-Host "Install MinGW-w64 g++ and rerun: pwsh -f scripts/bootstrap.ps1"
    exit 1
}

New-Item -ItemType Directory -Force -Path $buildDir | Out-Null

Write-Host "== Building Runtime Entrypoint (C++) =="
& $gpp.Source "-std=c++17" $runtimeSrc "-o" $runtimeBin

if ($RuntimeOnly) {
    Write-Host "== Starting Runtime Only =="
    & $runtimeBin
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
