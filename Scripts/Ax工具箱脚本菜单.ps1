param(
    [ValidateSet("Menu", "PackStable", "PackBeta", "PublishLatest", "Config")]
    [string]$Action = "Menu",

    [string]$Version = "",

    [ValidateSet("win-x64", "win-x86", "win-arm64")]
    [string]$Runtime = "win-x64",

    [string]$OutputRoot = "D:\DabaoV",

    [string]$ReleaseAssetRoot = ""
)

$ErrorActionPreference = "Stop"

$scriptsRoot = $PSScriptRoot
$repoRoot = Split-Path -Parent $scriptsRoot
if ([string]::IsNullOrWhiteSpace($ReleaseAssetRoot)) {
    $ReleaseAssetRoot = Join-Path $repoRoot "ReleaseAssets"
}

function Get-CurrentVersion {
    $projectPath = Join-Path $repoRoot "FantasyTools.csproj"
    [xml]$projectXml = Get-Content -LiteralPath $projectPath -Raw -Encoding UTF8
    return ($projectXml.Project.PropertyGroup |
        ForEach-Object { $_.Version } |
        Where-Object { ![string]::IsNullOrWhiteSpace($_) } |
        Select-Object -First 1).Trim()
}

function Get-VersionCore {
    param([string]$VersionText)

    $match = [regex]::Match($VersionText.Trim(), '^v?(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)(?:\.(?<build>\d+))?(?:-(?<label>[0-9A-Za-z.-]+))?$')
    if (!$match.Success) {
        return $VersionText.Trim()
    }

    return "{0}.{1}.{2}" -f $match.Groups["major"].Value, $match.Groups["minor"].Value, $match.Groups["patch"].Value
}

function Get-NextStableVersion {
    param([string]$CurrentVersion)

    $match = [regex]::Match($CurrentVersion.Trim(), '^v?(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)(?:\.(?<build>\d+))?(?:-(?<label>[0-9A-Za-z.-]+))?$')
    if (!$match.Success) {
        return ""
    }

    $major = [int]$match.Groups["major"].Value
    return "$($major + 1).0.0"
}

function Get-NextBetaVersion {
    param([string]$CurrentVersion)

    $match = [regex]::Match($CurrentVersion.Trim(), '^v?(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)(?:\.(?<build>\d+))?(?:-(?<label>[0-9A-Za-z.-]+))?$')
    if (!$match.Success) {
        return ""
    }

    $major = [int]$match.Groups["major"].Value
    $minor = [int]$match.Groups["minor"].Value
    $patch = [int]$match.Groups["patch"].Value
    return "$major.$minor.$($patch + 1)"
}

function Read-Default {
    param(
        [string]$Prompt,
        [string]$DefaultValue
    )

    $value = Read-Host "$Prompt [$DefaultValue]"
    if ([string]::IsNullOrWhiteSpace($value)) {
        return $DefaultValue
    }

    return $value.Trim()
}

function Read-NewVersion {
    param(
        [string]$CurrentVersion,
        [string]$SuggestedVersion,
        [bool]$IsBeta
    )

    Write-Host ""
    Write-Host "当前版本：$CurrentVersion"
    if ($IsBeta) {
        Write-Host "请输入大于当前版本的测试版基础版本号，例如 $SuggestedVersion。"
        Write-Host "脚本会自动写入为：$SuggestedVersion-Beta，并让工具箱显示为 V$SuggestedVersion-Beta。"
        return Read-Default -Prompt "测试版基础版本号" -DefaultValue $SuggestedVersion
    }

    Write-Host "请输入大于当前版本的正式版本号，例如 $SuggestedVersion。"
    return Read-Default -Prompt "正式版版本号" -DefaultValue $SuggestedVersion
}

function Pause-Menu {
    Write-Host ""
    Write-Host "按任意键返回菜单..."
    [void][System.Console]::ReadKey($true)
}

function Invoke-Pack {
    param(
        [string]$SelectedVersion,
        [string]$SelectedRuntime,
        [string]$SelectedOutputRoot,
        [string]$SelectedReleaseAssetRoot,
        [string]$SelectedChannel
    )

    $arguments = @{
        Runtime    = $SelectedRuntime
        OutputRoot = $SelectedOutputRoot
        ReleaseAssetRoot = $SelectedReleaseAssetRoot
        Channel    = $SelectedChannel
    }
    if (![string]::IsNullOrWhiteSpace($SelectedVersion)) {
        $arguments.Version = $SelectedVersion
    }

    & (Join-Path $scriptsRoot "打包工具箱.ps1") @arguments
}

function Invoke-PublishLatest {
    param(
        [string]$SelectedOutputRoot,
        [string]$SelectedReleaseAssetRoot
    )

    & (Join-Path $scriptsRoot "发布新版本.ps1") -OutputRoot $SelectedOutputRoot -ReleaseAssetRoot $SelectedReleaseAssetRoot
}

function Get-MaskedSecret {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return "未设置"
    }

    if ($Value.Length -le 8) {
        return "已设置（长度：$($Value.Length)）"
    }

    return "{0}****{1}（长度：{2}）" -f $Value.Substring(0, 4), $Value.Substring($Value.Length - 4), $Value.Length
}

