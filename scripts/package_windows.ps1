param(
    [string]$Configuration = "Release",
    [string]$Version = "0.1.0"
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$rid = "win-x64"
$outputRoot = Join-Path $repoRoot "build/release/$rid"
$publishDir = Join-Path $outputRoot "publish"
$runtimeDir = Join-Path $outputRoot "runtime"
$runtimeExe = Join-Path $runtimeDir "forgeengine_runtime.exe"
$packageRoot = Join-Path $outputRoot "package"
$appPayload = Join-Path $packageRoot "ForgeEngine"
$wxsPath = Join-Path $packageRoot "ForgeEngine.wxs"
$msiPath = Join-Path $outputRoot "ForgeEngine-$Version-win-x64.msi"
$sampleBrief = Join-Path $repoRoot "app/samples/interview-brief.sample.json"
$playtestScenario = Join-Path $repoRoot "app/samples/generated-prototype/cozy-colony-tales/testing/bot-baseline-scenario.v1.json"

New-Item -ItemType Directory -Force -Path $publishDir, $runtimeDir, $packageRoot | Out-Null

Write-Host "== ForgeEngine Windows packaging =="
Write-Host "Version: $Version"

Write-Host "[1/7] Building C++ runtime"
$gpp = Get-Command g++ -ErrorAction SilentlyContinue
if (-not $gpp) { throw "g++ not found. Install MinGW-w64." }
& $gpp.Source "-std=c++17" (Join-Path $repoRoot "runtime/cpp/main.cpp") "-o" $runtimeExe

Write-Host "[2/7] Publishing .NET editor"
& dotnet publish (Join-Path $repoRoot "editor/csharp/GameForge.Editor.csproj") `
    -c $Configuration `
    -r $rid `
    --self-contained true `
    /p:PublishSingleFile=true `
    /p:IncludeNativeLibrariesForSelfExtract=true `
    -o $publishDir

Write-Host "[3/7] Staging app payload"
if (Test-Path $appPayload) { Remove-Item -Recurse -Force $appPayload }
New-Item -ItemType Directory -Path $appPayload | Out-Null
Copy-Item -Recurse -Force (Join-Path $publishDir "*") $appPayload
Copy-Item -Force $runtimeExe (Join-Path $appPayload "forgeengine_runtime.exe")
Copy-Item -Recurse -Force (Join-Path $repoRoot "ai-orchestration") (Join-Path $appPayload "ai-orchestration")
Copy-Item -Recurse -Force (Join-Path $repoRoot "app") (Join-Path $appPayload "app")

Write-Host "[4/7] Running post-build validation"
& python (Join-Path $repoRoot "ai-orchestration/python/orchestrator.py") --prepare-models
& python (Join-Path $repoRoot "ai-orchestration/python/orchestrator.py") --benchmark
& python (Join-Path $repoRoot "ai-orchestration/python/orchestrator.py") `
    --run-generation-pipeline `
    --generate-prototype $sampleBrief `
    --output (Join-Path $repoRoot "build/generated-prototypes") `
    --bot-playtest-scenario $playtestScenario
& python (Join-Path $repoRoot "scripts/run_smoke_and_capture_evidence.py") --os windows --output-root (Join-Path $repoRoot "build/release-evidence")

Write-Host "[5/7] Building MSI (WiX)"
$wix = Get-Command wix -ErrorAction SilentlyContinue
if (-not $wix) { throw "wix CLI not found. Install WiX Toolset v4." }

$appPayloadEscaped = $appPayload -replace "\\", "\\\\"
@"
<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
  <Package Name="ForgeEngine" Manufacturer="ForgeEngine" Version="$Version" UpgradeCode="6F4F0A9F-409C-44AB-9883-9D0A9CE6D0BE">
    <MediaTemplate EmbedCab="yes" />
    <StandardDirectory Id="ProgramFiles64Folder">
      <Directory Id="INSTALLFOLDER" Name="ForgeEngine">
        <Component Id="MainExeComponent" Guid="3B3EE665-D90D-4A48-BA38-34F24DB0588D">
          <File Source="$appPayloadEscaped\\GameForge.Editor.exe" KeyPath="yes" />
        </Component>
      </Directory>
    </StandardDirectory>
    <Feature Id="MainFeature" Title="ForgeEngine" Level="1">
      <ComponentRef Id="MainExeComponent" />
    </Feature>
  </Package>
</Wix>
"@ | Set-Content -Encoding UTF8 $wxsPath

& $wix.Source "build" $wxsPath "-o" $msiPath

Write-Host "[6/7] Writing manifest"
@{
    version = $Version
    rid = $rid
    msi = (Split-Path $msiPath -Leaf)
    runtime_binary = "forgeengine_runtime.exe"
    post_build_validation = @(
        "orchestrator.py --prepare-models",
        "orchestrator.py --benchmark",
        "orchestrator.py --run-generation-pipeline --generate-prototype app/samples/interview-brief.sample.json",
        "run_smoke_and_capture_evidence.py --os windows"
    )
} | ConvertTo-Json -Depth 5 | Set-Content -Encoding UTF8 (Join-Path $outputRoot "release_manifest.json")

Write-Host "[7/7] Done"
Write-Host "Artifacts in $outputRoot"
