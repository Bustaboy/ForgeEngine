$ErrorActionPreference = 'Stop'
$projectDir = Split-Path -Path $MyInvocation.MyCommand.Path -Parent
Set-Location $projectDir
g++ -std=c++17 runtime/main.cpp -o runtime/prototype_runtime.exe
./runtime/prototype_runtime.exe
