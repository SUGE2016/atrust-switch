. "$PSScriptRoot\common.ps1"
$binDirectory = Join-Path $repoRoot 'bin'
New-Item -ItemType Directory -Force -Path $binDirectory | Out-Null
$destination = Join-Path $binDirectory 'aTrust开关.exe'
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ /utf8output "/out:$destination" "/win32icon:$src\app.ico" "/win32manifest:$src\app.manifest" @references @coreSources @uiSources
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Write-Output "Built: $destination ($((Get-Item -LiteralPath $destination).Length) bytes)"
Write-Output "SHA256: $((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash)"
