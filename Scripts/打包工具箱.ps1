param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [ValidateSet("win-x64", "win-x86", "win-arm64")]
    [string]$Runtime = "win-x64",

    [string]$OutputRoot = "D:\DabaoV",

    [string]$Version = "",

    [switch]$Clean,

    [switch]$KeepWorkFolder
)

$ErrorActionPreference = "Stop"

function Get-PlatformFromRuntime {
    param([string]$RuntimeIdentifier)

    switch ($RuntimeIdentifier) {
        "win-x64" { return "x64" }
        "win-x86" { return "x86" }
        "win-arm64" { return "ARM64" }
        default { throw "Unsupported runtime: $RuntimeIdentifier" }
    }
}

function Get-ProjectProperty {
    param(
        [xml]$ProjectXml,
        [string]$Name
    )

    $node = $ProjectXml.Project.PropertyGroup |
        ForEach-Object { $_.$Name } |
        Where-Object { ![string]::IsNullOrWhiteSpace($_) } |
        Select-Object -First 1

    if ([string]::IsNullOrWhiteSpace($node)) {
        throw "Project property '$Name' was not found."
    }

    return $node.Trim()
}

function Get-AppVersion {
    param(
        [string]$ExplicitVersion,
        [xml]$ProjectXml
    )

    if (![string]::IsNullOrWhiteSpace($ExplicitVersion)) {
        return $ExplicitVersion.Trim()
    }

    return Get-ProjectProperty -ProjectXml $ProjectXml -Name "Version"
}

function ConvertTo-VersionParts {
    param([string]$VersionText)

    $match = [regex]::Match($VersionText.Trim(), '^v?(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)(?:\.(?<build>\d+))?(?:-(?<label>[0-9A-Za-z.-]+))?$')
    if (!$match.Success) {
        throw "版本号格式无效：$VersionText。请使用 1.0.1 或 1.0.1-beta.1。"
    }

    $label = $match.Groups["label"].Value
    $labelNumber = 0
    if (![string]::IsNullOrWhiteSpace($label)) {
        $labelNumberMatch = [regex]::Match($label, '(\d+)$')
        if ($labelNumberMatch.Success) {
            $labelNumber = [int]$labelNumberMatch.Value
        }
    }

    return [pscustomobject]@{
        Text        = $VersionText.Trim().TrimStart('v', 'V')
        Major       = [int]$match.Groups["major"].Value
        Minor       = [int]$match.Groups["minor"].Value
        Patch       = [int]$match.Groups["patch"].Value
        Build       = $(if ($match.Groups["build"].Success) { [int]$match.Groups["build"].Value } else { 0 })
        Label       = $label
        LabelNumber = $labelNumber
        IsPrerelease = ![string]::IsNullOrWhiteSpace($label)
    }
}

function Compare-ToolboxVersion {
    param(
        [string]$Left,
        [string]$Right
    )

    $leftParts = ConvertTo-VersionParts -VersionText $Left
    $rightParts = ConvertTo-VersionParts -VersionText $Right
    foreach ($name in @("Major", "Minor", "Patch", "Build")) {
        if ($leftParts.$name -gt $rightParts.$name) { return 1 }
        if ($leftParts.$name -lt $rightParts.$name) { return -1 }
    }

    if ($leftParts.IsPrerelease -and !$rightParts.IsPrerelease) { return -1 }
    if (!$leftParts.IsPrerelease -and $rightParts.IsPrerelease) { return 1 }
    if ($leftParts.IsPrerelease -and $rightParts.IsPrerelease) {
        $labelCompare = [string]::Compare($leftParts.Label, $rightParts.Label, [System.StringComparison]::OrdinalIgnoreCase)
        if ($labelCompare -ne 0) { return $labelCompare }
        if ($leftParts.LabelNumber -gt $rightParts.LabelNumber) { return 1 }
        if ($leftParts.LabelNumber -lt $rightParts.LabelNumber) { return -1 }
    }

    return 0
}

function Get-AssemblyCompatibleVersion {
    param([string]$VersionText)

    $parts = ConvertTo-VersionParts -VersionText $VersionText
    return "{0}.{1}.{2}.{3}" -f $parts.Major, $parts.Minor, $parts.Patch, $parts.Build
}

