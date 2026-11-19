param([string]$Output=(Join-Path $PSScriptRoot '..\WaveDeck-Linux'))
$ErrorActionPreference='Stop'
$outputRoot=[IO.Path]::GetFullPath($Output)
dotnet publish (Join-Path $PSScriptRoot '..\MusicPlayer\WaveDeck.Linux\WaveDeck.Linux.csproj') -c Release -r linux-x64 --self-contained true -p:DebugType=None -p:DebugSymbols=false -o $outputRoot
if($LASTEXITCODE -ne 0){throw 'Linux publish failed.'}
foreach($name in @('LICENSE','THIRD-PARTY-NOTICES.md')){Copy-Item -LiteralPath (Join-Path $PSScriptRoot ('..\'+$name)) -Destination $outputRoot -Force}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\MusicPlayer\MusicPlayer\Assets\WaveDeck.png') -Destination $outputRoot -Force
$linuxTools = Join-Path $PSScriptRoot '..\tools\linux'
& (Join-Path $PSScriptRoot 'Get-Linux-AudioTools.ps1') -Destination $linuxTools
New-Item -ItemType Directory -Force -Path (Join-Path $outputRoot 'tools') | Out-Null
Get-ChildItem -LiteralPath $linuxTools -File | Copy-Item -Destination (Join-Path $outputRoot 'tools') -Force
$packageRoot=Join-Path ([Environment]::GetFolderPath('UserProfile')) '.nuget\packages'
if($env:NUGET_PACKAGES){$packageRoot=$env:NUGET_PACKAGES}
$runtimeVersion=((Get-Content -LiteralPath (Join-Path $outputRoot 'WaveDeck.runtimeconfig.json') -Raw | ConvertFrom-Json).runtimeOptions.includedFrameworks | Where-Object name -eq 'Microsoft.NETCore.App').version
foreach($license in @{
    'NET-Runtime-LICENSE.txt'="microsoft.netcore.app.runtime.linux-x64\$runtimeVersion\LICENSE.TXT"
    'NET-Runtime-THIRD-PARTY-NOTICES.txt'="microsoft.netcore.app.runtime.linux-x64\$runtimeVersion\THIRD-PARTY-NOTICES.TXT"
    'SkiaSharp-LICENSE.txt'='skiasharp\3.119.4\LICENSE.txt'
    'HarfBuzzSharp-LICENSE.txt'='harfbuzzsharp\8.3.1.3\LICENSE.txt'
}.GetEnumerator()){Copy-Item -LiteralPath (Join-Path $packageRoot $license.Value) -Destination (Join-Path $outputRoot ('tools\'+$license.Key)) -Force}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\tools\NAudio-LICENSE.txt') -Destination (Join-Path $outputRoot 'tools\NAudio-LICENSE.txt') -Force
foreach($license in @('Deno-LICENSE.md','yt-dlp-THIRD-PARTY-LICENSES.txt','SoundTouch.Net-LICENSE.txt','SoundTouch.Net-2.3.2-source.zip')){Copy-Item -LiteralPath (Join-Path $PSScriptRoot ('..\tools\'+$license)) -Destination (Join-Path $outputRoot ('tools\'+$license)) -Force}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'linux\Avalonia-LICENSE.md') -Destination (Join-Path $outputRoot 'tools') -Force
foreach($name in @('launch.sh','install-dependencies.sh','install-shortcut.sh','README.md')){Copy-Item -LiteralPath (Join-Path $PSScriptRoot ('linux\'+$name)) -Destination $outputRoot -Force}
Write-Output ('Linux launcher: '+(Join-Path $outputRoot 'WaveDeck'))
