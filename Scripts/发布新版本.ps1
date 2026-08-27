param(
    [string]$OutputRoot = "D:\DabaoV",

    [string]$ReleaseAssetRoot = "",

    [string]$Repository = "kirito0000001/FantasyTools",

    [string]$GiteeOwner = "xiaojie578",

    [string]$GiteeRepo = "FantasyTools",

    [switch]$DryRun
)

$ErrorActionPreference = "Stop"
$script:GiteeAttachmentLimitBytes = 100MB

$scriptsRoot = if (![string]::IsNullOrWhiteSpace($env:FANTASYTOOLS_SCRIPT_DIR)) {
    $env:FANTASYTOOLS_SCRIPT_DIR.TrimEnd('\')
}
else {
    $PSScriptRoot
}
$repoRoot = Split-Path -Parent $scriptsRoot
if ([string]::IsNullOrWhiteSpace($ReleaseAssetRoot)) {
    $ReleaseAssetRoot = Join-Path $repoRoot "ReleaseAssets"
}

function Get-ManifestAsset {
    param(
        [object]$Manifest,
        [string]$AssetRoot
    )

    $asset = @($Manifest.assets | Where-Object { ![string]::IsNullOrWhiteSpace($_.fileName) } | Select-Object -First 1)
    if ($asset.Count -eq 0) {
        throw "toolbox-update.json 中没有可发布的 assets。请先打包工具箱。"
    }

    $zipPath = Join-Path $AssetRoot $asset[0].fileName
    $shaName = [System.IO.Path]::GetFileNameWithoutExtension($asset[0].fileName) + ".sha256.txt"
    $shaPath = Join-Path $AssetRoot $shaName
    return [pscustomobject]@{
        ZipPath = $zipPath
        ShaPath = $shaPath
        Runtime = $asset[0].runtime
    }
}

function Get-GiteeToken {
    foreach ($name in @("FANTASYTOOLS_GITEE_TOKEN", "GITEE_TOKEN")) {
        $value = [Environment]::GetEnvironmentVariable($name, "Process")
        if ([string]::IsNullOrWhiteSpace($value)) {
            $value = [Environment]::GetEnvironmentVariable($name, "User")
        }
        if ([string]::IsNullOrWhiteSpace($value)) {
            $value = [Environment]::GetEnvironmentVariable($name, "Machine")
        }
        if (![string]::IsNullOrWhiteSpace($value)) {
            return $value.Trim()
        }
    }

    return ""
}

function Invoke-GiteeApi {
    param(
        [ValidateSet("Get", "Post", "Patch", "Delete")]
        [string]$Method,
        [string]$Uri,
        [hashtable]$Body = $null
    )

    if ($null -eq $Body) {
        $Body = @{}
    }
    if (![string]::IsNullOrWhiteSpace($script:GiteeToken)) {
        $Body.access_token = $script:GiteeToken
    }

    try {
        return Invoke-RestMethod -Method $Method -Uri $Uri -Body $Body -ContentType "application/x-www-form-urlencoded; charset=utf-8" -ErrorAction Stop
    }
    catch {
        $detail = ""
        if ($_.ErrorDetails -and ![string]::IsNullOrWhiteSpace($_.ErrorDetails.Message)) {
            $detail = $_.ErrorDetails.Message
        }

        if ($_.Exception.Response -and $_.Exception.Response.GetResponseStream()) {
            try {
                $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
                $streamDetail = $reader.ReadToEnd()
                if (![string]::IsNullOrWhiteSpace($streamDetail)) {
                    $detail = $streamDetail
                }
            }
            catch {
                if ([string]::IsNullOrWhiteSpace($detail)) {
                    $detail = ""
                }
            }
        }

        if ([string]::IsNullOrWhiteSpace($detail)) {
            $detail = $_.Exception.Message
        }

        $status = ""
        if ($_.Exception.Response -and $_.Exception.Response.StatusCode) {
            $status = "HTTP $([int]$_.Exception.Response.StatusCode) $($_.Exception.Response.StatusDescription)`n"
        }

        throw "Gitee API 请求失败：$Method $Uri`n$status$detail"
    }
}

function Get-GiteeRepository {
    param(
        [string]$Owner,
        [string]$Repo
    )

    $encodedOwner = [uri]::EscapeDataString($Owner)
    $encodedRepo = [uri]::EscapeDataString($Repo)
    $url = "https://gitee.com/api/v5/repos/$encodedOwner/$encodedRepo"
    return Invoke-GiteeApi -Method Get -Uri $url
}

function Get-GiteeReleaseByTag {
    param(
        [string]$Owner,
        [string]$Repo,
        [string]$Tag
    )

    $encodedOwner = [uri]::EscapeDataString($Owner)
    $encodedRepo = [uri]::EscapeDataString($Repo)
    $url = "https://gitee.com/api/v5/repos/$encodedOwner/$encodedRepo/releases"
    $releases = Invoke-GiteeApi -Method Get -Uri $url
    return @($releases | Where-Object { $_.tag_name -eq $Tag } | Select-Object -First 1)
}

function New-GiteeRelease {
    param(
        [string]$Owner,
        [string]$Repo,
        [string]$Tag,
        [string]$Title,
        [string]$Body,
        [bool]$Prerelease,
        [string]$TargetCommitish
    )

    $encodedOwner = [uri]::EscapeDataString($Owner)
    $encodedRepo = [uri]::EscapeDataString($Repo)
    $url = "https://gitee.com/api/v5/repos/$encodedOwner/$encodedRepo/releases"
    return Invoke-GiteeApi -Method Post -Uri $url -Body @{
        tag_name         = $Tag
        name             = $Title
        body             = $Body
        prerelease       = $Prerelease.ToString().ToLowerInvariant()
        target_commitish = $TargetCommitish
    }
}

function Update-GiteeRelease {
    param(
        [string]$Owner,
        [string]$Repo,
        [int]$ReleaseId,
        [string]$Tag,
        [string]$Title,
        [string]$Body,
        [bool]$Prerelease
    )

    $encodedOwner = [uri]::EscapeDataString($Owner)
    $encodedRepo = [uri]::EscapeDataString($Repo)
    $url = "https://gitee.com/api/v5/repos/$encodedOwner/$encodedRepo/releases/$ReleaseId"
    return Invoke-GiteeApi -Method Patch -Uri $url -Body @{
        tag_name   = $Tag
        name       = $Title
        body       = $Body
        prerelease = $Prerelease.ToString().ToLowerInvariant()
    }
}

function Get-GiteeAttachFiles {
    param(
        [string]$Owner,
        [string]$Repo,
        [int]$ReleaseId
    )

    $encodedOwner = [uri]::EscapeDataString($Owner)
    $encodedRepo = [uri]::EscapeDataString($Repo)
    $url = "https://gitee.com/api/v5/repos/$encodedOwner/$encodedRepo/releases/$ReleaseId/attach_files?per_page=100"
    $files = Invoke-GiteeApi -Method Get -Uri $url
    return @($files)
}

function Remove-GiteeAttachFile {
    param(
        [string]$Owner,
        [string]$Repo,
        [int]$ReleaseId,
        [int]$AttachFileId
    )

    $encodedOwner = [uri]::EscapeDataString($Owner)
    $encodedRepo = [uri]::EscapeDataString($Repo)
    $url = "https://gitee.com/api/v5/repos/$encodedOwner/$encodedRepo/releases/$ReleaseId/attach_files/$AttachFileId"
    Invoke-GiteeApi -Method Delete -Uri $url | Out-Null
}

function Add-GiteeAttachFile {
    param(
        [string]$Owner,
        [string]$Repo,
        [int]$ReleaseId,
        [string]$Path
    )

    $encodedOwner = [uri]::EscapeDataString($Owner)
    $encodedRepo = [uri]::EscapeDataString($Repo)
    $url = "https://gitee.com/api/v5/repos/$encodedOwner/$encodedRepo/releases/$ReleaseId/attach_files"
    $curl = Get-Command curl.exe -ErrorAction SilentlyContinue
    if ($null -eq $curl) {
        throw "没有找到 curl.exe，无法上传 Gitee Release 附件。"
    }

    $fileName = [System.IO.Path]::GetFileName($Path)
    $length = (Get-Item -LiteralPath $Path).Length
    if ($length -gt $script:GiteeAttachmentLimitBytes) {
        throw "Gitee Release 单个附件上限按 100 MiB 处理。$fileName 当前为 $(Format-FileSize -Bytes $length)，超过限制 $(Format-FileSize -Bytes ($length - $script:GiteeAttachmentLimitBytes))。请重新打包并提高 ZIP 压缩率。"
    }

    Write-Host ""
    Write-Host "  >>> 开始上传 Gitee 附件：$fileName" -ForegroundColor Cyan
    Write-Host "      大小：$(Format-FileSize -Bytes $length)"
    $responsePath = Join-Path $env:TEMP "FantasyTools-gitee-upload-$([guid]::NewGuid().ToString('N')).json"
    $previousNativeErrorPreference = $null
    $hasNativeErrorPreference = Test-Path variable:global:PSNativeCommandUseErrorActionPreference
    if ($hasNativeErrorPreference) {
        $previousNativeErrorPreference = $global:PSNativeCommandUseErrorActionPreference
        $global:PSNativeCommandUseErrorActionPreference = $false
    }

    try {
        $httpCode = & curl.exe `
            --fail-with-body `
            --show-error `
            --progress-bar `
            --request POST `
            --header "Accept: application/json" `
            --form "access_token=$script:GiteeToken" `
            --form "file=@$Path;filename=$fileName" `
            --output $responsePath `
            --write-out "%{http_code}" `
            $url
        $curlExitCode = $LASTEXITCODE
    }
    finally {
        if ($hasNativeErrorPreference) {
            $global:PSNativeCommandUseErrorActionPreference = $previousNativeErrorPreference
        }
    }

    if ($curlExitCode -ne 0) {
        $detail = if (Test-Path -LiteralPath $responsePath) {
            (Get-Content -LiteralPath $responsePath -Raw -ErrorAction SilentlyContinue).Trim()
        }
        else {
            ""
        }
        if ([string]::IsNullOrWhiteSpace($detail)) {
            $detail = "Gitee 未返回错误正文。"
        }
        Remove-Item -LiteralPath $responsePath -Force -ErrorAction SilentlyContinue
        throw "Gitee 附件上传失败：$fileName；HTTP $httpCode；curl exit code $curlExitCode；$detail"
    }
    Remove-Item -LiteralPath $responsePath -Force -ErrorAction SilentlyContinue
    Write-Host "  <<< Gitee 附件上传完成：$fileName" -ForegroundColor Green
}

function Add-GitHubReleaseAsset {
    param(
        [string]$Repository,
        [string]$UploadUrl,
        [string]$Path
    )

    $curl = Get-Command curl.exe -ErrorAction SilentlyContinue
    if ($null -eq $curl) {
        throw "没有找到 curl.exe，无法上传 GitHub Release 附件。"
    }

    $token = & gh auth token 2>$null
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($token)) {
        throw "无法从 GitHub CLI 获取认证 Token。请先运行 gh auth login。"
    }

    $fileName = [System.IO.Path]::GetFileName($Path)
    $length = (Get-Item -LiteralPath $Path).Length
    $uploadEndpoint = ($UploadUrl -replace '\{\?name,label\}$', '')
    $uploadEndpoint = '{0}?name={1}' -f $uploadEndpoint, [uri]::EscapeDataString($fileName)
    if (![uri]::IsWellFormedUriString($uploadEndpoint, [UriKind]::Absolute)) {
        throw "GitHub uploadUrl 无效：$uploadEndpoint"
    }

    Write-Host ""
    Write-Host "  >>> 开始上传 GitHub 附件：$fileName" -ForegroundColor Cyan
    Write-Host "      大小：$(Format-FileSize -Bytes $length)"
    $responsePath = Join-Path $env:TEMP "FantasyTools-github-upload-$([guid]::NewGuid().ToString('N')).json"
    $previousNativeErrorPreference = $null
    $hasNativeErrorPreference = Test-Path variable:global:PSNativeCommandUseErrorActionPreference
    if ($hasNativeErrorPreference) {
        $previousNativeErrorPreference = $global:PSNativeCommandUseErrorActionPreference
        $global:PSNativeCommandUseErrorActionPreference = $false
    }

    try {
        $httpCode = & curl.exe `
            --fail-with-body `
            --show-error `
            --progress-bar `
            --request POST `
            --header "Authorization: Bearer $token" `
            --header "Accept: application/vnd.github+json" `
            --header "Content-Type: application/octet-stream" `
            --data-binary "@$Path" `
            --output $responsePath `
            --write-out "%{http_code}" `
            $uploadEndpoint
        $curlExitCode = $LASTEXITCODE
    }
    finally {
        if ($hasNativeErrorPreference) {
            $global:PSNativeCommandUseErrorActionPreference = $previousNativeErrorPreference
        }
    }

    if ($curlExitCode -ne 0) {
        $detail = if (Test-Path -LiteralPath $responsePath) {
            (Get-Content -LiteralPath $responsePath -Raw -ErrorAction SilentlyContinue).Trim()
        }
        else {
            ""
        }
        if ([string]::IsNullOrWhiteSpace($detail)) {
            $detail = "GitHub 未返回错误正文。"
        }
        Remove-Item -LiteralPath $responsePath -Force -ErrorAction SilentlyContinue
        throw "GitHub 附件上传失败：$fileName；HTTP $httpCode；curl exit code $curlExitCode；$detail"
    }
    Remove-Item -LiteralPath $responsePath -Force -ErrorAction SilentlyContinue

    Write-Host "  <<< GitHub 附件上传完成：$fileName" -ForegroundColor Green
}

function Format-FileSize {
    param([long]$Bytes)

    if ($Bytes -ge 1GB) {
        return "{0:N2} GB" -f ($Bytes / 1GB)
    }
    if ($Bytes -ge 1MB) {
        return "{0:N2} MB" -f ($Bytes / 1MB)
    }
    if ($Bytes -ge 1KB) {
        return "{0:N2} KB" -f ($Bytes / 1KB)
    }

    return "$Bytes B"
}

function Publish-GitHubRelease {
    param(
        [string]$Repository,
        [string]$Tag,
        [string]$Title,
        [string]$NotesPath,
        [bool]$Prerelease,
        [string[]]$AssetPaths,
        [switch]$DryRun
    )

    $gh = Get-Command gh -ErrorAction SilentlyContinue
    if ($null -eq $gh) {
        Write-Host "没有找到 GitHub CLI。GitHub 发布跳过，需要手动上传：" -ForegroundColor Yellow
        foreach ($path in $AssetPaths) {
            Write-Host "  $path"
        }
        return $false
    }

    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"

    $previousNativeErrorPreference = $null
    $hasNativeErrorPreference = Test-Path variable:global:PSNativeCommandUseErrorActionPreference
    if ($hasNativeErrorPreference) {
        $previousNativeErrorPreference = $global:PSNativeCommandUseErrorActionPreference
        $global:PSNativeCommandUseErrorActionPreference = $false
    }

    try {
        $existingView = & gh release view $Tag --repo $Repository --json tagName,uploadUrl,isDraft,isPrerelease,assets 2>$null
        $viewExitCode = $LASTEXITCODE
    }
    finally {
        if ($hasNativeErrorPreference) {
            $global:PSNativeCommandUseErrorActionPreference = $previousNativeErrorPreference
        }
        $ErrorActionPreference = $previousErrorActionPreference
    }

    $releaseInfo = $null
    $existingAssetNames = @()
    if ($viewExitCode -eq 0 -and ![string]::IsNullOrWhiteSpace($existingView)) {
        $releaseInfo = $existingView | ConvertFrom-Json
        $existingAssetNames = @($releaseInfo.assets | ForEach-Object { $_.name })
        $expectedAssetNames = @($AssetPaths | ForEach-Object { [System.IO.Path]::GetFileName($_) })
        $missingAssetNames = @($expectedAssetNames | Where-Object { $_ -notin $existingAssetNames })
        if ($missingAssetNames.Count -eq 0 -and !$releaseInfo.isDraft) {
            Write-Host "GitHub 已存在完整版本 $Tag，跳过 GitHub，继续处理其他更新源。" -ForegroundColor Yellow
            return $false
        }

        if ($missingAssetNames.Count -gt 0) {
            Write-Host "GitHub 版本 $Tag 已存在但附件不完整，将继续上传：$($missingAssetNames -join ', ')" -ForegroundColor Yellow
        }
        elseif ($releaseInfo.isDraft) {
            Write-Host "GitHub 版本 $Tag 的附件已完整但仍是草稿，将继续公开 Release。" -ForegroundColor Yellow
        }
    }
    if ($viewExitCode -ne 0) {
        Write-Host "GitHub 未找到版本 $Tag，将创建新 Release。" -ForegroundColor DarkGray
    }

    Write-Host "准备发布到 GitHub：$Repository"
    if ($DryRun) {
        Write-Host "DryRun：跳过 GitHub Release 创建。"
        return $false
    }

    if ($null -eq $releaseInfo) {
        $args = @("release", "create", $Tag)
        $args += @("--title", $Title, "--notes-file", $NotesPath, "--repo", $Repository, "--draft")
        if ($Prerelease) {
            $args += "--prerelease"
        }

        Write-Host ""
        Write-Host ">>> 开始发布 GitHub Release：$Tag" -ForegroundColor Cyan
        Write-Host "    先创建草稿 Release，附件全部上传成功后再公开。"
        gh @args
        if ($LASTEXITCODE -ne 0) {
            throw "gh release create failed with exit code $LASTEXITCODE"
        }

        $releaseJson = & gh release view $Tag --repo $Repository --json uploadUrl,isDraft,assets 2>$null
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($releaseJson)) {
            throw "GitHub Release 已创建，但无法读取 uploadUrl。"
        }

        $releaseInfo = $releaseJson | ConvertFrom-Json
        $existingAssetNames = @()
    }

    foreach ($path in $AssetPaths) {
        $fileName = [System.IO.Path]::GetFileName($path)
        if ($fileName -in $existingAssetNames) {
            Write-Host "  GitHub 附件已存在，跳过：$fileName" -ForegroundColor DarkGray
            continue
        }

        Add-GitHubReleaseAsset -Repository $Repository -UploadUrl $releaseInfo.uploadUrl -Path $path
    }

    $verifiedReleaseJson = & gh release view $Tag --repo $Repository --json assets,isDraft 2>$null
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($verifiedReleaseJson)) {
        throw "GitHub 附件上传后无法重新读取 Release。"
    }
    $verifiedRelease = $verifiedReleaseJson | ConvertFrom-Json
    $verifiedAssetNames = @($verifiedRelease.assets | ForEach-Object { $_.name })
    $stillMissing = @($AssetPaths | ForEach-Object { [System.IO.Path]::GetFileName($_) } | Where-Object { $_ -notin $verifiedAssetNames })
    if ($stillMissing.Count -gt 0) {
        throw "GitHub Release 附件仍不完整：$($stillMissing -join ', ')"
    }

    if ($verifiedRelease.isDraft) {
        Write-Host ""
        Write-Host ">>> GitHub 附件上传完成，正在公开 Release：$Tag" -ForegroundColor Cyan
        gh release edit $Tag --repo $Repository --draft=false | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "GitHub Release 草稿公开失败：$Tag"
        }
    }

    Write-Host "<<< GitHub 发布完成：$Tag" -ForegroundColor Green
    return $true
}

function Publish-GiteeRelease {
    param(
        [string]$Owner,
        [string]$Repo,
        [string]$Tag,
        [string]$Title,
        [string]$Notes,
        [bool]$Prerelease,
        [string[]]$AssetPaths,
        [switch]$DryRun
    )

    $script:GiteeToken = Get-GiteeToken
    if ([string]::IsNullOrWhiteSpace($script:GiteeToken)) {
        Write-Host "没有找到 Gitee Token，Gitee 发布跳过。"
        Write-Host "请设置用户环境变量 FANTASYTOOLS_GITEE_TOKEN 或 GITEE_TOKEN 后重新发布。"
        return $false
    }

    Write-Host "准备发布到 Gitee：$Owner/$Repo"
    if ($DryRun) {
        Write-Host "DryRun：跳过 Gitee Release 创建。"
        return $false
    }

    $oversizedAssets = @($AssetPaths | Where-Object { (Get-Item -LiteralPath $_).Length -gt $script:GiteeAttachmentLimitBytes })
    if ($oversizedAssets.Count -gt 0) {
        Write-Host "Gitee 发布跳过：存在超过 100 MiB 的附件。" -ForegroundColor Yellow
        foreach ($path in $oversizedAssets) {
            $length = (Get-Item -LiteralPath $path).Length
            Write-Host "  $([System.IO.Path]::GetFileName($path))：$(Format-FileSize -Bytes $length)，超出 $(Format-FileSize -Bytes ($length - $script:GiteeAttachmentLimitBytes))"
        }
        Write-Host "请使用 Bandizip 最高压缩重新打包；GitHub 仍会继续发布。" -ForegroundColor Yellow
        return $false
    }

    $release = Get-GiteeReleaseByTag -Owner $Owner -Repo $Repo -Tag $Tag
    $existingFiles = @()
    if ($release.Count -ne 0) {
        $existingFiles = Get-GiteeAttachFiles -Owner $Owner -Repo $Repo -ReleaseId ([int]$release[0].id)
        $expectedNames = @($AssetPaths | ForEach-Object { [System.IO.Path]::GetFileName($_) })
        $existingNames = @($existingFiles | ForEach-Object { $_.name })
        $missingNames = @($expectedNames | Where-Object { $_ -notin $existingNames })
        if ($missingNames.Count -eq 0) {
            Write-Host "Gitee 已存在完整版本 $Tag，跳过 Gitee，继续处理其他更新源。" -ForegroundColor Yellow
            return $false
        }

        Write-Host "Gitee 版本 $Tag 已存在但附件不完整，将继续上传：$($missingNames -join ', ')" -ForegroundColor Yellow
        $release = $release[0]
        Update-GiteeRelease -Owner $Owner -Repo $Repo -ReleaseId ([int]$release.id) -Tag $Tag -Title $Title -Body $Notes -Prerelease $Prerelease | Out-Null
    }
    else {
        Write-Host ">>> 正在读取 Gitee 仓库信息..." -ForegroundColor Cyan
        $repositoryInfo = Get-GiteeRepository -Owner $Owner -Repo $Repo
        $targetCommitish = $repositoryInfo.default_branch
        if ([string]::IsNullOrWhiteSpace($targetCommitish)) {
            $targetCommitish = "master"
        }
        Write-Host "  Gitee 目标分支：$targetCommitish"

        Write-Host ""
        Write-Host ">>> 开始创建 Gitee Release：$Tag" -ForegroundColor Cyan
        $release = New-GiteeRelease -Owner $Owner -Repo $Repo -Tag $Tag -Title $Title -Body $Notes -Prerelease $Prerelease -TargetCommitish $targetCommitish
        Write-Host "<<< Gitee Release 创建完成：$Tag" -ForegroundColor Green
    }

    $releaseId = [int]$release.id
    foreach ($path in $AssetPaths) {
        $fileName = [System.IO.Path]::GetFileName($path)
        if (@($existingFiles | Where-Object { $_.name -eq $fileName }).Count -gt 0) {
            Write-Host "  Gitee 附件已存在，跳过：$fileName" -ForegroundColor DarkGray
            continue
        }

        Add-GiteeAttachFile -Owner $Owner -Repo $Repo -ReleaseId $releaseId -Path $path
    }

    $uploadedFiles = Get-GiteeAttachFiles -Owner $Owner -Repo $Repo -ReleaseId $releaseId
    $uploadedNames = @($uploadedFiles | ForEach-Object { $_.name })
    $stillMissing = @($AssetPaths | ForEach-Object { [System.IO.Path]::GetFileName($_) } | Where-Object { $_ -notin $uploadedNames })
    if ($stillMissing.Count -gt 0) {
        throw "Gitee Release 附件仍不完整：$($stillMissing -join ', ')"
    }

    Write-Host "Gitee 发布完成：$Tag" -ForegroundColor Green
    return $true
}

$assetRoot = $ReleaseAssetRoot
$manifestPath = Join-Path $assetRoot "toolbox-update.json"
if (!(Test-Path -LiteralPath $manifestPath)) {
    throw "没有找到最新包清单：$manifestPath。请先运行【打包正式版工具箱】或【打包测试版工具箱】。"
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ([string]::IsNullOrWhiteSpace($manifest.version)) {
    throw "toolbox-update.json 缺少 version。请重新打包。"
}

$releaseAsset = Get-ManifestAsset -Manifest $manifest -AssetRoot $assetRoot
$zipPath = $releaseAsset.ZipPath
$shaPath = $releaseAsset.ShaPath

foreach ($path in @($zipPath, $shaPath, $manifestPath)) {
    if (!(Test-Path -LiteralPath $path)) {
        throw "发布资产缺失：$path。请重新打包后再发布。"
    }
}

$version = $manifest.version
$channel = if ([string]::IsNullOrWhiteSpace($manifest.channel)) { "stable" } else { $manifest.channel.ToString().ToLowerInvariant() }
$isPrerelease = $channel -eq "beta" -or $version.Contains("-")

$tag = "v$version"
$title = if ($isPrerelease) { "幻杀工具箱 $version 测试版" } else { "幻杀工具箱 $version 正式版" }
$notesPath = Join-Path $scriptsRoot "新版本介绍.txt"
if (!(Test-Path -LiteralPath $notesPath)) {
    $title | Set-Content -LiteralPath $notesPath -Encoding UTF8
}

$notes = Get-Content -LiteralPath $notesPath -Raw -Encoding UTF8
if ([string]::IsNullOrWhiteSpace($notes)) {
    $notes = $title
    Set-Content -LiteralPath $notesPath -Value $notes -Encoding UTF8
}

Write-Host "准备发布最新包："
Write-Host "  版本：$version"
Write-Host "  通道：$channel"
Write-Host "  运行平台：$($releaseAsset.Runtime)"
Write-Host "  GitHub 仓库：$Repository"
Write-Host "  Gitee 仓库：$GiteeOwner/$GiteeRepo"
Write-Host "  Zip：$zipPath"

$assetPaths = @($zipPath, $shaPath, $manifestPath)
$publishedTargets = New-Object System.Collections.Generic.List[string]
$skippedTargets = New-Object System.Collections.Generic.List[string]

if (Publish-GitHubRelease -Repository $Repository -Tag $tag -Title $title -NotesPath $notesPath -Prerelease $isPrerelease -AssetPaths $assetPaths -DryRun:$DryRun) {
    $publishedTargets.Add("GitHub") | Out-Null
}
else {
    $skippedTargets.Add("GitHub") | Out-Null
}

if (Publish-GiteeRelease -Owner $GiteeOwner -Repo $GiteeRepo -Tag $tag -Title $title -Notes $notes -Prerelease $isPrerelease -AssetPaths $assetPaths -DryRun:$DryRun) {
    $publishedTargets.Add("Gitee") | Out-Null
}
else {
    $skippedTargets.Add("Gitee") | Out-Null
}

if ($publishedTargets.Count -eq 0) {
    Write-Host ""
    if ($DryRun) {
        Write-Host "DryRun 检查完成：没有执行真实发布。" -ForegroundColor Cyan
        return
    }

    Write-Host "没有新的更新源需要发布，所有可用源都已存在或被跳过。" -ForegroundColor Yellow
    Write-Host "如果需要覆盖已有版本，请先到对应 Release 页面手动删除旧版本后重新发布。"
    return
}

Write-Host ""
Write-Host "发布流程结束：$tag" -ForegroundColor Cyan
Write-Host "  已发布：$($publishedTargets -join ', ')"
if ($skippedTargets.Count -gt 0) {
    Write-Host "  已跳过：$($skippedTargets -join ', ')"
}
