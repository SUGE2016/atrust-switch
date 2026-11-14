. "$PSScriptRoot\common.ps1"
New-Item -ItemType Directory -Force -Path $artifactDirectory | Out-Null
$iconBuilder = Join-Path $artifactDirectory 'IconBuilder.exe'
& $compiler /nologo /target:exe "/out:$iconBuilder" /reference:System.Drawing.dll "$src\IconArt.cs" "$PSScriptRoot\IconBuilder.cs"
if ($LASTEXITCODE -ne 0) { throw 'Icon builder compilation failed.' }
& $iconBuilder "$src\app.ico" "$repoRoot\assets\icon.png"
if ($LASTEXITCODE -ne 0) { throw 'Icon generation failed.' }
