param([string]$JavaHome = $env:JAVA_HOME, [switch]$InstallEmulator)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$toolsRoot = Join-Path $repoRoot 'tools'
$artifactRoot = Join-Path $repoRoot 'artifacts\android-toolchain'
New-Item -ItemType Directory -Path $toolsRoot,$artifactRoot -Force | Out-Null
if (!$JavaHome -or !(Test-Path -LiteralPath (Join-Path $JavaHome 'bin\javac.exe'))) {
    $JavaHome = @('C:\Program Files\Java\jdk-21','C:\Program Files\Java\jdk-17') | Where-Object { Test-Path -LiteralPath (Join-Path $_ 'bin\javac.exe') } | Select-Object -First 1
}
if (!$JavaHome) { throw 'Install JDK 17 or 21, then pass its directory with -JavaHome.' }
$sdkRoot = Join-Path $toolsRoot 'android-sdk'
$gradleRoot = Join-Path $toolsRoot 'gradle-8.13'
Add-Type -AssemblyName System.IO.Compression.FileSystem
function Get-VerifiedZip([string]$Url,[string]$Destination,[string]$Sha256) {
    if (!(Test-Path -LiteralPath $Destination) -or (Get-FileHash -LiteralPath $Destination -Algorithm SHA256).Hash -ne $Sha256) {
        $partial = $Destination + '.' + [guid]::NewGuid().ToString('N') + '.partial'
        Invoke-WebRequest -UseBasicParsing -Uri $Url -OutFile $partial
        if ((Get-FileHash -LiteralPath $partial -Algorithm SHA256).Hash -ne $Sha256) { throw "Checksum failed for $Url" }
        Move-Item -LiteralPath $partial -Destination $Destination -Force
    }
}
if (!(Test-Path -LiteralPath (Join-Path $sdkRoot 'cmdline-tools\latest\bin\android.exe'))) {
    $archive = Join-Path $artifactRoot 'commandlinetools-win-16111833.zip'
    Get-VerifiedZip 'https://dl.google.com/android/repository/commandlinetools-win-16111833_latest.zip' $archive 'e5885e2e59038c0778a85fc19f01de7ce7c567c105553f7617b936b1e0e87b2b'
    $temporary = Join-Path $artifactRoot ([guid]::NewGuid().ToString('N'))
    [IO.Compression.ZipFile]::ExtractToDirectory($archive,$temporary)
    New-Item -ItemType Directory -Path (Join-Path $sdkRoot 'cmdline-tools') -Force | Out-Null
    Move-Item -LiteralPath (Join-Path $temporary 'cmdline-tools') -Destination (Join-Path $sdkRoot 'cmdline-tools\latest')
}
if (!(Test-Path -LiteralPath (Join-Path $gradleRoot 'bin\gradle.bat'))) {
    $archive = Join-Path $artifactRoot 'gradle-8.13-bin.zip'
    Get-VerifiedZip 'https://services.gradle.org/distributions/gradle-8.13-bin.zip' $archive '20f1b1176237254a6fc204d8434196fa11a4cfb387567519c61556e8710aed78'
    [IO.Compression.ZipFile]::ExtractToDirectory($archive,$toolsRoot)
}
$previousJava = $env:JAVA_HOME; $previousSdk = $env:ANDROID_HOME; $previousUser = $env:ANDROID_USER_HOME
try {
    $env:JAVA_HOME = $JavaHome; $env:ANDROID_HOME = $sdkRoot; $env:ANDROID_USER_HOME = Join-Path $toolsRoot 'android-user'
    New-Item -ItemType Directory -Path $env:ANDROID_USER_HOME -Force | Out-Null
    $packages = @{'platform-tools'='platform-tools\adb.exe';'platforms/android-36'='platforms\android-36\android.jar';'build-tools/36.0.0'='build-tools\36.0.0\apksigner.bat'}
    if ($InstallEmulator) { $packages['emulator']='emulator\emulator.exe'; $packages['system-images/android-36/google_apis/x86_64']='system-images\android-36\google_apis\x86_64\system.img' }
    foreach ($package in $packages.Keys) {
        if (Test-Path -LiteralPath (Join-Path $sdkRoot $packages[$package])) { continue }
        $log = Join-Path $artifactRoot ($package.Replace('/','-')+'.log')
        $previousErrorPreference = $ErrorActionPreference; $ErrorActionPreference='Continue'
        & (Join-Path $sdkRoot 'cmdline-tools\latest\bin\android.exe') sdk install $package 2>&1 | Out-File -LiteralPath $log -Encoding utf8
        $result=$LASTEXITCODE; $ErrorActionPreference=$previousErrorPreference
        if ($result -ne 0 -or !(Test-Path -LiteralPath (Join-Path $sdkRoot $packages[$package]))) { throw "Android SDK installation failed for $package. See $log" }
    }
} finally { $env:JAVA_HOME=$previousJava; $env:ANDROID_HOME=$previousSdk; $env:ANDROID_USER_HOME=$previousUser }
[pscustomobject]@{ JavaHome=$JavaHome; SdkRoot=$sdkRoot; Gradle=(Join-Path $gradleRoot 'bin\gradle.bat'); GradleHome=(Join-Path $toolsRoot 'gradle-user') }
