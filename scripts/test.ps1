$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$output = Join-Path $root 'dist\CoreTests.exe'
New-Item -ItemType Directory -Path (Join-Path $root 'dist') -Force | Out-Null
& $compiler /nologo /warnaserror+ /target:exe /platform:x64 /out:$output /r:System.Core.dll /r:System.Xml.Linq.dll (Join-Path $root 'src\Core.cs') (Join-Path $root 'tests\CoreTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
& $output
if ($LASTEXITCODE -ne 0) { throw 'Core tests failed.' }
$transportOutput = Join-Path $root 'dist\TransportTests.exe'
& $compiler /nologo /warnaserror+ /target:exe /platform:x64 /out:$transportOutput /r:System.Core.dll /r:System.Xml.Linq.dll /r:System.Management.dll (Join-Path $root 'src\Core.cs') (Join-Path $root 'src\Hardware.cs') (Join-Path $root 'tests\TransportTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Transport test compilation failed.' }
& $transportOutput
if ($LASTEXITCODE -ne 0) { throw 'Transport tests failed.' }
$cliOutput = Join-Path $root 'dist\CliTests.exe'
& $compiler /nologo /warnaserror+ /target:exe /platform:x64 /main:CliTests /out:$cliOutput /r:System.Core.dll /r:System.Xml.Linq.dll /r:System.Management.dll /r:System.Web.Extensions.dll (Join-Path $root 'src\Core.cs') (Join-Path $root 'src\Hardware.cs') (Join-Path $root 'src\Cli.cs') (Join-Path $root 'tests\CliTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'CLI test compilation failed.' }
& $cliOutput
if ($LASTEXITCODE -ne 0) { throw 'CLI tests failed.' }
$desktopOutput = Join-Path $root 'dist\DesktopSmoke.exe'
& $compiler /nologo /warnaserror+ /target:exe /platform:x64 /define:DESKTOP_SMOKE_STUBS /main:Fkgcc.Tests.DesktopSmoke /out:$desktopOutput /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll (Join-Path $root 'src\Desktop.cs') (Join-Path $root 'tests\DesktopSmoke.cs')
if ($LASTEXITCODE -ne 0) { throw 'Desktop smoke compilation failed.' }
& $desktopOutput
if ($LASTEXITCODE -ne 0) { throw 'Desktop smoke tests failed.' }
