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
$script:CurrentStep = 'initializing'

# ------------------------------------------------------------
# Colored output helpers for clean, friendly progress messages.
# ------------------------------------------------------------
function Write-Step([string]$Message) { $script:CurrentStep = $Message; Write-Host "🔷 $Message" -ForegroundColor Cyan }
function Write-Ok([string]$Message) { Write-Host "✅ $Message" -ForegroundColor Green }
function Write-Warn([string]$Message) { Write-Host "⚠️  $Message" -ForegroundColor Yellow }
function Write-Fail([string]$Message) { Write-Host "❌ $Message" -ForegroundColor Red }

trap {
    Write-Fail "Setup failed during: $script:CurrentStep"
    Write-Fail "Error: $($_.Exception.Message)"
    Write-Warn '  → Check the output above for details on what went wrong.'
    Write-Warn '  → Fix the issue, then re-run: pwsh -f scripts/Setup-Alpha.ps1'
    Write-Warn '  → To start completely fresh: pwsh -f scripts/Setup-Alpha.ps1 -Fresh'
    exit 1
}

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path.TrimEnd(' ', '\')
$VenvPath = (Join-Path $RepoRoot '.venv').TrimEnd(' ')
$OrchestratorPath = (Join-Path $RepoRoot 'ai-orchestration\python').TrimEnd(' ')
$OrchestratorScript = (Join-Path $OrchestratorPath 'orchestrator.py').TrimEnd(' ')
$RequirementsFile = (Join-Path $OrchestratorPath 'requirements.txt').TrimEnd(' ')
$BootstrapScript = (Join-Path $RepoRoot 'scripts\bootstrap.ps1').TrimEnd(' ')

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

    $installed = winget list --id $Id -e --accept-source-agreements 2>$null
    if ($installed -and ($installed -match [Regex]::Escape($Id))) {
        Write-Ok "$DisplayName already installed ($Id)."
        return
    }

    Write-Step "Installing $DisplayName via winget ($Id)"
    Invoke-CheckedNative -FilePath 'winget' -Arguments @(
        'install', '-e', '--id', $Id,
        '--accept-package-agreements', '--accept-source-agreements', '--silent'
    ) -FailureMessage "Failed to install $DisplayName via winget"

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

    $registryPaths = @(
        'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*',
        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*'
    )

    foreach ($regPath in $registryPaths) {
        $entries = Get-ItemProperty -Path $regPath -ErrorAction SilentlyContinue
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
    Write-Step 'Ensuring MSYS2 + MinGW g++ are installed'

    $msysRoot = Find-MsysRoot
    if ($msysRoot) {
        Write-Ok "MSYS2 already installed at $msysRoot — skipping winget install."
    }
    else {
        Ensure-WingetPackage -Id 'MSYS2.MSYS2' -DisplayName 'MSYS2'
        $msysRoot = Find-MsysRoot
    }

    if (-not $msysRoot) {
        throw @"
MSYS2 installation was not found after winget finished.
Next steps:
  1) Open Start Menu -> "MSYS2 UCRT64" once (it completes first-run initialization).
  2) Confirm one of these folders exists:
       C:\msys64\usr\bin\bash.exe
       C:\ProgramData\MSYS2\usr\bin\bash.exe
  3) Re-run: pwsh -f scripts/Setup-Alpha.ps1 -Fresh
