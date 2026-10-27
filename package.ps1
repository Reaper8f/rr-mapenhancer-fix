param(
    [string]$BuildDirectory = (Join-Path $PSScriptRoot 'artifacts\bin\Release'),
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'artifacts\packages')
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$manifest = [IO.File]::ReadAllText((Join-Path $BuildDirectory 'Info.json')) | ConvertFrom-Json
if ($manifest.identifier -ne 'vanguard/map-enhancer') { throw 'Unexpected mod identifier' }
if (@($manifest.assemblies).Count -ne 1 -or $manifest.assemblies[0] -ne 'MapEnhancer.dll') { throw 'Unexpected entry assembly list' }
$files = @('MapEnhancer.dll','0Harmony.dll','Info.json','arrow.png','flare.png','traincar.png','level-crossing.png','spawn-points.json','LICENSE','THIRD-PARTY-NOTICES.txt')
foreach ($name in $files) {
    if (-not (Test-Path -LiteralPath (Join-Path $BuildDirectory $name) -PathType Leaf)) { throw "Missing package file: $name" }
}
[IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null
$destination = Join-Path $OutputDirectory ("MapEnhancer-" + $manifest.version + ".zip")
if (Test-Path -LiteralPath $destination) { throw 'Package exists; use a new output directory for a new candidate.' }
$archive = [IO.Compression.ZipFile]::Open($destination, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($name in $files) {
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, (Join-Path $BuildDirectory $name),
            ("MapEnhancer/" + $name), [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally { $archive.Dispose() }
$archive = [IO.Compression.ZipFile]::OpenRead($destination)
try {
    $actual = @($archive.Entries | ForEach-Object { $_.FullName })
    $expected = @($files | ForEach-Object { "MapEnhancer/$_" })
    if (Compare-Object $expected $actual) { throw 'Archive entry validation failed' }
} finally { $archive.Dispose() }
Get-FileHash -LiteralPath $destination -Algorithm SHA256 | Select-Object Path, Hash
