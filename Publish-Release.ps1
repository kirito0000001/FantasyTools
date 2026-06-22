param(
    [string]$Version = "",

    [ValidateSet("win-x64", "win-x86", "win-arm64")]
    [string]$Runtime = "win-x64",

    [string]$OutputRoot = "D:\DabaoV",

    [switch]$Prerelease
)

$ErrorActionPreference = "Stop"

$repoRoot = $PSScriptRoot
$projectPath = Join-Path $repoRoot "FantasyTools.csproj"
[xml]$projectXml = Get-Content -LiteralPath $projectPath -Raw -Encoding UTF8
if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = ($projectXml.Project.PropertyGroup |
        ForEach-Object { $_.Version } |
        Where-Object { ![string]::IsNullOrWhiteSpace($_) } |
        Select-Object -First 1).Trim()
}

& (Join-Path $repoRoot "Pakout.ps1") -Configuration Release -Runtime $Runtime -OutputRoot $OutputRoot -Version $Version
if ($LASTEXITCODE -ne 0) {
    throw "Pakout.ps1 failed with exit code $LASTEXITCODE"
}

$assetRoot = Join-Path $OutputRoot "ReleaseAssets"
$zipPath = Join-Path $assetRoot "FantasyTools-v$Version-$Runtime.zip"
$shaPath = Join-Path $assetRoot "FantasyTools-v$Version-$Runtime.sha256.txt"
$manifestPath = Join-Path $assetRoot "toolbox-update.json"

foreach ($path in @($zipPath, $shaPath, $manifestPath)) {
    if (!(Test-Path -LiteralPath $path)) {
        throw "Release asset missing: $path"
    }
}

$gh = Get-Command gh -ErrorAction SilentlyContinue
if ($null -eq $gh) {
    Write-Host "GitHub CLI was not found. Release assets are ready:"
    Write-Host "  $zipPath"
    Write-Host "  $shaPath"
    Write-Host "  $manifestPath"
    Write-Host "Create a GitHub Release manually and upload these three files."
    exit 0
}

$tag = "v$Version"
$title = "FantasyTools $Version"
$notes = "FantasyTools $Version"
$args = @(
    "release",
    "create",
    $tag,
    $zipPath,
    $shaPath,
    $manifestPath,
    "--title",
    $title,
    "--notes",
    $notes
)
if ($Prerelease) {
    $args += "--prerelease"
}

gh @args
if ($LASTEXITCODE -ne 0) {
    throw "gh release create failed with exit code $LASTEXITCODE"
}

Write-Host "Release published: $tag"