function Set-ProjectVersion {
    param(
        [string]$ProjectPath,
        [xml]$ProjectXml,
        [string]$NewVersion
    )

    $currentVersion = Get-ProjectProperty -ProjectXml $ProjectXml -Name "Version"
    if ((Compare-ToolboxVersion -Left $NewVersion -Right $currentVersion) -le 0) {
        throw "新版本号必须大于当前版本。当前版本：$currentVersion；输入版本：$NewVersion"
    }

    $assemblyVersion = Get-AssemblyCompatibleVersion -VersionText $NewVersion
    $content = Get-Content -LiteralPath $ProjectPath -Raw -Encoding UTF8
    $content = [regex]::Replace($content, '<Version>[^<]+</Version>', "<Version>$NewVersion</Version>", 1)
    $content = [regex]::Replace($content, '<AssemblyVersion>[^<]+</AssemblyVersion>', "<AssemblyVersion>$assemblyVersion</AssemblyVersion>", 1)
    $content = [regex]::Replace($content, '<FileVersion>[^<]+</FileVersion>', "<FileVersion>$assemblyVersion</FileVersion>", 1)
    $content = [regex]::Replace($content, '<InformationalVersion>[^<]+</InformationalVersion>', "<InformationalVersion>$NewVersion</InformationalVersion>", 1)
    [System.IO.File]::WriteAllText(
        $ProjectPath,
        $content,
        [System.Text.UTF8Encoding]::new($false))

    Write-Host "==> Version updated: $currentVersion -> $NewVersion"
}

function Remove-DirectoryIfExists {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        throw "Refusing to remove an empty path."
    }

    $resolved = Resolve-Path -LiteralPath $Path -ErrorAction SilentlyContinue
    if ($null -ne $resolved) {
        Remove-Item -LiteralPath $resolved.Path -Recurse -Force
    }
}

function Copy-SourceToWorkFolder {
    param(
        [string]$SourceRoot,
        [string]$DestinationRoot
    )

    $excludeDirectories = @(
        ".git",
        ".vs",
        ".idea",
        "bin",
        "obj",
        "publish",
        "artifacts",
        "AppPackages"
    )

    New-Item -ItemType Directory -Force -Path $DestinationRoot | Out-Null

    $sourceRootFull = (Resolve-Path -LiteralPath $SourceRoot).Path
    Get-ChildItem -LiteralPath $sourceRootFull -Force | ForEach-Object {
        if ($_.PSIsContainer -and ($excludeDirectories -contains $_.Name)) {
            return
        }

        $destination = Join-Path $DestinationRoot $_.Name
        Copy-Item -LiteralPath $_.FullName -Destination $destination -Recurse -Force
    }
}

function Copy-WinUiCompiledResources {
    param(
        [string]$BuildOutputDir,
        [string]$DestinationDir,
        [string]$AppExeBaseName
    )

    $resourceNames = @(
        "$AppExeBaseName.pri",
        "App.xbf",
        "MainWindow.xbf",
        "Styles\ToolboxStyles.xbf"
    )

    foreach ($resourceName in $resourceNames) {
        $sourcePath = Join-Path $BuildOutputDir $resourceName
        if (!(Test-Path -LiteralPath $sourcePath)) {
            throw "Required WinUI resource was not found: $sourcePath"
        }

        $destinationPath = Join-Path $DestinationDir $resourceName
        $destinationParent = Split-Path -Parent $destinationPath
        New-Item -ItemType Directory -Force -Path $destinationParent | Out-Null
        Copy-Item -LiteralPath $sourcePath -Destination $destinationPath -Force
    }
}

function New-AppShortcut {
    param(
        [string]$ShortcutPath,
        [string]$TargetPath,
        [string]$WorkingDirectory,
        [string]$Description
    )

    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($ShortcutPath)
    $shortcut.TargetPath = $TargetPath
    $shortcut.WorkingDirectory = $WorkingDirectory
    $shortcut.IconLocation = "$TargetPath,0"
    $shortcut.Description = $Description
    $shortcut.Save()
}

