param(
    [string]$Version = "",

    [ValidateSet("win-x64", "win-x86", "win-arm64")]
    [string]$Runtime = "win-x64",

    [string]$OutputRoot = "D:\DabaoV",

    [switch]$Prerelease
)

$ErrorActionPreference = "Stop"

$scriptsRoot = if (![string]::IsNullOrWhiteSpace($env:FANTASYTOOLS_SCRIPT_DIR)) {
    $env:FANTASYTOOLS_SCRIPT_DIR.TrimEnd('\')
}
else {
    $PSScriptRoot
}
$repoRoot = Split-Path -Parent $scriptsRoot
$projectPath = Join-Path $repoRoot "FantasyTools.csproj"
[xml]$projectXml = Get-Content -LiteralPath $projectPath -Raw -Encoding UTF8
$explicitVersion = ![string]::IsNullOrWhiteSpace($Version)
if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = ($projectXml.Project.PropertyGroup |
        ForEach-Object { $_.Version } |
        Where-Object { ![string]::IsNullOrWhiteSpace($_) } |
        Select-Object -First 1).Trim()
}

$packArguments = @{
    Configuration = "Release"
    Runtime       = $Runtime
    OutputRoot    = $OutputRoot
}
if ($explicitVersion) {
    $packArguments.Version = $Version
}

& (Join-Path $scriptsRoot "打包工具箱.ps1") @packArguments
if ($LASTEXITCODE -ne 0) {
    throw "打包工具箱.ps1 failed with exit code $LASTEXITCODE"
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
$notesPath = Join-Path $scriptsRoot "新版本介绍.txt"
if (!(Test-Path -LiteralPath $notesPath)) {
    "FantasyTools $Version" | Set-Content -LiteralPath $notesPath -Encoding UTF8
}

$notes = Get-Content -LiteralPath $notesPath -Raw -Encoding UTF8
if ([string]::IsNullOrWhiteSpace($notes)) {
    $notes = "FantasyTools $Version"
    Set-Content -LiteralPath $notesPath -Value $notes -Encoding UTF8
}

$args = @(
    "release",
    "create",
    $tag,
    $zipPath,
    $shaPath,
    $manifestPath,
    "--title",
    $title,
    "--notes-file",
    $notesPath
)
if ($Prerelease) {
    $args += "--prerelease"
}

gh @args
if ($LASTEXITCODE -ne 0) {
    throw "gh release create failed with exit code $LASTEXITCODE"
}

Write-Host "Release published: $tag"
