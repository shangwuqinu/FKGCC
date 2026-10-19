$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$release = Join-Path $root 'release'
[xml]$manifest = Get-Content -LiteralPath (Join-Path $root 'src\app.manifest') -Raw
$version = ([version]$manifest.assembly.assemblyIdentity.version).ToString(3) + '-preview'
$packageName = "FKGCC-Light-$version-win-x64"
$stage = Join-Path $release $packageName
$packageFiles = [System.Collections.Generic.List[string]]::new()
New-Item -ItemType Directory -Path $stage, (Join-Path $stage 'src'), (Join-Path $stage 'tests'), (Join-Path $stage 'scripts'), (Join-Path $stage 'docs') -Force | Out-Null
foreach ($name in @('FKGCC.Light.exe','FKGCC.Cli.exe')) {
    $binary = Join-Path $root "dist\$name"
    if (-not (Test-Path -LiteralPath $binary)) { throw "Missing $binary; run scripts/build.ps1 first." }
    Copy-Item -LiteralPath $binary -Destination (Join-Path $stage $name) -Force
    $packageFiles.Add($name)
}
foreach ($name in @('README.md','LICENSE','THIRD_PARTY_NOTICES.md','AUTHORS.md')) {
    Copy-Item -LiteralPath (Join-Path $root $name) -Destination $stage -Force
    $packageFiles.Add($name)
}
foreach ($folder in @('src','tests','scripts','docs')) {
    Get-ChildItem -LiteralPath (Join-Path $root $folder) -File | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $stage $folder) -Force
        $packageFiles.Add("$folder/$($_.Name)")
    }
}
$zip = Join-Path $release "$packageName.zip"
# Add only the files copied in this run, excluding stale files in an existing stage.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$stream = [System.IO.File]::Open($zip, [System.IO.FileMode]::Create)
try {
    $archive = [System.IO.Compression.ZipArchive]::new($stream, [System.IO.Compression.ZipArchiveMode]::Create, $true)
    try {
        foreach ($relative in $packageFiles) {
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, (Join-Path $stage $relative), "$packageName/$relative", [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    } finally { $archive.Dispose() }
} finally { $stream.Dispose() }
$checksums = foreach ($path in @((Join-Path $stage 'FKGCC.Light.exe'), (Join-Path $stage 'FKGCC.Cli.exe'), $zip)) {
    $hash = Get-FileHash -LiteralPath $path -Algorithm SHA256
    $hash.Hash.ToLowerInvariant() + '  ' + (Split-Path -Leaf $path)
}
$checksums | Set-Content -LiteralPath (Join-Path $release 'SHA256SUMS.txt') -Encoding ascii
@{ version=$version; tag="v$version"; name=$packageName; archive="$packageName.zip" } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $release 'package-info.json') -Encoding utf8
Get-FileHash -LiteralPath $zip -Algorithm SHA256 | Format-List
