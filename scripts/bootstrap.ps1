param(
    [switch]$RuntimeOnly
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$buildDir = Join-Path $repoRoot "build"
$runtimeBin = Join-Path $buildDir "bin\forge_runtime.exe"
$editorProject = Join-Path $repoRoot "editor/csharp/GameForge.Editor.csproj"
$editorBin = Join-Path $repoRoot "editor/csharp/bin/Release/net8.0/GameForge.Editor.exe"
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

Write-Host "Soul Loom bootstrap (Windows)"
Write-Host "Mode: local-first, single-player, no-code-first"
Write-Host "== Repository Structure =="

function Invoke-CheckedNative {
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [string[]]$Arguments = @(),
        [Parameter(Mandatory)][string]$FailureMessage
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FailureMessage (exit code $LASTEXITCODE)"
    }
}

function Add-ToPathIfPresent {
    param([Parameter(Mandatory)][string]$PathEntry)

    if ((Test-Path $PathEntry) -and (-not ($env:PATH.Split(';') -contains $PathEntry))) {
        $env:PATH = "$PathEntry;$env:PATH"
    }
}

function Resolve-MsysPrefixFromCompiler([string]$CompilerPath) {
    if (-not $CompilerPath) {
        return $null
    }

    $compilerDir = Split-Path $CompilerPath -Parent
    if (-not $compilerDir) {
        return $null
    }

    $prefix = Split-Path $compilerDir -Parent
    if (-not $prefix) {
        return $null
    }

    if (Test-Path (Join-Path $prefix 'include\GLFW\glfw3.h')) {
        return $prefix
    }

    return $null
}

function Configure-RuntimeBuild {
    $cachePath = Join-Path $buildDir 'CMakeCache.txt'
    $cmake = Get-Command cmake -ErrorAction SilentlyContinue
    if (-not $cmake) {
        Write-Host "ERROR: Missing required build tool: cmake"
        Write-Host "  Install CMake or run the full setup again: pwsh -f scripts/Setup-Alpha.ps1"
        exit 1
    }

    $gpp = Get-Command g++ -ErrorAction SilentlyContinue
    if (-not $gpp) {
        Write-Host "ERROR: Missing required compiler: g++"
        Write-Host "  If you ran Setup-Alpha.ps1, try opening a new PowerShell window and retrying."
        Write-Host "  Or run the full setup again: pwsh -f scripts/Setup-Alpha.ps1"
        exit 1
    }

    $machineVulkan = [Environment]::GetEnvironmentVariable('VULKAN_SDK', 'Machine')
    $userVulkan = [Environment]::GetEnvironmentVariable('VULKAN_SDK', 'User')
    if (-not $env:VULKAN_SDK) {
        if ($machineVulkan) {
            $env:VULKAN_SDK = $machineVulkan
        }
        elseif ($userVulkan) {
            $env:VULKAN_SDK = $userVulkan
        }
    }

    $mingwPrefix = Resolve-MsysPrefixFromCompiler $gpp.Source
    if ($mingwPrefix) {
        Add-ToPathIfPresent -PathEntry (Join-Path $mingwPrefix 'bin')
        $env:GLFW_DIR = $mingwPrefix.Replace('\', '/')
    }

    $cmakeArgs = @('-S', $repoRoot, '-B', $buildDir, '-DCMAKE_BUILD_TYPE=Release')
    if (-not (Test-Path $cachePath)) {
        $ninja = Get-Command ninja -ErrorAction SilentlyContinue
        if ($ninja) {
            $cmakeArgs += @('-G', 'Ninja')
        }
        else {
            $mingwMake = Get-Command mingw32-make -ErrorAction SilentlyContinue
            if (-not $mingwMake -and $mingwPrefix) {
                $fallbackMake = Join-Path $mingwPrefix 'bin\mingw32-make.exe'
                if (Test-Path $fallbackMake) {
                    Add-ToPathIfPresent -PathEntry (Split-Path $fallbackMake -Parent)
                    $mingwMake = Get-Command mingw32-make -ErrorAction SilentlyContinue
                }
            }

            if (-not $mingwMake) {
                Write-Host "ERROR: Missing required build tool: ninja or mingw32-make"
                Write-Host "  Install Ninja or ensure MSYS2 mingw32-make is available in PATH."
                exit 1
            }

            $cmakeArgs += @('-G', 'MinGW Makefiles', "-DCMAKE_MAKE_PROGRAM=$($mingwMake.Source)")
        }

        if ($mingwPrefix) {
            $gccPath = Join-Path $mingwPrefix 'bin\gcc.exe'
            $gxxPath = Join-Path $mingwPrefix 'bin\g++.exe'
            if (Test-Path $gccPath) {
                $cmakeArgs += "-DCMAKE_C_COMPILER=$gccPath"
            }
            if (Test-Path $gxxPath) {
                $cmakeArgs += "-DCMAKE_CXX_COMPILER=$gxxPath"
            }
        }
    }

    $null = Invoke-CheckedNative -FilePath $cmake.Source -Arguments $cmakeArgs -FailureMessage 'CMake configure failed'
    return $cmake.Source
}

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

New-Item -ItemType Directory -Force -Path $buildDir | Out-Null

Write-Host "== Building Runtime Entrypoint (C++) =="
$cmakePath = Configure-RuntimeBuild
Invoke-CheckedNative -FilePath $cmakePath -Arguments @('--build', $buildDir, '--config', 'Release', '--target', 'forge_runtime', '-j', '4') -FailureMessage 'Runtime build failed'

if ($RuntimeOnly) {
    if (Test-Path $runtimeBin) {
        Write-Host "== Starting Runtime Only =="
        & $runtimeBin $repoRoot
    }
    else {
        Write-Host "ERROR: Runtime binary unavailable after successful build."
        exit 1
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
Invoke-CheckedNative -FilePath $dotnet.Source -Arguments @("build", $editorProject, "-c", "Release", "--no-restore") -FailureMessage 'Editor build failed'

if (-not (Test-Path $editorBin)) {
    throw "Editor binary missing after successful build: $editorBin"
}

& $editorBin "--editor-ui" $runtimeBin

Write-Host "Bootstrap completed successfully."
