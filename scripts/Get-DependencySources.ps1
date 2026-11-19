param([string]$Destination = (Join-Path $PSScriptRoot '..\tools'))
$ErrorActionPreference = 'Stop'
$Destination = [IO.Path]::GetFullPath($Destination)
New-Item -ItemType Directory -Force -Path $Destination | Out-Null
$provenance = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\Context\soundtouch-source-provenance.json') -Raw | ConvertFrom-Json
$archivePath = Join-Path $Destination 'SoundTouch.Net-2.3.2-source.zip'
if (!(Test-Path -LiteralPath $archivePath)) {
    Invoke-WebRequest -UseBasicParsing -Uri $provenance.Source -OutFile ($archivePath + '.partial')
    Move-Item -LiteralPath ($archivePath + '.partial') -Destination $archivePath
}
if ((Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash -ne $provenance.SHA256) {
    throw 'SoundTouch.Net source checksum mismatch.'
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($archivePath)
try {
    $entry = $archive.GetEntry(('soundtouch.net-' + $provenance.Commit + '/LICENSE'))
    if (!$entry) { throw 'SoundTouch.Net source license is missing.' }
    [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $Destination 'SoundTouch.Net-LICENSE.txt'), $true)
} finally {
    $archive.Dispose()
}