function Get-UserEnvironmentValue {
    param([string]$Name)

    return [Environment]::GetEnvironmentVariable($Name, "User")
}

function Get-EnvironmentValue {
    param([string]$Name)

    $value = [Environment]::GetEnvironmentVariable($Name, "Process")
    if ([string]::IsNullOrWhiteSpace($value)) {
        $value = [Environment]::GetEnvironmentVariable($Name, "User")
    }
    if ([string]::IsNullOrWhiteSpace($value)) {
        $value = [Environment]::GetEnvironmentVariable($Name, "Machine")
    }

    return $value
}

function Get-ScriptConfigStatus {
    $giteeToken = Get-EnvironmentValue -Name "FANTASYTOOLS_GITEE_TOKEN"
    $fallbackToken = Get-EnvironmentValue -Name "GITEE_TOKEN"
    $hasGiteeToken = ![string]::IsNullOrWhiteSpace($giteeToken) -or ![string]::IsNullOrWhiteSpace($fallbackToken)

    if ($hasGiteeToken) {
        return [pscustomobject]@{
            IsComplete = $true
            Text       = "配置：已完成"
            Detail     = "Gitee Token 已设置，可同步发布到 Gitee。"
            Color      = "Green"
        }
    }

    return [pscustomobject]@{
        IsComplete = $false
        Text       = "配置：未完成"
        Detail     = "Gitee Token 未设置，发布时会跳过 Gitee。"
        Color      = "Yellow"
    }
}

function Write-ScriptConfigStatus {
    $status = Get-ScriptConfigStatus
    Write-Host $status.Text -ForegroundColor $status.Color
    Write-Host $status.Detail -ForegroundColor $status.Color
}

function Set-UserEnvironmentValue {
    param(
        [string]$Name,
        [string]$Value
    )

    [Environment]::SetEnvironmentVariable($Name, $Value, "User")
    [Environment]::SetEnvironmentVariable($Name, $Value, "Process")
}

function Clear-UserEnvironmentValue {
    param([string]$Name)

    [Environment]::SetEnvironmentVariable($Name, $null, "User")
    [Environment]::SetEnvironmentVariable($Name, $null, "Process")
}

function Invoke-GiteeTokenSettings {
    while ($true) {
        Clear-Host
        $token = Get-UserEnvironmentValue -Name "FANTASYTOOLS_GITEE_TOKEN"
        $fallbackToken = Get-UserEnvironmentValue -Name "GITEE_TOKEN"
        Write-Host ""
        Write-Host "Ax工具箱脚本菜单 - 配置集 - Gitee"
        Write-Host "=================================="
        Write-Host "FANTASYTOOLS_GITEE_TOKEN：$(Get-MaskedSecret -Value $token)"
        Write-Host "GITEE_TOKEN（备用）：$(Get-MaskedSecret -Value $fallbackToken)"
        Write-Host ""
        Write-Host "1. 设置 FANTASYTOOLS_GITEE_TOKEN"
        Write-Host "2. 清除 FANTASYTOOLS_GITEE_TOKEN"
        Write-Host "3. 打开 Gitee 私人令牌页面"
        Write-Host "0. 返回配置集"
        Write-Host ""

        $choice = (Read-Host "请选择").Trim()
        switch ($choice) {
            "1" {
                Write-Host ""
                Write-Host "请粘贴 Gitee 私人令牌。输入内容不会自动隐藏，请确认周围环境安全。"
                $newToken = Read-Host "Gitee Token"
                if ([string]::IsNullOrWhiteSpace($newToken)) {
                    Write-Host "未输入 Token，已取消。"
                }
                else {
                    Set-UserEnvironmentValue -Name "FANTASYTOOLS_GITEE_TOKEN" -Value $newToken.Trim()
                    Write-Host "已写入用户环境变量 FANTASYTOOLS_GITEE_TOKEN。当前脚本后续发布也可以读取。"
                }
                Pause-Menu
            }
            "2" {
                Clear-UserEnvironmentValue -Name "FANTASYTOOLS_GITEE_TOKEN"
                Write-Host "已清除用户环境变量 FANTASYTOOLS_GITEE_TOKEN。"
                Pause-Menu
            }
            "3" {
                Start-Process "https://gitee.com/profile/personal_access_tokens"
                Write-Host "已打开 Gitee 私人令牌页面。"
                Pause-Menu
            }
            "0" { return }
            default {
                Write-Host "无效选择：$choice"
                Pause-Menu
            }
        }
    }
}

function Invoke-ConfigMenu {
    while ($true) {
        Clear-Host
        Write-Host ""
        Write-Host "Ax工具箱脚本菜单 - 配置集"
        Write-Host "========================"
        Write-Host "1. Gitee 分支（Token 设置）"
        Write-Host "0. 返回主菜单"
        Write-Host ""

        $choice = (Read-Host "请选择").Trim()
        switch ($choice) {
            "1" { Invoke-GiteeTokenSettings }
            "0" { return }
            default {
                Write-Host "无效选择：$choice"
                Pause-Menu
            }
        }
    }
}

