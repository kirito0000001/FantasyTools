param(
    [Parameter(Mandatory = $true)][string]$AppProcessId,
    [Parameter(Mandatory = $true)][string]$InstallDir,
    [Parameter(Mandatory = $true)][string]$PackagePath,
    [Parameter(Mandatory = $true)][string]$ExpectedSha256,
    [Parameter(Mandatory = $true)][string]$ExeRelativePath,
    [Parameter(Mandatory = $true)][string]$ToolboxStableKey,
    [Parameter(Mandatory = $true)][string]$TargetVersion,
    [string]$ReadySignalPath = ""
)

$ErrorActionPreference = "Stop"

function Write-Step {
    param(
        [int]$Percent,
        [string]$Message
    )

    Write-Progress -Activity "幻杀工具箱热更新" -Status $Message -PercentComplete $Percent
    Write-Host ("[{0,3}%] {1}" -f $Percent, $Message)
}

function Get-FileSha256 {
    param([string]$Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Assert-Inside {
    param(
        [string]$BasePath,
        [string]$ChildPath
    )

    $baseFull = [System.IO.Path]::GetFullPath($BasePath).TrimEnd('\') + '\'
    $childFull = [System.IO.Path]::GetFullPath($ChildPath)
    if (!$childFull.StartsWith($baseFull, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Path escapes base directory: $childFull"
    }
}

function Copy-DirectoryContents {
    param(
        [string]$SourceDir,
        [string]$DestinationDir,
        [int]$StartPercent = 60,
        [int]$EndPercent = 86
    )

    $sourceFull = [System.IO.Path]::GetFullPath($SourceDir).TrimEnd('\')
    $files = @(Get-ChildItem -LiteralPath $SourceDir -Recurse -File -Force)
    $directories = @(Get-ChildItem -LiteralPath $SourceDir -Recurse -Directory -Force)
    $total = [Math]::Max($files.Count, 1)
    $range = [Math]::Max($EndPercent - $StartPercent, 1)

    foreach ($directory in $directories) {
        $relativeDirectory = $directory.FullName.Substring($sourceFull.Length).TrimStart('\')
        $targetDirectory = Join-Path $DestinationDir $relativeDirectory
        New-Item -ItemType Directory -Force -Path $targetDirectory | Out-Null
    }

    for ($index = 0; $index -lt $files.Count; $index++) {
        $file = $files[$index]
        $relativeFile = $file.FullName.Substring($sourceFull.Length).TrimStart('\')
        $target = Join-Path $DestinationDir $relativeFile
        $targetParent = Split-Path -Parent $target
        New-Item -ItemType Directory -Force -Path $targetParent | Out-Null

        $copyPercent = $StartPercent + [int]([Math]::Floor((($index + 1) / $total) * $range))
        Write-Progress `
            -Activity "幻杀工具箱热更新" `
            -Status ("正在替换程序文件 {0}/{1}" -f ($index + 1), $files.Count) `
            -CurrentOperation $relativeFile `
            -PercentComplete $copyPercent

        if ($index -eq 0 -or $index -eq ($files.Count - 1) -or (($index + 1) % 25 -eq 0)) {
            Write-Host ("[{0,3}%] 正在替换程序文件 {1}/{2}: {3}" -f $copyPercent, ($index + 1), $files.Count, $relativeFile)
        }

        Copy-Item -LiteralPath $file.FullName -Destination $target -Force
    }
}

function Resolve-PackageSourceRoot {
    param(
        [string]$StagingRoot,
        [string]$ToolboxStableKey,
        [string]$TargetVersion,
        [string]$EntryExe
    )

    $manifestFiles = @(Get-ChildItem -LiteralPath $StagingRoot -Recurse -File -Filter "update-package.json")
    if ($manifestFiles.Count -eq 0) {
        throw "更新包缺少 update-package.json。"
    }

    foreach ($manifestFile in $manifestFiles) {
        Assert-Inside -BasePath $StagingRoot -ChildPath $manifestFile.FullName
        $manifest = Get-Content -LiteralPath $manifestFile.FullName -Raw | ConvertFrom-Json
        if ($manifest.toolboxStableKey -ne $ToolboxStableKey) {
            continue
        }

        if ($manifest.version -ne $TargetVersion) {
            continue
        }

        $sourceRoot = Split-Path -Parent $manifestFile.FullName
        $entryRelativePath = if ([string]::IsNullOrWhiteSpace($manifest.entryExe)) { $EntryExe } else { [string]$manifest.entryExe }
        $entryExe = Join-Path $sourceRoot $entryRelativePath
        if (Test-Path -LiteralPath $entryExe -PathType Leaf) {
            return @{
                SourceRoot = $sourceRoot
                Manifest = $manifest
            }
        }
    }

    $allManifests = $manifestFiles | ForEach-Object { $_.FullName } | Out-String
    throw "更新包内没有找到匹配 $ToolboxStableKey / $TargetVersion 且包含主程序 $EntryExe 的程序目录。候选清单：$allManifests"
}

function Write-ReadySignal {
    param(
        [string]$Path,
        [string]$Message
    )

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return
    }

    $signalFull = [System.IO.Path]::GetFullPath($Path)
    $signalParent = Split-Path -Parent $signalFull
    New-Item -ItemType Directory -Force -Path $signalParent | Out-Null
    @(
        "READY"
        "time=$((Get-Date).ToString("o"))"
        "message=$Message"
    ) | Set-Content -LiteralPath $signalFull -Encoding UTF8
}

function Update-AppShortcut {
    param(
        [string]$InstallDir,
        [string]$ExePath
    )

    $parentDir = Split-Path -Parent $InstallDir
    $appName = [System.IO.Path]::GetFileNameWithoutExtension($ExePath)
    $shortcutPath = Join-Path $parentDir "$appName.lnk"
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $ExePath
    $shortcut.WorkingDirectory = $InstallDir
    $shortcut.IconLocation = "$ExePath,0"
    $shortcut.Description = $appName
    $shortcut.Save()
    Write-Host "已修复快捷方式：$shortcutPath"
}

try {
    $installFull = [System.IO.Path]::GetFullPath($InstallDir)
    $packageFull = [System.IO.Path]::GetFullPath($PackagePath)
    if (!(Test-Path -LiteralPath $installFull -PathType Container)) {
        throw "InstallDir does not exist: $installFull"
    }

    if (!(Test-Path -LiteralPath $packageFull -PathType Leaf)) {
        throw "PackagePath does not exist: $packageFull"
    }

    if ($ExeRelativePath.Contains("..")) {
        throw "ExeRelativePath cannot contain '..'."
    }

    $logRoot = Join-Path $env:LOCALAPPDATA (Join-Path $ToolboxStableKey "UpdateLogs")
    New-Item -ItemType Directory -Force -Path $logRoot | Out-Null
    $logPath = Join-Path $logRoot ("Update-{0:yyyyMMdd-HHmmss}.log" -f (Get-Date))
    Start-Transcript -LiteralPath $logPath | Out-Null

    Write-Host "幻杀工具箱热更新"
    Write-Host "目标版本：$TargetVersion"
    Write-Host "程序目录：$installFull"
    Write-Host "更新包：$packageFull"
    if (![string]::IsNullOrWhiteSpace($ReadySignalPath)) {
        Write-Host "就绪信号：$ReadySignalPath"
    }
    Write-Host ""

    Write-Step 8 "正在进行热更新预检..."
    Write-Step 18 "校验更新包 SHA-256..."
    $actualSha256 = Get-FileSha256 -Path $packageFull
    if ($actualSha256 -ne $ExpectedSha256.ToLowerInvariant()) {
        throw "SHA-256 mismatch: $actualSha256"
    }

    $stagingRoot = Join-Path $env:LOCALAPPDATA (Join-Path $ToolboxStableKey "UpdateStaging")
    if (Test-Path -LiteralPath $stagingRoot) {
        Remove-Item -LiteralPath $stagingRoot -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $stagingRoot | Out-Null

    Write-Step 34 "正在解压更新包..."
    Expand-Archive -LiteralPath $packageFull -DestinationPath $stagingRoot -Force

    $packageSource = Resolve-PackageSourceRoot `
        -StagingRoot $stagingRoot `
        -ToolboxStableKey $ToolboxStableKey `
        -TargetVersion $TargetVersion `
        -EntryExe $ExeRelativePath
    $sourceRoot = [string]$packageSource.SourceRoot
    $manifest = $packageSource.Manifest
    if ($manifest.toolboxStableKey -ne $ToolboxStableKey) {
        throw "工具箱标识不匹配：$($manifest.toolboxStableKey)"
    }

    if ($manifest.version -ne $TargetVersion) {
        throw "版本不匹配：$($manifest.version)"
    }

    $entryRelativePath = if ([string]::IsNullOrWhiteSpace($manifest.entryExe)) { $ExeRelativePath } else { [string]$manifest.entryExe }
    $entryExe = Join-Path $sourceRoot $entryRelativePath
    if (!(Test-Path -LiteralPath $entryExe -PathType Leaf)) {
        throw "更新包缺少主程序：$entryRelativePath"
    }

    Write-Host "覆盖源目录：$sourceRoot"
    Write-ReadySignal -Path $ReadySignalPath -Message "precheck-complete"

    Write-Step 55 "等待主程序退出..."
    $processId = [int]$AppProcessId
    $process = Get-Process -Id $processId -ErrorAction SilentlyContinue
    if ($null -ne $process) {
        $process.WaitForExit(30000)
    }

    if ($null -ne (Get-Process -Id $processId -ErrorAction SilentlyContinue)) {
        throw "主程序未能在 30 秒内退出。"
    }

    Write-Step 60 "正在替换程序文件..."
    Copy-DirectoryContents -SourceDir $sourceRoot -DestinationDir $installFull -StartPercent 60 -EndPercent 86

    Write-Step 86 "正在清理临时文件..."
    Remove-Item -LiteralPath $stagingRoot -Recurse -Force

    Write-Step 100 "更新完成。"
    $exePath = Join-Path $installFull $ExeRelativePath
    try {
        Update-AppShortcut -InstallDir $installFull -ExePath $exePath
    }
    catch {
        Write-Host "快捷方式修复失败：$($_.Exception.Message)" -ForegroundColor Yellow
    }

    Write-Host ""
    Write-Host "更新完成：$TargetVersion"
    Write-Host "按 Enter 打开新版本。"
    [void][System.Console]::ReadLine()
    Start-Process -FilePath $exePath -WorkingDirectory $installFull
}
catch {
    Write-Host ""
    Write-Host "更新失败：$($_.Exception.Message)" -ForegroundColor Red
    Write-Host "请保留此窗口内容，或手动下载 Release 包覆盖程序目录。"
    Write-Host "按 Enter 关闭。"
    try { [void][System.Console]::ReadLine() } catch {}
    exit 1
}
finally {
    try { Stop-Transcript | Out-Null } catch {}
}
