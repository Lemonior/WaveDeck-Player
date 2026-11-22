param([string]$Revision='HEAD',[string]$Output=(Join-Path $PSScriptRoot '..\artifacts\android-source'))
$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
$repoRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$commit=(& git -C $repoRoot rev-parse --verify "$Revision^{commit}").Trim()
if($LASTEXITCODE -ne 0 -or $commit -notmatch '^[a-f0-9]{40}$'){throw 'Choose an existing committed release revision.'}
[xml]$props=(& git -C $repoRoot show "${commit}:Directory.Build.props") -join "`n"
$version=[string]$props.Project.PropertyGroup.Version
if($version -notmatch '^\d+\.\d+\.\d+$'){throw 'The revision must contain a stable central version.'}
$Output=[IO.Path]::GetFullPath($Output)
$destination=Join-Path $Output "WaveDeck-$version-android-source.zip"
if(Test-Path -LiteralPath $destination){throw 'The source bundle already exists. Choose a different output directory.'}
$stage=Join-Path $repoRoot ('artifacts\android-source-staging\'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage,$Output -Force|Out-Null
$source=Join-Path $stage 'WaveDeck-source.zip'
& git -C $repoRoot archive --format=zip "--output=$source" $commit
if($LASTEXITCODE -ne 0){throw 'Could not archive the committed WaveDeck source.'}
$extractor=Join-Path $stage 'NewPipeExtractor-6ce85c1-source.zip'
Invoke-WebRequest -UseBasicParsing -Uri 'https://codeload.github.com/TeamNewPipe/NewPipeExtractor/zip/6ce85c1af05bbb854bae73f8936fd9f75bb8f9e1' -OutFile $extractor
if((Get-FileHash -LiteralPath $extractor -Algorithm SHA256).Hash -ne '323dfa1f90af0fdb98e6d88f0a2db803028ed53ab57c295b4274733188848798'){throw 'Extractor source checksum mismatch.'}
$instructions=@"
WaveDeck $version Android corresponding source
WaveDeck commit: $commit

Extract WaveDeck-source.zip into a working directory, install JDK 17 or 21,
then run scripts/Publish-Android.ps1 from its root on Windows. The project
README documents the SDK, pinned Gradle/dependency versions and tests.
The build downloads normal Maven/JitPack dependencies using app/gradle.lockfile.

NewPipeExtractor-6ce85c1-source.zip contains the unmodified extractor source
used by this APK (v0.27.1, GPL-3.0-or-later). Gradle uses the pinned upstream
artifact. All APK dependency coordinates/notices are recorded in the project
and APK assets; corresponding Rhino sources are available at
https://github.com/mozilla/rhino/tree/Rhino1_8_1_Release .
Other dependencies and original WaveDeck source retain their upstream licenses.
The combined Android application is GPL-3.0-or-later; desktop licensing is unchanged.

Private development signing credentials are intentionally excluded. A source
rebuild can use its own signer; updating the published installation requires
the original signer. No credentials or user data are required to build/play.
"@
[IO.File]::WriteAllText((Join-Path $stage 'BUILD-SOURCE.txt'),$instructions,(New-Object Text.UTF8Encoding($false)))
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($stage,$destination,[IO.Compression.CompressionLevel]::Optimal,$false)
Write-Output "Source bundle: $destination"
Write-Output "SHA256: $((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant())"
