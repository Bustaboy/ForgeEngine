#requires -Version 7.0
<#
  ForgeEngine Alpha Setup (Windows 10/11)
  One-command, idempotent environment bootstrap for testers.
#>

[CmdletBinding()]
param(
    [switch]$Fresh
)

$ErrorActionPreference = 'Stop'
if ($args -contains '/Fresh') { $Fresh = $true }

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
$OrchestratorScript = Join-Path $OrchestratorPath 'orchestrator.py'
$RequirementsFile = Join-Path $OrchestratorPath 'requirements.txt'

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
    Invoke-CheckedNative -FilePath 'winget' -Arguments @('install', '-e', '--id', $Id, '--accept-package-agreements', '--accept-source-agreements', '--silent') -FailureMessage "Failed to install $DisplayName via winget"
    Write-Ok "$DisplayName installed."
}

function Find-MsysRoot {
    $knownRoots = @(
        'C:\msys64',
        'C:\ProgramData\MSYS2'
    )

    foreach ($root in $knownRoots) {
        if (Test-Path (Join-Path $root 'usr\bin\bash.exe')) {
            return $root
        }
    }

    $uninstallPaths = @(
        'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*',
        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*'
    )

    foreach ($path in $uninstallPaths) {
        $entries = Get-ItemProperty -Path $path -ErrorAction SilentlyContinue
        foreach ($entry in $entries) {
            if ($entry.DisplayName -like '*MSYS2*' -and $entry.InstallLocation) {
                $candidate = $entry.InstallLocation.TrimEnd('\\')
                if (Test-Path (Join-Path $candidate 'usr\bin\bash.exe')) {
                    return $candidate
                }
            }
        }
    }

    return $null
}

function Add-MsysToPathCurrentSession {
    param([Parameter(Mandatory)][string]$MsysRoot)

    $candidatePaths = @(
        (Join-Path $MsysRoot 'ucrt64\bin'),
        (Join-Path $MsysRoot 'mingw64\bin'),
        (Join-Path $MsysRoot 'usr\bin')
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

    $msysRoot = Find-MsysRoot
    if (-not $msysRoot) {
        throw 'MSYS2 was installed but not found in C:\msys64, C:\ProgramData\MSYS2, or uninstall registry keys. Open MSYS2 once, then rerun Setup-Alpha.ps1.'
    }

    $msysBash = Join-Path $msysRoot 'usr\bin\bash.exe'
    Invoke-CheckedNative -FilePath $msysBash -Arguments @('-lc', 'pacman --noconfirm -Sy') -FailureMessage 'MSYS2 pacman sync failed'
    Invoke-CheckedNative -FilePath $msysBash -Arguments @('-lc', 'pacman --noconfirm --needed -S mingw-w64-ucrt-x86_64-gcc make') -FailureMessage 'MSYS2 gcc package install failed'

    Add-MsysToPathCurrentSession -MsysRoot $msysRoot

    if (-not (Get-Command g++ -ErrorAction SilentlyContinue)) {
        throw "g++ is still unavailable after MSYS2 install. Confirm $msysRoot\ucrt64\bin exists, then re-run Setup-Alpha.ps1."
    }

    Write-Ok 'g++ is available from MSYS2 MinGW toolchain.'
}

function Get-PythonCommand {
    $pythonCmd = Get-Command python -ErrorAction SilentlyContinue
    if ($pythonCmd) { return @('python') }

    $pyLauncher = Get-Command py -ErrorAction SilentlyContinue
    if ($pyLauncher) { return @('py', '-3') }

    throw 'Could not find python or py launcher after installation.'
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
Invoke-CheckedNative -FilePath 'pwsh' -Arguments @('-f', (Join-Path $RepoRoot 'scripts/bootstrap.ps1')) -FailureMessage 'bootstrap.ps1 failed'
Write-Ok 'Bootstrap script completed.'

# ------------------------------------------------------------
# Create/reuse venv and persistent forge.pth import path.
# ------------------------------------------------------------
$createdFreshVenv = $false
if ((Test-Path $VenvPath) -and (-not $Fresh)) {
    Write-Step 'Reusing existing virtual environment at .\.venv'
}
else {
    if ((Test-Path $VenvPath) -and $Fresh) {
        Write-Step '--Fresh provided: removing existing .\.venv'
        Remove-Item -Recurse -Force $VenvPath
    }

    Write-Step 'Creating Python virtual environment at .\.venv'
    $pyCmdParts = Get-PythonCommand
    if ($pyCmdParts[0] -eq 'py') {
        Invoke-CheckedNative -FilePath 'py' -Arguments @('-3', '-m', 'venv', $VenvPath) -FailureMessage 'Failed to create virtual environment'
    }
    else {
        Invoke-CheckedNative -FilePath 'python' -Arguments @('-m', 'venv', $VenvPath) -FailureMessage 'Failed to create virtual environment'
    }
    $createdFreshVenv = $true
}

$activateScript = Join-Path $VenvPath 'Scripts/Activate.ps1'
. $activateScript

Write-Step 'Writing forge.pth so ai-orchestration/python is always in PYTHONPATH'
$sitePackages = python -c "import site; print(next(p for p in site.getsitepackages() if p.endswith('site-packages')))"
if ($LASTEXITCODE -ne 0) {
    throw 'Failed to discover site-packages path in virtual environment.'
}
$pthFile = Join-Path $sitePackages 'forge.pth'
Set-Content -Path $pthFile -Value $OrchestratorPath -Encoding utf8
Write-Ok 'forge.pth configured.'

if (Test-Path $RequirementsFile) {
    Write-Step "Installing Python dependencies from $RequirementsFile"
    Invoke-CheckedNative -FilePath 'python' -Arguments @('-m', 'pip', 'install', '--upgrade', 'pip') -FailureMessage 'Failed to upgrade pip'
    Invoke-CheckedNative -FilePath 'python' -Arguments @('-m', 'pip', 'install', '-r', $RequirementsFile) -FailureMessage 'Failed to install Python requirements'
}
else {
    Write-Warn "No requirements.txt found at $RequirementsFile (skipping pip install)."
}

# ------------------------------------------------------------
# Run orchestrator setup/benchmark only for new/fresh venv.
# ------------------------------------------------------------
Set-Location $RepoRoot
if ($createdFreshVenv) {
    Write-Step 'Preparing AI models'
    Invoke-CheckedNative -FilePath 'python' -Arguments @($OrchestratorScript, '--prepare-models') -FailureMessage 'orchestrator --prepare-models failed'

    Write-Step 'Running AI benchmark'
    Invoke-CheckedNative -FilePath 'python' -Arguments @($OrchestratorScript, '--benchmark') -FailureMessage 'orchestrator --benchmark failed'
}
else {
    Write-Warn 'Skipping model prep/benchmark because existing .venv was reused. Use -Fresh (or /Fresh) to force rerun.'
}

# ------------------------------------------------------------
# Friendly completion summary for alpha testers.
# ------------------------------------------------------------
Write-Ok 'All done! ForgeEngine Alpha environment is ready. 🎉'
Write-Host ''
Write-Host 'Run the editor with:' -ForegroundColor Green
Write-Host '  pwsh -f scripts/bootstrap.ps1' -ForegroundColor White
Write-Host 'or' -ForegroundColor DarkGray
Write-Host '  dotnet run --project editor/csharp/GameForge.Editor.csproj' -ForegroundColor White