function Invoke-SelectedAction {
    param(
        [string]$SelectedAction,
        [string]$SelectedVersion,
        [string]$SelectedRuntime,
        [string]$SelectedOutputRoot,
        [string]$SelectedReleaseAssetRoot
    )

    switch ($SelectedAction) {
        "PackStable" {
            Invoke-Pack -SelectedVersion $SelectedVersion -SelectedRuntime $SelectedRuntime -SelectedOutputRoot $SelectedOutputRoot -SelectedReleaseAssetRoot $SelectedReleaseAssetRoot -SelectedChannel "stable"
        }
        "PackBeta" {
            Invoke-Pack -SelectedVersion $SelectedVersion -SelectedRuntime $SelectedRuntime -SelectedOutputRoot $SelectedOutputRoot -SelectedReleaseAssetRoot $SelectedReleaseAssetRoot -SelectedChannel "beta"
        }
        "PublishLatest" {
            Invoke-PublishLatest -SelectedOutputRoot $SelectedOutputRoot -SelectedReleaseAssetRoot $SelectedReleaseAssetRoot
        }
        "Config" {
            Invoke-ConfigMenu
        }
        default {
            throw "未知操作：$SelectedAction"
        }
    }
}

if ($Action -ne "Menu") {
    Invoke-SelectedAction -SelectedAction $Action -SelectedVersion $Version -SelectedRuntime $Runtime -SelectedOutputRoot $OutputRoot -SelectedReleaseAssetRoot $ReleaseAssetRoot
    exit 0
}

while ($true) {
    Clear-Host
    $selectedAction = "Menu"
    $selectedVersion = ""
    $selectedRuntime = $Runtime
    $selectedOutputRoot = $OutputRoot
    $selectedReleaseAssetRoot = $ReleaseAssetRoot

    Write-Host ""
    Write-Host "Ax工具箱脚本菜单"
    Write-Host "================"
    Write-Host "当前版本：$(Get-CurrentVersion)"
    Write-ScriptConfigStatus
    Write-Host "1. 打包正式版工具箱"
    Write-Host "2. 打包测试版工具箱"
    Write-Host "3. 发布最新包"
    Write-Host "4. 配置集"
    Write-Host "0. 退出"
    Write-Host ""

    $choice = (Read-Host "请选择").Trim()
    switch ($choice) {
        "1" { $selectedAction = "PackStable" }
        "2" { $selectedAction = "PackBeta" }
        "3" { $selectedAction = "PublishLatest" }
        "4" { $selectedAction = "Config" }
        "0" { exit 0 }
        default {
            Write-Host "无效选择：$choice"
            Pause-Menu
            continue
        }
    }

    try {
        if ($selectedAction -eq "Config") {
            Invoke-SelectedAction -SelectedAction $selectedAction -SelectedVersion $selectedVersion -SelectedRuntime $selectedRuntime -SelectedOutputRoot $selectedOutputRoot -SelectedReleaseAssetRoot $selectedReleaseAssetRoot
            continue
        }

        if ($selectedAction -ne "PublishLatest") {
            $selectedOutputRoot = Read-Default -Prompt "输出目录" -DefaultValue $selectedOutputRoot
            $selectedRuntime = Read-Default -Prompt "运行平台（一般保持 win-x64，除非要给特殊电脑打包）" -DefaultValue $selectedRuntime

            $currentVersion = Get-CurrentVersion
            if ($selectedAction -eq "PackStable") {
                $suggestedVersion = Get-NextStableVersion -CurrentVersion $currentVersion
                $defaultVersion = $suggestedVersion
                if (![string]::IsNullOrWhiteSpace($Version)) {
                    $defaultVersion = $Version
                }

                $selectedVersion = Read-NewVersion -CurrentVersion $currentVersion -SuggestedVersion $defaultVersion -IsBeta $false
            }
            else {
                $suggestedVersion = Get-NextBetaVersion -CurrentVersion $currentVersion
                $defaultVersion = $suggestedVersion
                if (![string]::IsNullOrWhiteSpace($Version)) {
                    $defaultVersion = Get-VersionCore -VersionText $Version
                }

                $selectedVersion = Read-NewVersion -CurrentVersion $currentVersion -SuggestedVersion $defaultVersion -IsBeta $true
            }
        }
        elseif ($selectedAction -eq "PublishLatest") {
            Write-Host "将从 $selectedReleaseAssetRoot 读取 toolbox-update.json，并发布其中记录的最新包。"
            Write-Host "发布会尝试同步 GitHub / Gitee；Gitee 需要环境变量 FANTASYTOOLS_GITEE_TOKEN 或 GITEE_TOKEN。"
        }

        Invoke-SelectedAction -SelectedAction $selectedAction -SelectedVersion $selectedVersion -SelectedRuntime $selectedRuntime -SelectedOutputRoot $selectedOutputRoot -SelectedReleaseAssetRoot $selectedReleaseAssetRoot
        Write-Host ""
        Write-Host "操作执行完成。"
    }
    catch {
        Write-Host ""
        Write-Host "操作执行失败：$($_.Exception.Message)"
    }

    Pause-Menu
}
