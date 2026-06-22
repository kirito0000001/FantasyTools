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
        "Assets\DefaultCardFace.png"
    )

    foreach ($relativePath in $requiredPaths) {
        $fullPath = Join-Path $ProgramDir $relativePath
        if (!(Test-Path -LiteralPath $fullPath)) {
            throw "Required package file was not found: $fullPath"
        }
    }
}

$repoRoot = $PSScriptRoot
$projectPath = Join-Path $repoRoot "FantasyTools.csproj"
if (!(Test-Path -LiteralPath $projectPath)) {
    throw "Project file not found: $projectPath"
}

[xml]$projectXml = Get-Content -LiteralPath $projectPath -Raw -Encoding UTF8
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
Get-ChildItem -LiteralPath $programDir -Recurse -Include *.pdb,*.xml | Remove-Item -Force
Copy-WinUiCompiledResources `
    -BuildOutputDir $workBuildOutputDir `
    -DestinationDir $programDir `
    -AppExeBaseName $assemblyName

$appExe = Join-Path $programDir $appExeName
Assert-RequiredPackagePaths -ProgramDir $programDir -AssemblyName $assemblyName
New-AppShortcut `
    -ShortcutPath $shortcutPath `
    -TargetPath $appExe `
    -WorkingDirectory $programDir `
    -Description $appDisplayName

if (!$KeepWorkFolder) {
    Remove-DirectoryIfExists -Path $workRoot
}

Write-Host ""
Write-Host "Package folder: $packageRoot"
Write-Host "Version:        $appVersion"
Write-Host "Runtime:        $Runtime"
Write-Host "Program folder: $appDisplayName"
Write-Host "Shortcut:       $appDisplayName.lnk"
if ($KeepWorkFolder) {
    Write-Host "Work folder:    $workRoot"
}
