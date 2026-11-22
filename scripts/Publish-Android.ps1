param([string]$Output=(Join-Path $PSScriptRoot '..\WaveDeck-Android'),[string]$JavaHome=$env:JAVA_HOME)
$ErrorActionPreference='Stop'
$repoRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$project=Join-Path $repoRoot 'MusicPlayer\WaveDeck.Android'
$toolchain=& (Join-Path $PSScriptRoot 'Get-AndroidToolchain.ps1') -JavaHome $JavaHome
[xml]$properties=Get-Content -LiteralPath (Join-Path $repoRoot 'Directory.Build.props') -Raw
$version=[string]$properties.Project.PropertyGroup.Version
$signingRoot=Join-Path $repoRoot 'tools\android-signing'
$signingFile=Join-Path $signingRoot 'wavedeck-development.jks'
$signingSettings=Join-Path $signingRoot 'development.json'
$logRoot=Join-Path $repoRoot 'artifacts\android-build'
New-Item -ItemType Directory -Path $signingRoot,$logRoot -Force | Out-Null
if (!(Test-Path -LiteralPath $signingSettings)) {
    if (Test-Path -LiteralPath $signingFile) { throw 'The signing key exists but its local password file is missing. Restore the matching password file; do not replace the key.' }
    $bytes=New-Object byte[] 32; $random=[Security.Cryptography.RandomNumberGenerator]::Create();$random.GetBytes($bytes);$random.Dispose()
    [IO.File]::WriteAllText($signingSettings,(@{StorePassword=[Convert]::ToBase64String($bytes)}|ConvertTo-Json))
}
$saved=@{}
foreach ($name in @('JAVA_HOME','ANDROID_HOME','ANDROID_USER_HOME','GRADLE_USER_HOME','WAVEDECK_ANDROID_KEYSTORE','WAVEDECK_ANDROID_STORE_PASSWORD')) { $saved[$name]=[Environment]::GetEnvironmentVariable($name,'Process') }
try {
    $env:JAVA_HOME=$toolchain.JavaHome;$env:ANDROID_HOME=$toolchain.SdkRoot;$env:ANDROID_USER_HOME=Join-Path $repoRoot 'tools\android-user';$env:GRADLE_USER_HOME=$toolchain.GradleHome
    $private=Get-Content -LiteralPath $signingSettings -Raw|ConvertFrom-Json
    $env:WAVEDECK_ANDROID_STORE_PASSWORD=$private.StorePassword;$env:WAVEDECK_ANDROID_KEYSTORE=$signingFile
    if (!(Test-Path -LiteralPath $signingFile)) {
        $ErrorActionPreference='Continue'
        & (Join-Path $toolchain.JavaHome 'bin\keytool.exe') -genkeypair -keystore $signingFile -alias wavedeck-development -storepass:env WAVEDECK_ANDROID_STORE_PASSWORD -keypass:env WAVEDECK_ANDROID_STORE_PASSWORD -dname 'CN=WaveDeck Development,O=WaveDeck,C=BR' -keyalg RSA -keysize 3072 -validity 10000 2>&1 | Out-File (Join-Path $logRoot 'signer.log')
        $result=$LASTEXITCODE;$ErrorActionPreference='Stop';if($result-ne 0-or !(Test-Path -LiteralPath $signingFile)){throw 'The development signing key could not be created.'}
    }
    Push-Location $project
    try {
        $ErrorActionPreference='Continue'
        & $toolchain.Gradle --no-daemon :app:assembleRelease :app:testDebugUnitTest :app:lintRelease 2>&1 | Out-File (Join-Path $logRoot 'build.log') -Encoding utf8
        $result=$LASTEXITCODE;$ErrorActionPreference='Stop'
        if($result-ne 0){Get-Content (Join-Path $logRoot 'build.log') -Tail 45;throw 'Android build or required checks failed.'}
    } finally { Pop-Location }
    $apk=Join-Path $project 'app\build\outputs\apk\release\app-release.apk'
    $ErrorActionPreference='Continue'
    & (Join-Path $toolchain.SdkRoot 'build-tools\36.0.0\apksigner.bat') verify --verbose --print-certs $apk 2>&1 | Out-File (Join-Path $logRoot 'signature.txt')
    $result=$LASTEXITCODE;$ErrorActionPreference='Stop';if($result-ne 0){throw 'The generated APK signature could not be verified.'}
    $Output=[IO.Path]::GetFullPath($Output);New-Item -ItemType Directory -Path $Output -Force|Out-Null
    $name="WaveDeck-$version-android.apk";$destination=Join-Path $Output $name
    Copy-Item -LiteralPath $apk -Destination $destination -Force
    Set-Content -LiteralPath (Join-Path $Output 'VERSION.txt') -Value $version -Encoding ASCII
    Set-Content -LiteralPath (Join-Path $Output 'SHA256SUMS.txt') -Value ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant()+'  '+$name) -Encoding ASCII
    $instructions=(Get-Content -LiteralPath (Join-Path $PSScriptRoot 'android\README.md') -Raw).Replace('{VERSION}',$version)
    [IO.File]::WriteAllText((Join-Path $Output 'README.md'),$instructions,(New-Object Text.UTF8Encoding($false)))
    Write-Output "APK: $destination";Write-Output "Version: $version. Package: io.github.lemonior.wavedeck. Signed with the local development key."
} finally { foreach($name in $saved.Keys){[Environment]::SetEnvironmentVariable($name,$saved[$name],'Process')} }
