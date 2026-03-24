#requires -Version 7.0
<#
  ForgeEngine Alpha Setup (Windows 10/11)
  One-command, idempotent environment bootstrap for testers.
#>

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

# ------------------------------------------------------------
# Colored output helpers for clean, friendly progress messages.
# ------------------------------------------------------------
function Write-Step([string]$Message) { Write-Host "▶ $Message" -ForegroundColor Cyan }
function Write-Ok([string]$Message) { Write-Host "✔ $Message" -ForegroundColor Green }
function Write-Warn([string]$Message) { Write-Host "⚠ $Message" -ForegroundColor Yellow }
function Write-Fail([string]$Message) { Write-Host "✖ $Message" -ForegroundColor Red }

trap {
    Write-Fail "Setup failed: $($_.Exception.Message)"
    exit 1
}

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$VenvPath = Join-Path $RepoRoot '.venv'
$OrchestratorPath = Join-Path $RepoRoot 'ai-orchestration/python'

# ------------------------------------------------------------
# Utility helpers for reliable idempotent installs.
# ------------------------------------------------------------
function Ensure-Command {
    param(
        [Parameter(Mandatory)][string]$Command,
        [string]$Hint = ''
    )

    if (-not (Get-Command $Command -ErrorAction SilentlyContinue)) {
        Write-Fail "Missing required command: $Command"
        if ($Hint) { Write-Warn $Hint }
        exit 1
    }
}

function Ensure-WingetPackage {
    param(
        [Parameter(Mandatory)][string]$Id,
        [Parameter(Mandatory)][string]$DisplayName
    )

    $already = winget list --id $Id -e --accept-source-agreements 2>$null
    if ($already -and ($already -match [Regex]::Escape($Id))) {
        Write-Ok "$DisplayName already installed ($Id)."
        return
    }

    Write-Step "Installing $DisplayName via winget ($Id)"
    winget install -e --id $Id --accept-package-agreements --accept-source-agreements --silent
    Write-Ok "$DisplayName installed."
}

function Add-MsysToPathCurrentSession {
    $candidatePaths = @(
        'C:\msys64\ucrt64\bin',
        'C:\msys64\mingw64\bin',
        'C:\msys64\usr\bin'
    )

    foreach ($path in $candidatePaths) {
        if (Test-Path $path) {
            if (-not ($env:PATH.Split(';') -contains $path)) {
                $env:PATH = "$path;$env:PATH"
            }
        }
    }
}

function Ensure-MsysGpp {
    Write-Step 'Ensuring MSYS2/MinGW g++ is installed'
    Ensure-WingetPackage -Id 'MSYS2.MSYS2' -DisplayName 'MSYS2'

    $msysBash = 'C:\msys64\usr\bin\bash.exe'
    if (-not (Test-Path $msysBash)) {
        throw 'MSYS2 was installed but bash.exe was not found at C:\msys64\usr\bin\bash.exe'
    }

    & $msysBash -lc 'pacman --noconfirm -Sy'
    & $msysBash -lc 'pacman --noconfirm --needed -S mingw-w64-ucrt-x86_64-gcc make'

    Add-MsysToPathCurrentSession

    if (-not (Get-Command g++ -ErrorAction SilentlyContinue)) {
        throw 'g++ is still unavailable after MSYS2 install. Open a new shell and re-run Setup-Alpha.ps1.'
    }

    Write-Ok 'g++ is available from MSYS2 MinGW toolchain.'
}

# ------------------------------------------------------------
# Install all required system dependencies via winget.
# ------------------------------------------------------------
Write-Step 'Starting ForgeEngine Alpha setup for Windows 10/11'
Ensure-Command -Command 'winget' -Hint 'Install/enable App Installer (winget) from Microsoft Store, then rerun.'

Ensure-WingetPackage -Id 'Microsoft.DotNet.SDK.8' -DisplayName '.NET 8 SDK'
Ensure-WingetPackage -Id 'Kitware.CMake' -DisplayName 'CMake'
Ensure-WingetPackage -Id 'Ninja-build.Ninja' -DisplayName 'Ninja'
Ensure-WingetPackage -Id 'Python.Python.3.12' -DisplayName 'Python 3.12'

# Vulkan SDK is critical; provide explicit fallback instructions if install fails.
Write-Step 'Installing Vulkan SDK (critical dependency)'
try {
    Ensure-WingetPackage -Id 'KhronosGroup.VulkanSDK' -DisplayName 'Khronos Vulkan SDK'
}
catch {
    Write-Fail 'Failed to install Vulkan SDK automatically via winget.'
    Write-Warn 'Manual fallback: download and install from https://vulkan.lunarg.com/sdk/home'
    Write-Warn 'Then re-run this script.'
    throw
}

Ensure-MsysGpp

# ------------------------------------------------------------
# Run existing project bootstrap script.
# ------------------------------------------------------------
Write-Step 'Running existing ForgeEngine bootstrap.ps1'
pwsh -f (Join-Path $RepoRoot 'scripts/bootstrap.ps1')
Write-Ok 'Bootstrap script completed.'

# ------------------------------------------------------------
# Create a clean venv and persistent forge.pth import path.
# ------------------------------------------------------------
Write-Step 'Creating clean Python virtual environment at .\.venv'
if (Test-Path $VenvPath) {
    Remove-Item -Recurse -Force $VenvPath
}

$pythonCmd = (Get-Command python -ErrorAction SilentlyContinue)
if (-not $pythonCmd) {
    $pythonCmd = Get-Command py -ErrorAction SilentlyContinue
}
if (-not $pythonCmd) {
    throw 'Could not find python or py launcher after installation.'
}

if ($pythonCmd.Name -eq 'py') {
    & py -3 -m venv $VenvPath
}
else {
    & python -m venv $VenvPath
}

$activateScript = Join-Path $VenvPath 'Scripts/Activate.ps1'
. $activateScript

Write-Step 'Writing forge.pth so ai-orchestration/python is always in PYTHONPATH'
$sitePackages = python -c "import site; print(next(p for p in site.getsitepackages() if p.endswith('site-packages')))"
$pthFile = Join-Path $sitePackages 'forge.pth'
Set-Content -Path $pthFile -Value $OrchestratorPath -Encoding utf8
Write-Ok 'Virtual environment and forge.pth configured.'

# ------------------------------------------------------------
# Run orchestrator setup/benchmark commands in activated venv.
# ------------------------------------------------------------
Set-Location $RepoRoot

Write-Step 'Preparing AI models'
python ai-orchestration/python/orchestrator.py --prepare-models

Write-Step 'Running AI benchmark'
python ai-orchestration/python/orchestrator.py --benchmark

# ------------------------------------------------------------
# Friendly completion summary for alpha testers.
# ------------------------------------------------------------
Write-Ok 'All done! ForgeEngine Alpha environment is ready. 🎉'
Write-Host ''
Write-Host 'Run the editor with:' -ForegroundColor Green
Write-Host '  pwsh -f scripts/bootstrap.ps1' -ForegroundColor White
Write-Host 'or' -ForegroundColor DarkGray
Write-Host '  dotnet run --project editor/csharp/GameForge.Editor.csproj' -ForegroundColor White
