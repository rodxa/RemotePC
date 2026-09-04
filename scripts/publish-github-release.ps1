[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+([\-+][0-9A-Za-z\.-]+)?$')]
    [string]$Version,

    [string]$Repository = $(if ($env:REMOTEPC_GITHUB_REPOSITORY) { $env:REMOTEPC_GITHUB_REPOSITORY } else { "rodxa/RemotePC" })
)

$ErrorActionPreference = "Stop"

$AppName = "RemotePC"
$ProjectRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$ReleasesDir = Join-Path $ProjectRoot "Releases"
$Tag = "v$Version"

function Get-RepositoryParts {
    param([Parameter(Mandatory = $true)][string]$RepositoryName)

    $repo = $RepositoryName.Trim().TrimEnd("/")
    $repo = $repo -replace "^https://github\.com/", ""
    $repo = $repo -replace "\.git$", ""
    $parts = $repo.Split("/", [System.StringSplitOptions]::RemoveEmptyEntries)

    if ($parts.Length -ne 2) {
        throw "Repository must be OWNER/REPO or https://github.com/OWNER/REPO."
    }

    return [pscustomobject]@{
        Owner = $parts[0]
        Name = $parts[1]
    }
}

function Get-ContentType {
    param([Parameter(Mandatory = $true)][string]$Path)

    switch ([System.IO.Path]::GetExtension($Path).ToLowerInvariant()) {
        ".json" { return "application/json" }
        ".zip" { return "application/zip" }
        ".exe" { return "application/octet-stream" }
        ".nupkg" { return "application/octet-stream" }
        default { return "application/octet-stream" }
    }
}

function Invoke-GitHubJson {
    param(
        [Parameter(Mandatory = $true)][string]$Method,
        [Parameter(Mandatory = $true)][string]$Uri,
        [object]$Body = $null
    )

    $headers = @{
        Accept = "application/vnd.github+json"
        Authorization = "Bearer $env:GITHUB_TOKEN"
        "X-GitHub-Api-Version" = "2022-11-28"
        "User-Agent" = "$AppName-release-script"
    }

    $args = @{
        Method = $Method
        Uri = $Uri
        Headers = $headers
        ErrorAction = "Stop"
    }

    if ($null -ne $Body) {
        $args.Body = $Body | ConvertTo-Json -Depth 10
        $args.ContentType = "application/json"
    }

    Invoke-RestMethod @args
}

function Publish-WithGitHubCli {
    param(
        [Parameter(Mandatory = $true)][string]$RepositoryName,
        [Parameter(Mandatory = $true)][string]$ReleaseTag,
        [Parameter(Mandatory = $true)][object[]]$ReleaseFiles
    )

    & gh release view $ReleaseTag --repo $RepositoryName *> $null
    $releaseExists = $LASTEXITCODE -eq 0

    if (-not $releaseExists) {
        $createArgs = @(
            "release", "create", $ReleaseTag,
            "--repo", $RepositoryName,
            "--title", $ReleaseTag,
            "--notes", "Velopack release $Version"
        )

        if ($Version.Contains("-")) {
            $createArgs += "--prerelease"
        }

        Write-Host "Creating GitHub release $ReleaseTag in $RepositoryName..."
        & gh @createArgs
        if ($LASTEXITCODE -ne 0) {
            throw "gh release create failed."
        }
    }
    else {
        Write-Host "Reusing existing GitHub release $ReleaseTag in $RepositoryName..."
    }

    $uploadArgs = @(
        "release", "upload", $ReleaseTag,
        "--repo", $RepositoryName,
        "--clobber"
    )
    $uploadArgs += $ReleaseFiles.FullName

    Write-Host "Uploading $($ReleaseFiles.Count) Velopack file(s)..."
    & gh @uploadArgs

    if ($LASTEXITCODE -ne 0) {
        throw "gh release upload failed."
    }
}

function Publish-WithGitHubRestApi {
    param(
        [Parameter(Mandatory = $true)][string]$RepositoryName,
        [Parameter(Mandatory = $true)][string]$ReleaseTag,
        [Parameter(Mandatory = $true)][object[]]$ReleaseFiles
    )

    if ([string]::IsNullOrWhiteSpace($env:GITHUB_TOKEN)) {
        throw "GitHub CLI was not found. Install gh, manually upload the files in GitHub, or set GITHUB_TOKEN for this publish script."
    }

    $repoParts = Get-RepositoryParts $RepositoryName
    $apiBase = "https://api.github.com/repos/$($repoParts.Owner)/$($repoParts.Name)"

    try {
        $release = Invoke-GitHubJson `
            -Method "Get" `
            -Uri "$apiBase/releases/tags/$ReleaseTag"
        Write-Host "Reusing existing GitHub release $ReleaseTag in $RepositoryName..."
    }
    catch {
        $statusCode = $_.Exception.Response.StatusCode.value__
        if ($statusCode -ne 404) {
            throw
        }

        Write-Host "Creating GitHub release $ReleaseTag in $RepositoryName..."
        $release = Invoke-GitHubJson `
            -Method "Post" `
            -Uri "$apiBase/releases" `
            -Body @{
                tag_name = $ReleaseTag
                name = $ReleaseTag
                body = "Velopack release $Version"
                prerelease = $Version.Contains("-")
            }
    }

    $assets = Invoke-GitHubJson -Method "Get" -Uri $release.assets_url
    $headers = @{
        Accept = "application/vnd.github+json"
        Authorization = "Bearer $env:GITHUB_TOKEN"
        "X-GitHub-Api-Version" = "2022-11-28"
        "User-Agent" = "$AppName-release-script"
    }

    Write-Host "Uploading $($ReleaseFiles.Count) Velopack file(s)..."
    foreach ($file in $ReleaseFiles) {
        $existingAsset = $assets | Where-Object { $_.name -eq $file.Name } | Select-Object -First 1
        if ($existingAsset) {
            Invoke-GitHubJson -Method "Delete" -Uri "$apiBase/releases/assets/$($existingAsset.id)" | Out-Null
        }

        $assetName = [System.Uri]::EscapeDataString($file.Name)
        $uploadUrl = ($release.upload_url -replace "\{\?name,label\}", "") + "?name=$assetName"
        Invoke-RestMethod `
            -Method "Post" `
            -Uri $uploadUrl `
            -Headers $headers `
            -ContentType (Get-ContentType $file.FullName) `
            -InFile $file.FullName `
            -ErrorAction Stop | Out-Null
    }
}

if (-not (Test-Path -LiteralPath $ReleasesDir)) {
    throw "Releases directory not found. Run .\scripts\build-release.ps1 -Version $Version first."
}

$patterns = @(
    "RELEASES-*",
    "releases.*.json",
    "assets.*.json",
    "$AppName-$Version-*.nupkg",
    "$AppName-*-Setup.exe",
    "$AppName-*-Portable.zip"
)

$files = foreach ($pattern in $patterns) {
    Get-ChildItem -LiteralPath $ReleasesDir -File -Filter $pattern -ErrorAction SilentlyContinue
}

$files = $files | Sort-Object FullName -Unique
if (-not $files) {
    throw "No Velopack release files matched in $ReleasesDir."
}

if (Get-Command "gh" -ErrorAction SilentlyContinue) {
    Publish-WithGitHubCli -RepositoryName $Repository -ReleaseTag $Tag -ReleaseFiles $files
}
else {
    Publish-WithGitHubRestApi -RepositoryName $Repository -ReleaseTag $Tag -ReleaseFiles $files
}

Write-Host "Published $Tag to $Repository."
