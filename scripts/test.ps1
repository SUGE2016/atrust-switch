param([switch]$Live)
. "$PSScriptRoot\common.ps1"
New-Item -ItemType Directory -Force -Path $artifactDirectory | Out-Null
$testExe = Join-Path $artifactDirectory 'Tests.exe'
& $compiler /nologo /target:exe "/out:$testExe" @references @coreSources "$repoRoot\tests\Tests.cs"
if ($LASTEXITCODE -ne 0) { throw 'Test build failed.' }
$testArgs = @()
if ($Live) {
    Copy-Item -LiteralPath $testExe -Destination (Join-Path $artifactDirectory 'aTrustTray.exe') -Force
    $testArgs = @('--live')
}
& $testExe @testArgs | Tee-Object -FilePath (Join-Path $artifactDirectory 'test-results.txt')
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