function Assert-RequiredPackagePaths {
    param(
        [string]$ProgramDir,
        [string]$AssemblyName
    )

    $requiredPaths = @(
        "$AssemblyName.exe",
        "$AssemblyName.pri",
        "App.xbf",
        "MainWindow.xbf",
        "Styles\ToolboxStyles.xbf",
        "Assets\AppIcon.png",
        "Assets\AppIcon.ico",
        "Assets\StoreLogo.png",
        "Assets\DefaultCardFace.png",
        "Scripts\热更新覆盖.ps1"
    )

    foreach ($relativePath in $requiredPaths) {
        $fullPath = Join-Path $ProgramDir $relativePath
        if (!(Test-Path -LiteralPath $fullPath)) {
            throw "Required package file was not found: $fullPath"
        }
    }
}

function New-UpdatePackageManifest {
    param(
        [string]$ProgramDir,
        [string]$Version,
        [string]$Runtime,
        [string]$EntryExe
    )

    $manifest = [ordered]@{
        schemaVersion    = 1
        toolboxStableKey = "FantasyTools"
        version          = $Version
        runtime          = $Runtime
        entryExe         = $EntryExe
        createdAt        = (Get-Date).ToString("o")
    }
    $manifestPath = Join-Path $ProgramDir "update-package.json"
    $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
}

function New-ReleaseAssets {
    param(
        [string]$PackageRoot,
        [string]$ProgramDir,
        [string]$OutputRoot,
        [string]$AppDisplayName,
        [string]$Version,
        [string]$Runtime
    )

    $releaseAssetRoot = Join-Path $OutputRoot "ReleaseAssets"
    New-Item -ItemType Directory -Force -Path $releaseAssetRoot | Out-Null

    $zipName = "FantasyTools-v$Version-$Runtime.zip"
    $zipPath = Join-Path $releaseAssetRoot $zipName
    $shaPath = Join-Path $releaseAssetRoot "FantasyTools-v$Version-$Runtime.sha256.txt"
    $manifestPath = Join-Path $releaseAssetRoot "toolbox-update.json"

    if (Test-Path -LiteralPath $zipPath) {
        Remove-Item -LiteralPath $zipPath -Force
    }

    Compress-Archive -LiteralPath $ProgramDir -DestinationPath $zipPath -Force
    $sha = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    "$sha  $zipName" | Set-Content -LiteralPath $shaPath -Encoding ASCII
    $zipSize = (Get-Item -LiteralPath $zipPath).Length

    $manifest = [ordered]@{
        schemaVersion           = 1
        toolboxStableKey        = "FantasyTools"
        displayName             = $AppDisplayName
        version                 = $Version
        channel                 = "stable"
        publishedAt             = (Get-Date).ToString("o")
        minSupportedVersion     = "1.0.0"
        releaseNotesUrl         = "https://github.com/kirito0000001/FantasyTools/releases/tag/v$Version"
        requiresManualMigration = $false
        requiresRestart         = $true
        assets                  = @(
            [ordered]@{
                runtime   = $Runtime
                fileName  = $zipName
                sha256    = $sha
                sizeBytes = $zipSize
            }
        )
    }
    $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

    return [pscustomobject]@{
        ZipPath      = $zipPath
        ShaPath      = $shaPath
        ManifestPath = $manifestPath
    }
}

