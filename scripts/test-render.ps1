. "$PSScriptRoot\common.ps1"
New-Item -ItemType Directory -Force -Path $artifactDirectory | Out-Null
$testExe = Join-Path $artifactDirectory 'RenderProbe.exe'
$screenshots = Join-Path $artifactDirectory 'render-check'
& $compiler /nologo /target:exe /main:RenderProbe "/out:$testExe" "/win32icon:$src\app.ico" "/win32manifest:$src\app.manifest" @references @coreSources @uiSources "$repoRoot\tests\RenderProbe.cs"
if ($LASTEXITCODE -ne 0) { throw 'Render test build failed.' }
& $testExe $screenshots
if ($LASTEXITCODE -ne 0) { throw 'Render test failed.' }
Write-Output "Review screenshots: $screenshots"
