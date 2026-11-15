# 构建 Windows 单文件绿色版：NSIS 自解压到 %LOCALAPPDATA%\H2sim\<version> 并启动 DWSIM.exe
param(
    [string]$Configuration = "Release",
    [string]$Platform = "x64"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$version = (Get-Content VERSION -Raw).Trim()
$dist = Join-Path $root "DistPackages\Windows"
if (-not (Test-Path (Join-Path $dist "DWSIM.exe"))) {
    throw "Missing DWSIM.exe in $dist; run scripts/build.ps1 first."
}

$makensis = (Get-Command makensis -ErrorAction SilentlyContinue).Source
if (-not $makensis) {
    foreach ($candidate in @("$env:ProgramFiles\NSIS\makensis.exe", "${env:ProgramFiles(x86)}\NSIS\makensis.exe")) {
        if (Test-Path $candidate) { $makensis = $candidate; break }
    }
}
if (-not $makensis) { throw "makensis not found; install NSIS." }

New-Item -ItemType Directory -Force (Join-Path $root "release\windows") | Out-Null

& $makensis "/DVERSION=$version" "/DOUTDIR=release\windows" "packaging\windows\H2sim.nsi"
if ($LASTEXITCODE -ne 0) { throw "makensis failed ($LASTEXITCODE)" }

$exe = Join-Path $root "release\windows\H2sim-$version-windows-x64.exe"
if (-not (Test-Path $exe)) { throw "Missing package: $exe" }
Write-Host "PACKAGE OK: $exe"