"@
    }

    $msysBash = (Join-Path $msysRoot 'usr\bin\bash.exe').TrimEnd(' ')
    Write-Step "Using MSYS2 root: $msysRoot"

    # Initialise the pacman GPG keyring explicitly before syncing.
    # Without this, pacman spawns gpg-agent in a separate window on first run
    # which can stall or confuse the installer if the window is closed.
    Write-Step 'Initialising MSYS2 package keyring (one-time step, may take a moment)'
    Invoke-CheckedNative -FilePath $msysBash -Arguments @('-lc', 'pacman-key --init && pacman-key --populate msys2') -FailureMessage 'MSYS2 keyring initialisation failed'

    Write-Step 'Syncing MSYS2 package index'
    Invoke-CheckedNative -FilePath $msysBash -Arguments @('-lc', 'pacman --noconfirm -Sy') -FailureMessage 'MSYS2 package index sync failed'

    Write-Step 'Installing MinGW g++ via MSYS2'
    Invoke-CheckedNative -FilePath $msysBash -Arguments @('-lc', 'pacman --noconfirm --needed -S mingw-w64-ucrt-x86_64-gcc make') -FailureMessage 'MSYS2 MinGW g++ install failed'

    Add-MsysToPathCurrentSession -MsysRoot $msysRoot

    if (-not (Get-Command g++ -ErrorAction SilentlyContinue)) {
        throw @"
g++ was not found in this PowerShell session after MSYS2 install.
Tried root: $msysRoot
Please verify this path exists: $msysRoot\ucrt64\bin\g++.exe
Then open a new PowerShell and run:
  pwsh -f scripts/Setup-Alpha.ps1
"@
    }

    Write-Ok 'g++ is available from MSYS2 MinGW toolchain.'
}

function Refresh-ProcessEnvironment {
    $machinePath = [Environment]::GetEnvironmentVariable('Path', 'Machine')
    $userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
    if ($machinePath -or $userPath) {
        $env:Path = ($machinePath, $userPath -ne $null ? $userPath : '') -join ';'
    }

    $machineVulkan = [Environment]::GetEnvironmentVariable('VULKAN_SDK', 'Machine')
    $userVulkan = [Environment]::GetEnvironmentVariable('VULKAN_SDK', 'User')
    if ($machineVulkan) { $env:VULKAN_SDK = $machineVulkan }
    elseif ($userVulkan) { $env:VULKAN_SDK = $userVulkan }
}

function Ensure-VulkanSdk {
    Write-Step 'Installing Vulkan SDK (critical dependency)'

    try {
        Ensure-WingetPackage -Id 'KhronosGroup.VulkanSDK' -DisplayName 'Khronos Vulkan SDK'
    }
    catch {
        Write-Fail 'Vulkan SDK installation via winget failed.'
        Write-Warn 'Manual fallback steps:'
        Write-Warn '  1) Download installer: https://vulkan.lunarg.com/sdk/home#windows'
        Write-Warn '  2) Install with default options'
        Write-Warn '  3) Reopen PowerShell and re-run this script'
        throw
    }

    Refresh-ProcessEnvironment

    if (-not $env:VULKAN_SDK) {
        Write-Warn 'Vulkan SDK appears installed, but VULKAN_SDK is not visible yet in this shell.'
        Write-Warn 'If this is your first install, reboot or open a new PowerShell and rerun this script.'
        Write-Warn 'Manual installer link: https://vulkan.lunarg.com/sdk/home#windows'
    }
    else {
        Write-Ok "Vulkan SDK detected: $env:VULKAN_SDK"
    }
}

function Get-PythonLauncher {
    $pythonCmd = Get-Command python -ErrorAction SilentlyContinue
    if ($pythonCmd) { return @('python') }

    $pyCmd = Get-Command py -ErrorAction SilentlyContinue
    if ($pyCmd) { return @('py', '-3') }

    throw 'Could not find python or py launcher after installation.'
}

