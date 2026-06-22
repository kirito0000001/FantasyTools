param(
    [Parameter(Mandatory = $true)][string]$AppProcessId,
    [Parameter(Mandatory = $true)][string]$InstallDir,
    [Parameter(Mandatory = $true)][string]$PackagePath,
    [Parameter(Mandatory = $true)][string]$ExpectedSha256,
    [Parameter(Mandatory = $true)][string]$ExeRelativePath,
    [Parameter(Mandatory = $true)][string]$ToolboxStableKey,
    [Parameter(Mandatory = $true)][string]$TargetVersion
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
        [string]$DestinationDir
    )

    Get-ChildItem -LiteralPath $SourceDir -Force | ForEach-Object {
        $target = Join-Path $DestinationDir $_.Name
        if ($_.PSIsContainer) {
            Copy-Item -LiteralPath $_.FullName -Destination $target -Recurse -Force
        }
        else {
            Copy-Item -LiteralPath $_.FullName -Destination $target -Force
        }
    }
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
    Write-Host ""

    Write-Step 5 "等待主程序退出..."
    $processId = [int]$AppProcessId
    $process = Get-Process -Id $processId -ErrorAction SilentlyContinue
    if ($null -ne $process) {
        $process.WaitForExit(30000)
    }

    if ($null -ne (Get-Process -Id $processId -ErrorAction SilentlyContinue)) {
        throw "主程序未能在 30 秒内退出。"
    }

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

    $manifestPath = Get-ChildItem -LiteralPath $stagingRoot -Recurse -File -Filter "update-package.json" |
        Select-Object -First 1
    if ($null -eq $manifestPath) {
        throw "更新包缺少 update-package.json。"
    }

    Assert-Inside -BasePath $stagingRoot -ChildPath $manifestPath.FullName
    $manifest = Get-Content -LiteralPath $manifestPath.FullName -Raw | ConvertFrom-Json
    if ($manifest.toolboxStableKey -ne $ToolboxStableKey) {
        throw "工具箱标识不匹配：$($manifest.toolboxStableKey)"
    }

    if ($manifest.version -ne $TargetVersion) {
        throw "版本不匹配：$($manifest.version)"
    }

    $sourceRoot = Split-Path -Parent $manifestPath.FullName
    $entryExe = Join-Path $sourceRoot $manifest.entryExe
    if (!(Test-Path -LiteralPath $entryExe -PathType Leaf)) {
        throw "更新包缺少主程序：$($manifest.entryExe)"
    }

    Write-Step 60 "正在替换程序文件..."
    Copy-DirectoryContents -SourceDir $sourceRoot -DestinationDir $installFull

    Write-Step 86 "正在清理临时文件..."
    Remove-Item -LiteralPath $stagingRoot -Recurse -Force

    Write-Step 100 "更新完成。"
    $exePath = Join-Path $installFull $ExeRelativePath
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
}
finally {
    try { Stop-Transcript | Out-Null } catch {}
}
