param([string]$Output = (Join-Path $PSScriptRoot '..\WaveDeck'))
$ErrorActionPreference = 'Stop'
$Output = [IO.Path]::GetFullPath($Output)
$project = Join-Path $PSScriptRoot '..\MusicPlayer\MusicPlayer\MusicPlayer.csproj'
& dotnet publish $project -c Release -r win-x64 --self-contained true -p:DebugType=None -p:DebugSymbols=false -o $Output
if ($LASTEXITCODE -ne 0) { throw 'WaveDeck publish failed.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\README.md') -Destination $Output
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\THIRD-PARTY-NOTICES.md') -Destination $Output
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\LICENSE') -Destination $Output
$packageDir = $env:NUGET_PACKAGES
if (!$packageDir) { $packageDir = Join-Path ([Environment]::GetFolderPath('UserProfile')) '.nuget\packages' }
$frameworks = (Get-Content -LiteralPath (Join-Path $Output 'WaveDeck.runtimeconfig.json') -Raw | ConvertFrom-Json).runtimeOptions.includedFrameworks
$runtimeVersion = ($frameworks | Where-Object name -eq 'Microsoft.NETCore.App').version
$desktopVersion = ($frameworks | Where-Object name -eq 'Microsoft.WindowsDesktop.App').version
$licenses = @{
    'NAudio-LICENSE.txt' = 'naudio\2.2.1\license.txt'
    'NET-Runtime-LICENSE.txt' = "microsoft.netcore.app.runtime.win-x64\$runtimeVersion\LICENSE.TXT"
    'NET-Runtime-THIRD-PARTY-NOTICES.txt' = "microsoft.netcore.app.runtime.win-x64\$runtimeVersion\THIRD-PARTY-NOTICES.TXT"
    'NET-Desktop-LICENSE.txt' = "microsoft.windowsdesktop.app.runtime.win-x64\$desktopVersion\LICENSE"
}
foreach ($name in $licenses.Keys) {
    $source = Join-Path $packageDir $licenses[$name]
    Copy-Item -LiteralPath $source -Destination (Join-Path $Output ('tools\' + $name)) -Force
}
Write-Output "Launch: $(Join-Path $Output 'WaveDeck.exe')"
