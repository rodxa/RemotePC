[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+([\-+][0-9A-Za-z\.-]+)?$')]
    [string]$Version,

    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"

$AppName = "RemotePC"
$ProjectRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$ProjectFile = Join-Path $ProjectRoot "RemotePC.csproj"
$PublishDir = Join-Path $ProjectRoot "artifacts\publish\$Runtime"
$ReleasesDir = Join-Path $ProjectRoot "Releases"
$IconPath = Join-Path $ProjectRoot "Assets\AppIcon.ico"

function Test-InProjectRoot {
    param([Parameter(Mandatory = $true)][string]$Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $rootPath = [System.IO.Path]::GetFullPath($ProjectRoot)
    return $fullPath.StartsWith($rootPath, [System.StringComparison]::OrdinalIgnoreCase)
}

function Get-VpkCommand {
    $globalVpk = Get-Command "vpk" -ErrorAction SilentlyContinue
    if ($globalVpk -and $globalVpk.Source) {
        return [pscustomobject]@{
            File = $globalVpk.Source
            PrefixArgs = @()
        }
    }

    $userTool = Join-Path $env:USERPROFILE ".dotnet\tools\vpk.exe"
    if (Test-Path -LiteralPath $userTool) {
        return [pscustomobject]@{
            File = $userTool
            PrefixArgs = @()
        }
    }

    $manifest = Join-Path $ProjectRoot ".config\dotnet-tools.json"
    if (Test-Path -LiteralPath $manifest) {
        & dotnet tool restore --tool-manifest $manifest
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet tool restore failed."
        }

        return [pscustomobject]@{
            File = "dotnet"
            PrefixArgs = @("tool", "run", "vpk", "--")
        }
    }

    throw "Could not find vpk. Install it with 'dotnet tool install -g vpk', or add it to a local dotnet tool manifest."
}

if (-not (Test-Path -LiteralPath $ProjectFile)) {
    throw "Project file not found: $ProjectFile"
}

if (-not (Test-InProjectRoot $PublishDir)) {
    throw "Refusing to clean a publish directory outside the project root: $PublishDir"
}

if (Test-Path -LiteralPath $PublishDir) {
    Remove-Item -LiteralPath $PublishDir -Recurse -Force
}

New-Item -ItemType Directory -Path $PublishDir -Force | Out-Null
New-Item -ItemType Directory -Path $ReleasesDir -Force | Out-Null

Write-Host "Publishing $AppName $Version for $Runtime..."
& dotnet publish $ProjectFile `
    -c Release `
    -r $Runtime `
    --self-contained true `
    -o $PublishDir `
    /p:Version=$Version

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed."
}

$vpk = Get-VpkCommand
$packArgs = @(
    "pack",
    "--packId", $AppName,
    "--packTitle", $AppName,
    "--packVersion", $Version,
    "--packDir", $PublishDir,
    "--mainExe", "$AppName.exe",
    "--runtime", $Runtime,
    "--channel", $Runtime,
    "--outputDir", $ReleasesDir
)

if (Test-Path -LiteralPath $IconPath) {
    $packArgs += @("--icon", $IconPath)
}

Write-Host "Packing Velopack release..."
$vpkFile = $vpk.File
$vpkPrefixArgs = $vpk.PrefixArgs
Push-Location $ProjectRoot
try {
    & $vpkFile @($vpkPrefixArgs + $packArgs)
}
finally {
    Pop-Location
}

if ($LASTEXITCODE -ne 0) {
    throw "vpk pack failed."
}

Write-Host "Release files written to $ReleasesDir"
