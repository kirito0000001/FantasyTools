param(
    [ValidateSet("Menu", "Pack", "ReleaseStable", "ReleaseBeta")]
    [string]$Action = "Menu",

    [string]$Version = "",

    [ValidateSet("win-x64", "win-x86", "win-arm64")]
    [string]$Runtime = "win-x64",

    [string]$OutputRoot = "D:\DabaoV"
)

$ErrorActionPreference = "Stop"

$scriptsRoot = $PSScriptRoot
$repoRoot = Split-Path -Parent $scriptsRoot

function Get-DefaultVersion {
    $projectPath = Join-Path $repoRoot "FantasyTools.csproj"
    [xml]$projectXml = Get-Content -LiteralPath $projectPath -Raw -Encoding UTF8
    return ($projectXml.Project.PropertyGroup |
        ForEach-Object { $_.Version } |
        Where-Object { ![string]::IsNullOrWhiteSpace($_) } |
        Select-Object -First 1).Trim()
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

function Invoke-Pack {
    param(
        [string]$SelectedVersion,
        [string]$SelectedRuntime,
        [string]$SelectedOutputRoot
    )

    $arguments = @{
        Runtime    = $SelectedRuntime
        OutputRoot = $SelectedOutputRoot
    }
    if (![string]::IsNullOrWhiteSpace($SelectedVersion)) {
        $arguments.Version = $SelectedVersion
    }

    & (Join-Path $scriptsRoot "打包工具箱.ps1") @arguments
}

function Invoke-Release {
    param(
        [string]$SelectedVersion,
        [string]$SelectedRuntime,
        [string]$SelectedOutputRoot,
        [bool]$IsPrerelease
    )

    $arguments = @{
        Runtime    = $SelectedRuntime
        OutputRoot = $SelectedOutputRoot
    }
    if (![string]::IsNullOrWhiteSpace($SelectedVersion)) {
        $arguments.Version = $SelectedVersion
    }
    if ($IsPrerelease) {
        $arguments.Prerelease = $true
    }

    & (Join-Path $scriptsRoot "发布新版本.ps1") @arguments
}

function Invoke-SelectedAction {
    param(
        [string]$SelectedAction,
        [string]$SelectedVersion,
        [string]$SelectedRuntime,
        [string]$SelectedOutputRoot
    )

    switch ($SelectedAction) {
        "Pack" {
            Invoke-Pack -SelectedVersion $SelectedVersion -SelectedRuntime $SelectedRuntime -SelectedOutputRoot $SelectedOutputRoot
        }
        "ReleaseStable" {
            Invoke-Release -SelectedVersion $SelectedVersion -SelectedRuntime $SelectedRuntime -SelectedOutputRoot $SelectedOutputRoot -IsPrerelease $false
        }
        "ReleaseBeta" {
            Invoke-Release -SelectedVersion $SelectedVersion -SelectedRuntime $SelectedRuntime -SelectedOutputRoot $SelectedOutputRoot -IsPrerelease $true
        }
        default {
            throw "未知操作：$SelectedAction"
        }
    }
}

if ($Action -ne "Menu") {
    Invoke-SelectedAction -SelectedAction $Action -SelectedVersion $Version -SelectedRuntime $Runtime -SelectedOutputRoot $OutputRoot
    exit 0
}

Write-Host ""
Write-Host "幻杀工具箱脚本菜单"
Write-Host "=================="
Write-Host "1. 打包工具箱"
Write-Host "2. 发布正式版"
Write-Host "3. 发布测试版 Beta"
Write-Host "0. 退出"
Write-Host ""

$choice = Read-Host "请选择"
switch ($choice) {
    "1" { $Action = "Pack" }
    "2" { $Action = "ReleaseStable" }
    "3" { $Action = "ReleaseBeta" }
    "0" { exit 0 }
    default { throw "无效选择：$choice" }
}

$Runtime = Read-Default -Prompt "Runtime" -DefaultValue $Runtime
$OutputRoot = Read-Default -Prompt "输出目录" -DefaultValue $OutputRoot
if ($Action -eq "Pack") {
    $Version = Read-Host "新版本号（留空则按当前 csproj 版本打包；填写时必须大于当前版本并会写入 csproj）"
}
else {
    $Version = Read-Default -Prompt "版本号" -DefaultValue $(if ([string]::IsNullOrWhiteSpace($Version)) { Get-DefaultVersion } else { $Version })
}

Invoke-SelectedAction -SelectedAction $Action -SelectedVersion $Version -SelectedRuntime $Runtime -SelectedOutputRoot $OutputRoot

Write-Host ""
Write-Host "脚本执行完成。"
