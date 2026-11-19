param([string]$Destination=(Join-Path $PSScriptRoot '..\tools\linux'))
$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
$destinationRoot=[IO.Path]::GetFullPath($Destination)
New-Item -Path $destinationRoot -ItemType Directory -Force | Out-Null
$headers=@{'User-Agent'='WaveDeck-build/1.0.0'}
if(!(Test-Path -LiteralPath (Join-Path $destinationRoot 'yt-dlp'))){
    $release=Invoke-RestMethod https://api.github.com/repos/yt-dlp/yt-dlp/releases/latest -Headers $headers
    $asset=$release.assets|Where-Object name -eq 'yt-dlp_linux'
    $sumsAsset=$release.assets|Where-Object name -eq 'SHA2-256SUMS'
    Invoke-WebRequest -UseBasicParsing -Uri $asset.browser_download_url -OutFile (Join-Path $destinationRoot 'yt-dlp')
    $sums=(Invoke-WebRequest -UseBasicParsing -Uri $sumsAsset.browser_download_url).Content
    if($sums -is [byte[]]){$sums=[Text.Encoding]::UTF8.GetString($sums)}
    $expected=[regex]::Match($sums,'(?m)^([a-fA-F0-9]{64})\s+yt-dlp_linux\s*$').Groups[1].Value
    if(!$expected -or (Get-FileHash -LiteralPath (Join-Path $destinationRoot 'yt-dlp')).Hash -ne $expected){throw 'yt-dlp Linux checksum mismatch.'}
    $release.tag_name|Set-Content -LiteralPath (Join-Path $destinationRoot 'yt-dlp.version.txt') -Encoding UTF8
}
if(!(Test-Path -LiteralPath (Join-Path $destinationRoot 'deno'))){
    $release=Invoke-RestMethod https://api.github.com/repos/denoland/deno/releases/latest -Headers $headers
    $asset=$release.assets|Where-Object name -eq 'deno-x86_64-unknown-linux-gnu.zip'
    $archive=Join-Path $destinationRoot 'deno-download.zip'
    Invoke-WebRequest -UseBasicParsing -Uri $asset.browser_download_url -OutFile $archive
    if(!$asset.digest -or (Get-FileHash -LiteralPath $archive).Hash -ne $asset.digest.Replace('sha256:','')){throw 'Deno Linux checksum mismatch.'}
    Expand-Archive -LiteralPath $archive -DestinationPath $destinationRoot -Force
    Remove-Item -LiteralPath $archive
    $release.tag_name|Set-Content -LiteralPath (Join-Path $destinationRoot 'deno.version.txt') -Encoding UTF8
}