function Ensure-Venv {
    $createdFresh = $false

    if ((Test-Path $VenvPath) -and (-not $Fresh)) {
        Write-Ok 'Reusing existing .venv (model prep/benchmark will be skipped).'
    }
    else {
        if ((Test-Path $VenvPath) -and $Fresh) {
            Write-Step '-Fresh specified: deleting existing .venv'
            Remove-Item -Recurse -Force $VenvPath
        }

        Write-Step 'Creating Python virtual environment at .\.venv'
        $launcher = Get-PythonLauncher
        if ($launcher[0] -eq 'py') {
            Invoke-CheckedNative -FilePath 'py' -Arguments @('-3', '-m', 'venv', $VenvPath) -FailureMessage 'Failed to create virtual environment with py launcher'
        }
        else {
            Invoke-CheckedNative -FilePath 'python' -Arguments @('-m', 'venv', $VenvPath) -FailureMessage 'Failed to create virtual environment with python'
        }

        $createdFresh = $true
    }

    $activateScript = (Join-Path $VenvPath 'Scripts\Activate.ps1').TrimEnd(' ')
    if (-not (Test-Path $activateScript)) {
        throw "Could not find venv activation script at $activateScript"
    }

    Write-Step 'Activating virtual environment'
    . $activateScript

    $venvPython = (Join-Path $VenvPath 'Scripts\python.exe').TrimEnd(' ')
    if (-not (Test-Path $venvPython)) {
        throw "Virtual environment python not found at $venvPython"
    }

    Write-Step 'Creating/updating forge.pth for ai-orchestration/python'
    $sitePackages = & $venvPython -c "import site; print(next(p for p in site.getsitepackages() if p.endswith('site-packages')))"
    if ($LASTEXITCODE -ne 0 -or -not $sitePackages) {
        throw 'Failed to discover site-packages path in the virtual environment.'
    }

    $pthFile = (Join-Path $sitePackages 'forge.pth').TrimEnd(' ')
    Set-Content -Path $pthFile -Value $OrchestratorPath -Encoding utf8
    Write-Ok "forge.pth written: $pthFile"

    if (Test-Path $RequirementsFile) {
        Write-Step "Installing Python dependencies from $RequirementsFile"
        Invoke-CheckedNative -FilePath $venvPython -Arguments @('-m', 'pip', 'install', '--upgrade', 'pip') -FailureMessage 'pip upgrade failed'
        Invoke-CheckedNative -FilePath $venvPython -Arguments @('-m', 'pip', 'install', '-r', $RequirementsFile) -FailureMessage 'requirements install failed'
    }
    else {
        Write-Warn "No requirements.txt found at $RequirementsFile (skipping pip install)."
    }

    return [PSCustomObject]@{
        CreatedFresh = $createdFresh
        VenvPython = $venvPython
    }
}

function Run-OrchestratorStep {
    param(
        [Parameter(Mandatory)][string]$VenvPython,
        [Parameter(Mandatory)][string]$Argument,
        [Parameter(Mandatory)][string]$StepLabel
    )

    Write-Step $StepLabel
    Invoke-CheckedNative -FilePath $VenvPython -Arguments @($OrchestratorScript, $Argument) -FailureMessage "Orchestrator step failed: $Argument"
}

# ------------------------------------------------------------
# Main setup flow.
# ------------------------------------------------------------
Write-Host ''
Write-Host '╔══════════════════════════════════════════════════╗' -ForegroundColor Cyan
Write-Host '║       ForgeEngine Alpha Setup — Windows          ║' -ForegroundColor Cyan
Write-Host '╚══════════════════════════════════════════════════╝' -ForegroundColor Cyan
Write-Host ''
Write-Host 'This script will install the following (skipping anything already present):' -ForegroundColor White
Write-Host '  • .NET 8 SDK          — runs the ForgeEngine editor' -ForegroundColor Gray
Write-Host '  • CMake + Ninja       — C++ build system' -ForegroundColor Gray
Write-Host '  • Python 3.12         — AI orchestration layer' -ForegroundColor Gray
Write-Host '  • Vulkan SDK          — graphics API' -ForegroundColor Gray
Write-Host '  • MSYS2 + MinGW g++   — C++ compiler for the runtime' -ForegroundColor Gray
Write-Host ''
Write-Host 'Estimated time: 5–15 minutes depending on internet speed.' -ForegroundColor DarkGray
Write-Host 'You may be prompted for administrator permission during installation.' -ForegroundColor DarkGray
Write-Host ''

