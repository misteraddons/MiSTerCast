param(
    [string]$Configuration = "Debug",
    [string]$Platform = "Any CPU",
    [string]$PlatformToolset = "v143",
    [switch]$Clean
)

$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$Solution = Join-Path $RepoRoot "FrontEnd\MiSTerCast.sln"
$Lz4Dir = Join-Path $RepoRoot "External\lz4"
$Lz4Header = Join-Path $Lz4Dir "include\lz4.h"

function Get-MSBuildPath {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path -LiteralPath $vswhere) {
        $found = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find "MSBuild\Current\Bin\amd64\MSBuild.exe" | Select-Object -First 1
        if ($found) {
            return $found
        }
    }

    $fromPath = (Get-Command MSBuild.exe -ErrorAction SilentlyContinue | Select-Object -First 1).Source
    if ($fromPath) {
        return $fromPath
    }

    throw "MSBuild.exe not found. Install Visual Studio Build Tools with MSBuild."
}

function Get-TargetFrameworkVersion {
    $frameworkRoot = Join-Path ${env:ProgramFiles(x86)} "Reference Assemblies\Microsoft\Framework\.NETFramework"
    foreach ($version in @("v4.6.1", "v4.8.1", "v4.8")) {
        if (Test-Path -LiteralPath (Join-Path $frameworkRoot $version)) {
            return $version
        }
    }

    throw ".NET Framework targeting pack not found. Install v4.6.1 or v4.8+ targeting packs."
}

function Get-WindowsSdkVersion {
    $sdkRoot = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\Include"
    $preferred = "10.0.22621.0"
    if (Test-Path -LiteralPath (Join-Path $sdkRoot $preferred)) {
        return $preferred
    }

    $latest = Get-ChildItem -LiteralPath $sdkRoot -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match '^\d+\.\d+\.\d+\.\d+$' } |
        Sort-Object { [version]$_.Name } -Descending |
        Select-Object -First 1

    if ($latest) {
        return $latest.Name
    }

    throw "Windows 10 SDK headers not found."
}

function Ensure-Lz4 {
    if (Test-Path -LiteralPath $Lz4Header) {
        return
    }

    Write-Host "Downloading LZ4 1.9.4..."
    $zip = Join-Path $env:TEMP "lz4_win32_v1_9_4.zip"
    Invoke-WebRequest -Uri "https://github.com/lz4/lz4/releases/download/v1.9.4/lz4_win32_v1_9_4.zip" -OutFile $zip

    if (Test-Path -LiteralPath $Lz4Dir) {
        $resolved = (Resolve-Path -LiteralPath $Lz4Dir).Path
        if (-not $resolved.StartsWith($RepoRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove LZ4 directory outside repo: $resolved"
        }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }

    New-Item -ItemType Directory -Path $Lz4Dir | Out-Null
    Expand-Archive -LiteralPath $zip -DestinationPath $Lz4Dir -Force
}

$msbuild = Get-MSBuildPath
$targetFramework = Get-TargetFrameworkVersion
$windowsSdk = Get-WindowsSdkVersion
Ensure-Lz4

$commonArgs = @(
    $Solution,
    "/p:Configuration=$Configuration",
    "/p:Platform=$Platform",
    "/p:PlatformToolset=$PlatformToolset",
    "/p:WindowsTargetPlatformVersion=$windowsSdk",
    "/p:TargetFrameworkVersion=$targetFramework"
)

Write-Host "MSBuild: $msbuild"
Write-Host "Framework: $targetFramework"
Write-Host "Windows SDK: $windowsSdk"
Write-Host "Toolset: $PlatformToolset"

if ($Clean) {
    & $msbuild @commonArgs /t:Clean
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

& $msbuild @commonArgs
exit $LASTEXITCODE
