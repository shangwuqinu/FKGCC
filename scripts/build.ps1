$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Windows .NET Framework 4.x compiler not found.' }
$output = Join-Path $root 'dist'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$common = @((Join-Path $root 'src\Core.cs'), (Join-Path $root 'src\Hardware.cs'))
$manifest = Join-Path $root 'src\app.manifest'
$desktopOutput = Join-Path $output 'FKGCC.Light.exe'
$cliOutput = Join-Path $output 'FKGCC.Cli.exe'
& $compiler /nologo /warnaserror+ /optimize+ /target:winexe /platform:x64 /main:Fkgcc.DesktopProgram /win32manifest:$manifest /out:$desktopOutput /r:System.Core.dll /r:System.Xml.Linq.dll /r:System.Management.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll $common (Join-Path $root 'src\Desktop.cs')
if ($LASTEXITCODE -ne 0) { throw 'Desktop build failed.' }
& $compiler /nologo /warnaserror+ /optimize+ /target:exe /platform:x64 /main:Fkgcc.CliProgram /win32manifest:$manifest /out:$cliOutput /r:System.Core.dll /r:System.Xml.Linq.dll /r:System.Management.dll /r:System.Web.Extensions.dll $common (Join-Path $root 'src\Cli.cs')
if ($LASTEXITCODE -ne 0) { throw 'CLI build failed.' }
Write-Output "Built: $output\FKGCC.Light.exe and FKGCC.Cli.exe"
