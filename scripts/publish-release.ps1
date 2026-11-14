param([switch]$PlanOnly)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$version = (Get-Content -LiteralPath (Join-Path $repoRoot 'VERSION') -Raw).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'VERSION must contain major.minor.patch.' }
if ($env:GITHUB_REPOSITORY -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') { throw 'Missing or invalid repository.' }
if ($env:GITHUB_SHA -notmatch '^[0-9a-f]{40}$') { throw 'Missing or invalid commit SHA.' }
if ($env:GITHUB_EVENT_NAME -ne 'push') { throw 'Only push events may publish.' }

$exePath = Join-Path $repoRoot 'bin\aTrust开关.exe'
$assetPath = Join-Path $repoRoot 'bin\aTrustSwitch.exe'
$fileVersion = (Get-Item -LiteralPath $exePath).VersionInfo.FileVersion
if ($fileVersion -ne "$version.0") { throw 'VERSION and executable file version must match.' }
$hash = (Get-FileHash -LiteralPath $exePath -Algorithm SHA256).Hash.ToLowerInvariant()
$checksumPath = Join-Path $repoRoot 'bin\SHA256SUMS.txt'
$notesPath = Join-Path $repoRoot 'bin\release-notes.md'
$isStable = $env:GITHUB_REF -match '^refs/tags/v\d+\.\d+\.\d+$'
if ($isStable) {
    $tag = $env:GITHUB_REF.Substring(10)
    if ($tag -ne "v$version") { throw 'The version tag must match VERSION and AssemblyInfo.cs.' }
    $title = "aTrust Switch $tag"
    $kind = '正式版本'
} else {
    if ($env:GITHUB_REF -ne 'refs/heads/main') { throw 'Only main or a matching version tag may publish.' }
    if ($env:GITHUB_RUN_ID -notmatch '^\d+$' -or $env:GITHUB_RUN_ATTEMPT -notmatch '^\d+$') { throw 'Missing workflow run identity.' }
    $tag = "build-$($env:GITHUB_RUN_ID)-$($env:GITHUB_RUN_ATTEMPT)"
    $title = "aTrust Switch v$version-dev.$($env:GITHUB_RUN_NUMBER) ($($env:GITHUB_SHA.Substring(0,7)))"
    $kind = '自动预发布版本：来自 main，尚未完成本机启停与网络回归验证'
}

$notes = @"
$kind

作者：新BUG主理人 · 许可证：MIT

下载附件 aTrustSwitch.exe 即可运行；SHA256SUMS.txt 用于校验下载文件。程序未做代码签名。

需要 64 位 Windows、.NET Framework 4.x 和默认路径安装的 aTrust + EAIO。服务操作会请求管理员权限。网络过滤驱动可能保持加载；EAIO 可能被其他软件共用。

已通过 GitHub Actions 基础测试和编译；CI 不包含厂商客户端、实际服务启停或网络恢复验证。

退出按钮不是许愿池，手动启动也不是五分钟有效的体验券。下班了，别给自己续命。

源码提交：$($env:GITHUB_SHA)
使用说明：https://github.com/$($env:GITHUB_REPOSITORY)/blob/$($env:GITHUB_SHA)/README.md
构建记录：https://github.com/$($env:GITHUB_REPOSITORY)/actions/runs/$($env:GITHUB_RUN_ID)
SHA256：$hash
"@

$releaseArgs = @('release', 'create', $tag, $assetPath, $checksumPath,
    '--repo', $env:GITHUB_REPOSITORY, '--target', $env:GITHUB_SHA,
    '--title', $title, '--notes-file', $notesPath)
if ($isStable) { $releaseArgs += @('--verify-tag', '--latest') }
else { $releaseArgs += @('--prerelease', '--latest=false') }

if ($PlanOnly) {
    [pscustomobject]@{Tag=$tag; Title=$title; Stable=$isStable; Commit=$env:GITHUB_SHA; SHA256=$hash; Arguments=$releaseArgs}
    return
}
if ([string]::IsNullOrWhiteSpace($env:GH_TOKEN)) { throw 'GH_TOKEN is required to publish.' }
$utf8 = New-Object System.Text.UTF8Encoding($false)
Copy-Item -LiteralPath $exePath -Destination $assetPath -Force
[System.IO.File]::WriteAllText($checksumPath,($hash + '  aTrustSwitch.exe' + "`n"),$utf8)
[System.IO.File]::WriteAllText($notesPath,$notes,$utf8)
& gh @releaseArgs
if ($LASTEXITCODE -ne 0) { throw 'GitHub Release creation failed.' }
$releaseJson = & gh release view $tag --repo $env:GITHUB_REPOSITORY --json assets
if ($LASTEXITCODE -ne 0) { throw 'Release verification failed.' }
$release = $releaseJson | ConvertFrom-Json
foreach ($assetFile in @($assetPath, $checksumPath)) {
    $assetName = [System.IO.Path]::GetFileName($assetFile)
    $remoteAsset = @($release.assets | Where-Object { $_.name -ceq $assetName })
    $expectedDigest = 'sha256:' + (Get-FileHash -LiteralPath $assetFile -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($remoteAsset.Count -ne 1 -or $remoteAsset[0].digest -ne $expectedDigest) {
        throw "Release asset name or digest mismatch: $assetName"
    }
}
Write-Output 'Published assets verified by name and SHA256.'