$scriptsRoot = if (![string]::IsNullOrWhiteSpace($env:FANTASYTOOLS_SCRIPT_DIR)) {
    $env:FANTASYTOOLS_SCRIPT_DIR.TrimEnd('\')
}
else {
    $PSScriptRoot
}
$repoRoot = Split-Path -Parent $scriptsRoot
$projectPath = Join-Path $repoRoot "FantasyTools.csproj"
if (!(Test-Path -LiteralPath $projectPath)) {
    throw "Project file not found: $projectPath"
}

[xml]$projectXml = Get-Content -LiteralPath $projectPath -Raw -Encoding UTF8
if (![string]::IsNullOrWhiteSpace($Version)) {
    Set-ProjectVersion -ProjectPath $projectPath -ProjectXml $projectXml -NewVersion $Version.Trim()
    [xml]$projectXml = Get-Content -LiteralPath $projectPath -Raw -Encoding UTF8
}
$platform = Get-PlatformFromRuntime -RuntimeIdentifier $Runtime
$targetFramework = Get-ProjectProperty -ProjectXml $projectXml -Name "TargetFramework"
$appDisplayName = Get-ProjectProperty -ProjectXml $projectXml -Name "Product"
$assemblyName = Get-ProjectProperty -ProjectXml $projectXml -Name "AssemblyName"
$appVersion = Get-AppVersion -ExplicitVersion $Version -ProjectXml $projectXml
$packageBaseName = "$appDisplayName" + "V" + $appVersion

$workRoot = Join-Path $env:TEMP "FantasyTools-Pakout"
$workSourceRoot = Join-Path $workRoot "source"
$publishDir = Join-Path $workRoot "publish"
$workProjectPath = Join-Path $workSourceRoot "FantasyTools.csproj"
$workBuildOutputDir = Join-Path $workSourceRoot (Join-Path "bin" (Join-Path $platform (Join-Path $Configuration (Join-Path $targetFramework $Runtime))))
$packageRoot = Join-Path $OutputRoot $packageBaseName
$programDir = Join-Path $packageRoot $appDisplayName
$shortcutPath = Join-Path $packageRoot "$appDisplayName.lnk"

if ($Clean) {
    Remove-DirectoryIfExists -Path (Join-Path $repoRoot "bin")
    Remove-DirectoryIfExists -Path (Join-Path $repoRoot "obj")
}

New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null
Remove-DirectoryIfExists -Path $workRoot
Remove-DirectoryIfExists -Path $packageRoot

Write-Host "==> Copying clean source"
Copy-SourceToWorkFolder -SourceRoot $repoRoot -DestinationRoot $workSourceRoot

Write-Host "==> Restoring packages"
dotnet restore $workProjectPath
if ($LASTEXITCODE -ne 0) {
    throw "dotnet restore failed with exit code $LASTEXITCODE"
}

Write-Host "==> Publishing $Configuration / $Runtime"
dotnet publish $workProjectPath `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained true `
    --output $publishDir `
    -p:Platform=$platform `
    -p:WindowsPackageType=None `
    -p:WindowsAppSDKSelfContained=true `
    -p:PublishSingleFile=false `
    -p:PublishTrimmed=false `
    -p:PublishReadyToRun=true `
    -p:DebugType=None `
    -p:DebugSymbols=false

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$appExeName = "$assemblyName.exe"
$publishedExe = Join-Path $publishDir $appExeName
if (!(Test-Path -LiteralPath $publishedExe)) {
    throw "Publish completed, but the app exe was not found: $publishedExe"
}

Write-Host "==> Building release layout"
New-Item -ItemType Directory -Force -Path $programDir | Out-Null
Copy-Item -Path (Join-Path $publishDir "*") -Destination $programDir -Recurse -Force
Get-ChildItem -LiteralPath $programDir -Recurse -File |
    Where-Object { $_.Extension -in @(".pdb", ".xml") } |
    ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force }
Copy-WinUiCompiledResources `
    -BuildOutputDir $workBuildOutputDir `
    -DestinationDir $programDir `
    -AppExeBaseName $assemblyName

$appExe = Join-Path $programDir $appExeName
New-UpdatePackageManifest -ProgramDir $programDir -Version $appVersion -Runtime $Runtime -EntryExe $appExeName
Assert-RequiredPackagePaths -ProgramDir $programDir -AssemblyName $assemblyName
New-AppShortcut `
    -ShortcutPath $shortcutPath `
    -TargetPath $appExe `
    -WorkingDirectory $programDir `
    -Description $appDisplayName

Write-Host "==> Building release assets"
$releaseAssets = New-ReleaseAssets `
    -PackageRoot $packageRoot `
    -ProgramDir $programDir `
    -OutputRoot $OutputRoot `
    -AppDisplayName $appDisplayName `
    -Version $appVersion `
    -Runtime $Runtime

if (!$KeepWorkFolder) {
    Remove-DirectoryIfExists -Path $workRoot
}

Write-Host ""
Write-Host "Package folder: $packageRoot"
Write-Host "Version:        $appVersion"
Write-Host "Runtime:        $Runtime"
Write-Host "Program folder: $appDisplayName"
Write-Host "Shortcut:       $appDisplayName.lnk"
Write-Host "Release zip:    $($releaseAssets.ZipPath)"
Write-Host "Release sha256: $($releaseAssets.ShaPath)"
Write-Host "Release json:   $($releaseAssets.ManifestPath)"
if ($KeepWorkFolder) {
    Write-Host "Work folder:    $workRoot"
}
