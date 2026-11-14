# 构建 H2sim（Windows / GitHub Actions windows-latest）
param(
    [string]$Configuration = "Release",
    [string]$Platform = "x64"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

function Resolve-NuGet {
    $cmd = Get-Command nuget -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $vendored = Join-Path $root ".nuget\NuGet.exe"
    if (Test-Path $vendored) { return $vendored }
    throw "找不到 nuget（PATH 或 .nuget\NuGet.exe）"
}

function Resolve-MSBuild {
    $cmd = Get-Command msbuild -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere) {
        $found = & $vswhere -latest -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
        if ($found) { return $found }
    }
    throw "找不到 msbuild（需要 Visual Studio Build Tools）"
}

$nuget = Resolve-NuGet
& $nuget restore DWSIM.sln -PackagesDirectory packages
if ($LASTEXITCODE -ne 0) { throw "NuGet 还原失败（exit $LASTEXITCODE）" }

$msbuild = Resolve-MSBuild
& $msbuild DWSIM.sln "/p:Configuration=$Configuration" "/p:Platform=$Platform" /m /v:m /nologo
if ($LASTEXITCODE -ne 0) { throw "编译失败（exit $LASTEXITCODE）" }

$exe = Join-Path $root "DWSIM\bin\$Platform\$Configuration\DWSIM.exe"
if (-not (Test-Path $exe)) { throw "编译完成但找不到产物: $exe" }
Write-Host "BUILD OK: $exe"