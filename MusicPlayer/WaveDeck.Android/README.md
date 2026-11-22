# WaveDeck Android

Native Java 17 Android client for WaveDeck 1.1.0. The version comes from the root `Directory.Build.props`; Gradle computes `versionCode` as `major * 1000000 + minor * 1000 + patch`. Minimum Android 8 / API 26, compile and target Android 16 / API 36. The APK contains Java/Dex code with no bundled ABI-specific native libraries and works on ARM64, ARMv7 and x86_64 devices with supported system decoders.

## Build on Windows

From the repository root, with JDK 17 or 21 installed:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Publish-Android.ps1 -JavaHome 'C:\Program Files\Java\jdk-21'
```

`Get-AndroidToolchain.ps1` installs checksum-verified Google command-line tools and Gradle 8.13 into ignored workspace folders, then installs platform 36 and build tools 36.0.0. Gradle uses Android Gradle Plugin 8.13.2 and pinned dependencies from Google Maven, Maven Central and JitPack; `app/gradle.lockfile` records resolved transitive versions. Initial builds need internet access. Nothing installs on a connected phone automatically.

`Publish-Android.ps1` assembles a release APK, runs unit tests and release lint, verifies its signature, then creates `WaveDeck-Android/WaveDeck-<version>-android.apk` with `VERSION.txt`, `SHA256SUMS.txt` and installation instructions. Build logs live under `artifacts/android-build/`. Both debug and release variants use a persistent local development signer. Preserve `tools/android-signing/wavedeck-development.jks` and its private password file together; never commit them. Replacing the key would prevent updating the existing package without uninstalling it.

For Android Studio, open this directory and configure the SDK/JDK and the signing environment variables used in `app/build.gradle`. The PowerShell script handles these variables automatically for command-line builds.

## Runtime

- `PlaybackService` owns ExoPlayer and MediaSession, audio focus, a network wake lock and the media foreground notification. Closing the activity does not stop audio. Android's media notification, lock screen and headset transport commands use the same session. Wired headset-hook gestures support one click to toggle, two for next and three for previous; many Bluetooth earbuds send the corresponding standard media key directly.
- `StateStore` saves an atomic private queue/position checkpoint. Normal app launch restores paused; explicit media-button playback can resume. Force stop and reboot stop playback.
- `MediaRuntime` provides a bounded 256 MiB stream cache and separate persistent offline audio. `WaveDownloadService` owns foreground downloads. Download & play waits for completion; manual navigation cancels the pending automatic switch. Downloads preserve encoded source audio and do not transcode or export files.
- `Sources` and `ExtractorDownloader` resolve anonymous public YouTube audio using NewPipe Extractor v0.27.1, pinned to commit `6ce85c1af05bbb854bae73f8936fd9f75bb8f9e1`. Direct HTTP(S) audio also works. Playlist pagination and individual-video chapter expansion are supported. Background preparation warms up to three upcoming online sources and silently discards preparation errors.
- `FileLibrary` uses Android's Storage Access Framework. Selected documents/folders retain read grants. Externally shared audio with transient access is preserved in private imported storage so background playback can continue after the sender closes. No original files are modified. Local codecs depend on the phone; the APK does not include FFmpeg.
- `DeckAudioProcessor` / `DspEngine` implement ten-band EQ, preamp/width, tone, drive/compression/balance, echo/reverb/filter/crush and a limiter. Media3 handles independent tempo/pitch and tape behavior. Original Android presets and playlists can be saved through the system picker; desktop playlists retain usable online URLs, while desktop file paths need selecting again.
- `MainActivity` provides a phone layout, queue/search, source modes and collapsible sound controls. English and Brazilian Portuguese resources live in `res/values` and `res/values-pt-rBR`. Language is immediately below Theme; Save applies and Cancel discards changes.
- `AndroidUpdates` checks the public GitHub Releases API. Startup checks only give a quiet availability notice. Manual checks can offer a future versioned APK with a verified GitHub digest, matching package and signer. Android's installer requires user confirmation; it cannot silently replace the running application. No APK is published by the current desktop workflow.

This build does not include desktop Google cookies/sign-in/history, desktop visualizers, OS-wide desktop hotkeys or desktop conversion formats. Anonymous extraction may be rejected by YouTube for restricted videos or particular networks. Live listening, physical audio routes and vendor battery-management behavior require development-phone validation.

## Verification

Nine JVM unit checks cover source URLs, versions and DSP behavior. Eight Android instrumentation checks cover screen-off transitions with the activity closed, platform media transport and headset gestures, Play after Stop/end-of-track, quiet failed preparation, foreground retry/skip, offline playback after the server closes, download-completion sequencing/cancellation, language Save/Cancel, and real YouTube decoding with the screen off.

After `Publish-Android.ps1`, an isolated emulator can run the instrumentation checks. Select its exact ADB serial using `ANDROID_SERIAL`; Gradle's test runner installs and removes its app/test APKs on that emulator. Set `JAVA_HOME`, `ANDROID_HOME`, `ANDROID_USER_HOME`, `GRADLE_USER_HOME`, `WAVEDECK_ANDROID_KEYSTORE` and `WAVEDECK_ANDROID_STORE_PASSWORD` as the packaging script does, then run from this directory:

```powershell
& ..\..\tools\gradle-8.13\bin\gradle.bat --no-daemon :app:connectedDebugAndroidTest
```

`:app:youtubeProbe` is an explicit host network probe; it resolves a real public YouTube video and reads audio bytes. Ordinary JVM checks do not need YouTube access. Instrumentation fixtures and development audio do not enter the release APK. See the root `Context/1.1.0-android-validation.md` for actual results and phone checks.

## Source and notices

Original WaveDeck source retains its MIT license. The Android APK combines it with NewPipe Extractor (GPL-3.0-or-later), so the combined Android distribution is GPL-3.0-or-later. This does not change the desktop editions. NewPipe is unmodified; its exact source is available at [the pinned upstream commit](https://github.com/TeamNewPipe/NewPipeExtractor/tree/6ce85c1af05bbb854bae73f8936fd9f75bb8f9e1). Dependencies are pinned in `app/build.gradle` and resolved library notices are copied into APK assets at build time. Settings → Licenses displays the attribution notice; full license texts are under `app/src/main/assets/licenses/` in source and `assets/licenses/` inside the APK.

For binary publication, provide the complete corresponding WaveDeck Android source at the exact same revision, these build scripts/configuration, and the pinned extractor/dependency sources alongside it. `scripts/Package-AndroidSource.ps1 -Revision <release-tag>` creates that source bundle with the exact upstream extractor source. Development signing credentials are private and excluded. The user accepted the Android build and authorized publishing 1.1.0 on November 22, 2026; later changes still require appropriate validation before release.