$script:SetupStartTime = Get-Date

Write-Step 'Starting ForgeEngine Alpha setup for Windows 10/11'
Ensure-Command -Command 'winget' -Hint 'Install/enable App Installer from Microsoft Store, then rerun this script.'

Write-Host ''
Write-Host '── Installing dependencies ─────────────────────────' -ForegroundColor DarkGray
Ensure-WingetPackage -Id 'Microsoft.DotNet.SDK.8' -DisplayName '.NET 8 SDK'
Ensure-WingetPackage -Id 'Kitware.CMake' -DisplayName 'CMake'
Ensure-WingetPackage -Id 'Ninja-build.Ninja' -DisplayName 'Ninja'
Ensure-WingetPackage -Id 'Python.Python.3.12' -DisplayName 'Python 3.12'

# Refresh PATH so tools installed above are visible in this session.
Write-Step 'Refreshing environment PATH for newly installed tools'
Refresh-ProcessEnvironment

Ensure-VulkanSdk
Ensure-MsysGpp

Write-Host ''
Write-Host '── Building runtime ────────────────────────────────' -ForegroundColor DarkGray
Write-Step 'Running ForgeEngine bootstrap (compiling C++ runtime)'
Invoke-CheckedNative -FilePath 'pwsh' -Arguments @('-f', $BootstrapScript) -FailureMessage 'bootstrap.ps1 failed'
Write-Ok 'Bootstrap completed.'

Write-Host ''
Write-Host '── Setting up Python environment ───────────────────' -ForegroundColor DarkGray
Set-Location $RepoRoot
$venvInfo = Ensure-Venv

if ($venvInfo.CreatedFresh) {
    Write-Host ''
    Write-Host '── Preparing AI models ─────────────────────────────' -ForegroundColor DarkGray
    Run-OrchestratorStep -VenvPython $venvInfo.VenvPython -Argument '--prepare-models' -StepLabel 'Preparing AI models (fresh venv)'
    Run-OrchestratorStep -VenvPython $venvInfo.VenvPython -Argument '--benchmark' -StepLabel 'Running AI benchmark (fresh venv)'
    Write-Ok 'Model preparation and benchmark complete.'
}
else {
    Write-Warn 'Skipped model prep/benchmark because existing .venv was reused. Use -Fresh to force rerun.'
}

$elapsed = [math]::Round(((Get-Date) - $script:SetupStartTime).TotalMinutes, 1)

Write-Host ''
Write-Host '╔══════════════════════════════════════════════════╗' -ForegroundColor Green
Write-Host '║         Setup complete!  Time: ' -ForegroundColor Green -NoNewline
Write-Host ('{0} min' -f $elapsed).PadRight(17) -ForegroundColor Green -NoNewline
Write-Host '║' -ForegroundColor Green
Write-Host '╚══════════════════════════════════════════════════╝' -ForegroundColor Green
Write-Host ''
Write-Host 'To launch the editor:' -ForegroundColor White
Write-Host '  dotnet run --project editor/csharp/GameForge.Editor.csproj' -ForegroundColor Cyan
Write-Host ''
Write-Host 'To launch just the runtime:' -ForegroundColor White
Write-Host '  pwsh -f scripts/bootstrap.ps1 -RuntimeOnly' -ForegroundColor Cyan
Write-Host ''
Write-Host 'For CI/headless launcher smoke:' -ForegroundColor White
Write-Host '  pwsh -f scripts/bootstrap.ps1 -LauncherSmoke' -ForegroundColor Cyan
Write-Host ''
Write-Host 'To do a full clean reinstall next time:' -ForegroundColor DarkGray
Write-Host '  pwsh -f scripts/Setup-Alpha.ps1 -Fresh' -ForegroundColor DarkGray
