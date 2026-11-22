param(
    [string]$Output = (Join-Path $PSScriptRoot '..\artifacts\releases'),
    [ValidateSet('Windows', 'Linux')][string[]]$Platforms = @('Windows', 'Linux'),
    [string]$ExpectedTag
)
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
[xml]$properties = Get-Content -LiteralPath (Join-Path $repoRoot 'Directory.Build.props') -Raw
$version = [string]$properties.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') { throw 'Use a semantic version such as 1.0.0 in Directory.Build.props.' }
if ($ExpectedTag -and $ExpectedTag -cne "v$version") { throw "Tag $ExpectedTag does not match version v$version." }
$releaseRoot = Join-Path ([IO.Path]::GetFullPath($Output)) $version
foreach ($platform in $Platforms) {
    $rid = if ($platform -eq 'Windows') { 'win-x64' } else { 'linux-x64' }
    if (Test-Path -LiteralPath (Join-Path $releaseRoot "WaveDeck-$version-$rid.zip")) {
        throw "A $rid ZIP already exists. Use a new output directory to rebuild without replacing it."
    }
}
New-Item -ItemType Directory -Force -Path $releaseRoot | Out-Null
& (Join-Path $PSScriptRoot 'Get-AudioTools.ps1')
Add-Type -AssemblyName System.IO.Compression.FileSystem
$checksums = @()
foreach ($platform in $Platforms) {
    $rid = if ($platform -eq 'Windows') { 'win-x64' } else { 'linux-x64' }
    $folder = if ($platform -eq 'Windows') { 'WaveDeck' } else { 'WaveDeck-Linux' }
    $stage = Join-Path (Join-Path $repoRoot 'artifacts\release-staging') ([guid]::NewGuid().ToString('N'))
    $appRoot = Join-Path $stage $folder
    if ($platform -eq 'Windows') {
        & (Join-Path $PSScriptRoot 'Publish.ps1') -Output $appRoot
    } else {
        & (Join-Path $PSScriptRoot 'Publish-Linux.ps1') -Output $appRoot
    }
    Set-Content -LiteralPath (Join-Path $appRoot 'VERSION.txt') -Value $version -Encoding ASCII
    $required = @('WaveDeck.dll', 'WaveDeck.deps.json', 'WaveDeck.runtimeconfig.json', 'VERSION.txt', 'README.md', 'LICENSE', 'THIRD-PARTY-NOTICES.md',
        'tools/NAudio-LICENSE.txt', 'tools/NET-Runtime-LICENSE.txt', 'tools/NET-Runtime-THIRD-PARTY-NOTICES.txt',
        'tools/SoundTouch.Net-LICENSE.txt', 'tools/SoundTouch.Net-2.3.2-source.zip', 'tools/Deno-LICENSE.md', 'tools/yt-dlp-THIRD-PARTY-LICENSES.txt')
    if ($platform -eq 'Windows') {
        $required += @('WaveDeck.exe', 'tools/ffmpeg.exe', 'tools/ffprobe.exe', 'tools/yt-dlp.exe', 'tools/deno.exe', 'tools/FFmpeg-LICENSE', 'tools/NET-Desktop-LICENSE.txt')
    } else {
        $required += @('WaveDeck', 'launch.sh', 'install-dependencies.sh', 'install-shortcut.sh', 'tools/yt-dlp', 'tools/deno', 'tools/Avalonia-LICENSE.md', 'tools/SkiaSharp-LICENSE.txt', 'tools/HarfBuzzSharp-LICENSE.txt')
    }
    foreach ($name in $required) {
        if (!(Test-Path -LiteralPath (Join-Path $appRoot $name) -PathType Leaf)) { throw "Missing release file: $folder/$name" }
    }
    $assemblyVersion = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $appRoot 'WaveDeck.dll')).Version.ToString()
    if ($assemblyVersion -ne ($version.Split('-')[0] + '.0')) { throw "Assembly version $assemblyVersion does not match $version." }
    $manifestFiles = @(Get-ChildItem -LiteralPath $appRoot -Recurse -File | Sort-Object FullName | ForEach-Object {
        [ordered]@{ path = $_.FullName.Substring($appRoot.Length + 1).Replace('\', '/'); length = $_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
    })
    $manifest = [ordered]@{ schema = 1; version = $version; runtimeId = $rid; files = $manifestFiles }
    [IO.File]::WriteAllText((Join-Path $appRoot 'update-manifest.json'), ($manifest | ConvertTo-Json -Depth 6), (New-Object Text.UTF8Encoding($false)))
    $required += 'update-manifest.json'
    $archivePath = Join-Path $releaseRoot "WaveDeck-$version-$rid.zip"
    $partialPath = $archivePath + '.' + [guid]::NewGuid().ToString('N') + '.partial'
    $archive = [IO.Compression.ZipFile]::Open($partialPath, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in (Get-ChildItem -LiteralPath $stage -Recurse -File)) {
            $entryName = $file.FullName.Substring($stage.Length + 1).Replace('\', '/')
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $entryName, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    } finally { $archive.Dispose() }
    $archive = [IO.Compression.ZipFile]::OpenRead($partialPath)
    try {
        foreach ($name in $required) {
            if (!$archive.GetEntry("$folder/$name")) { throw "ZIP is missing $folder/$name" }
        }
    } finally { $archive.Dispose() }
    Move-Item -LiteralPath $partialPath -Destination $archivePath
    $checksums += ((Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($archivePath))
    Write-Output "Packaged: $archivePath"
}
$checksums = foreach ($rid in @('win-x64', 'linux-x64')) {
    $existingArchive = Join-Path $releaseRoot "WaveDeck-$version-$rid.zip"
    if (Test-Path -LiteralPath $existingArchive -PathType Leaf) {
        (Get-FileHash -LiteralPath $existingArchive -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($existingArchive)
    }
}
Set-Content -LiteralPath (Join-Path $releaseRoot 'SHA256SUMS.txt') -Value $checksums -Encoding ASCII
Write-Output "Release version: $version"
