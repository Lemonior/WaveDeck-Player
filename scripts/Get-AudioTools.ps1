param([string]$Destination = (Join-Path $PSScriptRoot '..\tools'))
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$PSDefaultParameterValues['Invoke-WebRequest:UseBasicParsing'] = $true
$Destination = [IO.Path]::GetFullPath($Destination)
New-Item -ItemType Directory -Force -Path $Destination | Out-Null
& (Join-Path $PSScriptRoot 'Get-DependencySources.ps1') -Destination $Destination
$headers = @{ 'User-Agent' = 'WaveDeck-build/1.0' }
function Get-Text([string]$Uri) {
    $content = (Invoke-WebRequest $Uri -Headers $headers).Content
    if ($content -is [byte[]]) { return [Text.Encoding]::UTF8.GetString($content) }
    return [string]$content
}
function Get-Asset([string]$Uri, [string]$Name) {
    $target = Join-Path $Destination $Name
    if (!(Test-Path -LiteralPath $target)) {
        Write-Host "Downloading $Name"
        Invoke-WebRequest -Uri $Uri -Headers $headers -OutFile ($target + '.partial')
        Move-Item -LiteralPath ($target + '.partial') -Destination $target -Force
    }
    return $target
}
if (!(Test-Path -LiteralPath (Join-Path $Destination 'yt-dlp.version.txt'))) {
    $release = Invoke-RestMethod 'https://api.github.com/repos/yt-dlp/yt-dlp/releases/latest' -Headers $headers
    $asset = $release.assets | Where-Object name -eq 'yt-dlp.exe' | Select-Object -First 1
    $path = Get-Asset $asset.browser_download_url 'yt-dlp.exe'
    $sumAsset = $release.assets | Where-Object name -eq 'SHA2-256SUMS' | Select-Object -First 1
    $sums = Get-Text $sumAsset.browser_download_url
    $expected = (($sums -split "`n" | Where-Object { $_ -match '\s+yt-dlp\.exe\s*$' }) -split '\s+')[0]
    if (!$expected -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $expected) { throw 'yt-dlp checksum mismatch.' }
    $release.tag_name | Set-Content (Join-Path $Destination 'yt-dlp.version.txt')
}
if (!(Test-Path -LiteralPath (Join-Path $Destination 'deno.exe'))) {
    $release = Invoke-RestMethod 'https://api.github.com/repos/denoland/deno/releases/latest' -Headers $headers
    $asset = $release.assets | Where-Object name -eq 'deno-x86_64-pc-windows-msvc.zip' | Select-Object -First 1
    $zip = Get-Asset $asset.browser_download_url 'deno.zip'
    $sumAsset = $release.assets | Where-Object name -eq 'deno-x86_64-pc-windows-msvc.zip.sha256sum' | Select-Object -First 1
    if ($sumAsset) {
        $expected = [regex]::Match((Get-Text $sumAsset.browser_download_url), '[a-fA-F0-9]{64}').Value
        if ((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash -ne $expected) { throw 'Deno checksum mismatch.' }
    }
    Expand-Archive -LiteralPath $zip -DestinationPath $Destination -Force
    $release.tag_name | Set-Content (Join-Path $Destination 'deno.version.txt')
}
if (!(Test-Path -LiteralPath (Join-Path $Destination 'ffmpeg.exe'))) {
    $zip = Get-Asset 'https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip' 'ffmpeg.zip'
    $expected = [regex]::Match((Get-Text 'https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip.sha256'), '[a-fA-F0-9]{64}').Value
    if ((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash -ne $expected) { throw 'FFmpeg checksum mismatch.' }
    $stage = Join-Path $Destination 'ffmpeg-unpacked'
    Expand-Archive -LiteralPath $zip -DestinationPath $stage -Force
    foreach ($name in @('ffmpeg.exe','ffprobe.exe')) {
        $file = Get-ChildItem -LiteralPath $stage -Recurse -Filter $name | Select-Object -First 1
        Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $Destination $name)
    }
    Get-ChildItem -LiteralPath $stage -Recurse -Filter 'LICENSE*' | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $Destination ('FFmpeg-' + $_.Name)) }
}
foreach ($name in @('yt-dlp.exe','ffmpeg.exe','ffprobe.exe','deno.exe')) {
    $file = Join-Path $Destination $name
    Write-Output "$name $((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash)"
}
$licenses = @{
    'NAudio-LICENSE.txt' = 'https://raw.githubusercontent.com/naudio/NAudio/v2.2.1/license.txt'
    'Deno-LICENSE.md' = 'https://raw.githubusercontent.com/denoland/deno/v2.9.7/LICENSE.md'
    'yt-dlp-THIRD-PARTY-LICENSES.txt' = 'https://raw.githubusercontent.com/yt-dlp/yt-dlp/2026.08.19/THIRD_PARTY_LICENSES.txt'
}
foreach ($name in $licenses.Keys) {
    $target = Join-Path $Destination $name
    if (!(Test-Path -LiteralPath $target)) { Invoke-WebRequest $licenses[$name] -Headers $headers -OutFile $target }
}
& (Join-Path $Destination 'ffmpeg.exe') -version | Select-Object -First 1 | Set-Content (Join-Path $Destination 'ffmpeg.version.txt')
