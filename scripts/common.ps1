$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '64-bit .NET Framework 4.x compiler was not found.' }
$src = Join-Path $repoRoot 'src'
$artifactDirectory = Join-Path $repoRoot 'artifacts'
$references = @('/reference:System.Drawing.dll', '/reference:System.Windows.Forms.dll', '/reference:System.Management.dll', '/reference:System.ServiceProcess.dll', '/reference:Microsoft.CSharp.dll')
$coreSources = @("$src\Core.cs", "$src\Diagnostics.cs", "$src\OnDemand.cs")
$uiSources = @("$src\App.cs", "$src\Theme.cs", "$src\IconArt.cs", "$src\AssemblyInfo.cs")
