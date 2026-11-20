# WaveDeck

A C# WPF desktop music player with an early-2000s feel, live audio visualizers and a ten-band equalizer. Runs on Windows 10/11 x64.

Download the Windows or Linux ZIP from [GitHub Releases](https://github.com/Lemonior/WaveDeck-Player/releases). Extract the complete folder before launching.

## Run

Open **WaveDeck\WaveDeck.exe**. Keep the complete WaveDeck folder together. Its .NET runtime and media tools are included; no installation or console window is needed. The development launcher is MusicPlayer\MusicPlayer\bin\Debug\net10.0-windows\WaveDeck.exe.

## Linux

The separate **WaveDeck-Linux** folder contains a self-contained Linux x86_64 build. Copy it to Linux, run `bash install-dependencies.sh` once if dependencies are missing, then `bash launch.sh`. The native executable is **WaveDeck**. Its README explains desktop dependencies, optional application-menu installation and platform differences.

## Memory

Visualizer audio arrays and FFT workspace are reused, themed drawing resources are cached, and idle/minimized refresh is reduced. Runtime memory conservation limits retained managed memory without discarding the network audio buffer or forcing periodic collections. In a matched local playback benchmark, private memory averaged 118.8 MB before and 101.2 MB after; total working set stayed similar. Actual memory varies with the runtime, rendering, playlist, decoder and workload. Full measurements are recorded in Context/1.0.0-validation.md.

## Music and URL modes

Add files, choose a folder or drag music onto the window. MP3, WAV, FLAC, M4A, AAC, OGG, Opus, WMA and AIFF use native codecs or FFmpeg. Direct video files and URLs can play their audio too.

On Windows, **Settings → Choose default file types…** registers WaveDeck in Open with / Default apps and opens Windows' file-type choices. Choose the extensions you want there. Registration is per-user and includes audio, video audio and playlists, plus an Explorer **Play folder in WaveDeck** action. Windows keeps control of the final default choice. If you move the portable folder, use this button again to update its location.

Opening one or multiple files starts the first selected audio file. Later opens forward to the already-running player, adding files without duplicate playlist entries. By default, opening an audio file also discovers audio in the same folder. **Settings** can disable sibling discovery or include subfolders; explicit folder opens always scan that folder.

**Remember my last song and position** is enabled by default. Windows restores the exact track or chapter paused, ready for Play, independently of remembering the whole playlist. It checkpoints every 15 seconds and saves before closing. Explicit file opens take priority over resumed playback.

Paste a YouTube video/playlist, SoundCloud link, another site supported by yt-dlp, or a direct HTTP(S) audio/video URL. Generic pages are examined for playable embedded media; unsupported pages get a direct decoding attempt. Site access, available formats and restrictions determine what can play. Protected/DRM streams are not supported.

- **Stream:** plays online, without saving a permanent music file. A rejected stream can use a temporary playback cache that is removed when its audio source closes.
- **Download:** saves the loaded tracks in the background, without starting playback. Existing playback continues.
- **Download & play:** starts the first successfully saved track, then continues downloading the rest. For a chaptered video, this means its first saved song; it does not wait for the entire album. Playback uses completed local files when available and can stream a queued song while its download continues. A late download completion does not override a newer manual selection or Stop. Unavailable sources are skipped.

Settings controls the destination, MP3/M4A/FLAC/WAV/Opus format, lossy bitrate, seconds between downloads and chapter splitting. Completed files are reused when the source, segment boundaries, folder, format and quality match. Cancel stops imports, preparation and queued downloads while keeping completed files. Pending downloads are not resumed automatically after closing; select remembered entries and choose Download selected or Download all to continue.

Playlists load **500 tracks** initially. **Load next** adds a page; **Load all** continues the same mode to the configured total limit. The default total is **10,000**, configurable up to **50,000**, with page sizes 1–2,000. Download modes queue each new page. An existing streamed playlist can be saved through **List tools → Download all loaded online tracks**, or by selecting rows and choosing **Download selected**.

## Songs inside a video

When a video publishes chapters, WaveDeck treats its sections as an album inside **one main playlist entry**. Every video start checks chapter metadata automatically in the background, including saved and remembered entries. The song selector appears when chapters are found; there is no manual Chapters button. Optional metadata failures leave playback running. Song titles and boundaries come from the video's published metadata; songs cannot be identified reliably when the uploader provides no chapters.

The song selector appears above the playlist. The second sub-control row contains only **Song previous**, **Song next** and **Song shuffle**. Song shuffle is independent of main-playlist shuffle. The existing **Repeat** button is shared: One repeats the selected song, Off stops after the main playlist, and All repeats the main playlist including each video's songs. Seeking and the time display use the selected song's relative time. After the final song, normal playback advances to the next main playlist entry. The header keeps the video title while the now-playing panel shows the individual song.

**Save songs inside chaptered videos as separate audio files** is enabled by default in Settings. Downloads cut the original recording at chapter boundaries and save each titled song in the configured format. They remain nested under their original video entry, with saved file paths and timestamps preserved in WaveDeck JSON and WaveDeck's M3U metadata extension. A downloaded segment plays from its own beginning, without applying the original video offset again. Videos without chapters download as one file. Disable splitting to save the whole recording instead.

## Controls and appearance

**Settings → Language**, directly below Theme, offers **English** and **Português (Brasil)** on Windows and Linux. Save settings to apply the language immediately and remember it for the next launch. Cancel keeps the current language. The interface, menus, tooltips and player messages are translated; media titles, paths, keyboard shortcuts and stored playback options retain their values. Output produced directly by external media tools may stay in its original language.

- **Modern:** textured slate surfaces, brushed controls and amber accents. **OldSchool:** dark stone, double gold frames, warm serif headings and matte controls inspired by the user's current brown RuneScape 3 screenshots. **Retro:** textured violet surfaces, scanlines and neon accents. All artwork is original WPF/vector code. Settings previews themes; Cancel restores the previous one.
- Drag anywhere across a slider's enlarged area to move its knob, including after clicking directly on the rail. Horizontal knobs fit inside their controls. **Default** restores flat EQ, 0 dB preamp, original stereo width, enabled EQ/safe gain and exits DRY; volume stays unchanged. Double-click a band/preamp to reset, width for original stereo, volume for 65%. Right-click the preset selector to save/load presets.
- Volume uses a perceptual curve: 0% is silent, 100% full gain and 50% approximately -10 dB. Loudness also depends on the recording, EQ and speakers.
- **Controls…** configures in-app play/pause on Space, Ctrl+Space or P, plus an optional middle/extra mouse button. On Windows, hardware media commands and a configurable background play/pause shortcut work while another application is focused or WaveDeck is minimized. The background shortcut defaults to **Ctrl+Alt+Space**. Hardware media keys include FN combinations and mouse buttons that send Windows media commands. Background media handling can be disabled; conflicting shortcuts are reported. Text fields retain normal editing. Windows taskbar thumbnail previous/play/pause/next controls are supported.
- **A / B / C** show or hide each live channel. Hidden channels collapse completely and their space expands the mixer. Visible channels use approximately 12.5% less height than the original layout, with that extra height assigned to Equalizer V2; small windows retain the minimum visual/control sizes. Channel C builds its shapes from audio: **Aurora** mirrors the measured frequency contour, **Ribbons** draws the stereo waveform and **Orbit** projects the measured envelope / spectrum into a radial contour. The previous **Stereo phase** view is also available. Shapes have no autonomous animation; identical audio produces identical shapes, silence settles to a line or point, and trails retain actual previous audio frames. Visuals use audio before the volume slider, so they remain responsive at low volume or mute; L/R meters show the actual output level. **Visuals** configures the mode, layered trails and colors driven by the song. Freeze pauses all three visuals, and pausing playback holds the ambient shape.

- **Equalizer V2** has three tabs in the existing console. **Equalizer** retains the ten-band EQ, preamp and stereo width. **Knobs** adds bass, presence, air, drive, compression and balance. **Effects** adds speed, pitch, echo, room/reverb, low-pass sweep and bit crushing. Hover a control for a plain-language explanation and its units. Drag a knob up/down, scroll, or use arrow keys; Shift-drag is finer, double-click / Home resets one knob, and right-click opens exact-value entry.
- **Speed** ranges from 0.5× to 2× and preserves pitch; **Pitch** shifts up/down one octave without changing speed. **Effects → Detail** exposes echo timing/feedback, room decay, filter resonance, effect wet/dry mix and a tape/turntable mode that links speed and pitch. The detail window is modeless so the player remains usable. Larger pitch/tempo changes can produce time-stretch artifacts.
- **Reset / Presets** inside each tab affect that tab independently. Equalizer presets change only its ten bands; its Reset also restores preamp and stereo width. Knobs presets change only tone, drive, compression and balance. Effects presets change only playback effects and their timing/room/filter details; the master wet/dry mix stays unchanged. These controls preserve the other tabs and volume. The **top preset selector** applies complete setups across all three tabs: familiar EQ curves plus clean, bass club, electronic, lo-fi room, clear vocals and slow/spacious profiles. **A/B** compares the complete setup—including speed and pitch—with original playback. **Default** restores every sound control and preserves volume. Right-click the top selector to save/load full sound presets; older EQ-only files still load.
- **Safe gain** reacts to actual output peaks that would clip, with linked stereo protection and smooth recovery. Boosting a knob or equalizer band no longer automatically lowers the entire song. The master volume remains independent. Compression, filtering and balance still deliberately change the signal according to their functions.
- Processing applies live while listening and is remembered with preferences. Seeking resets delay and tempo buffers; chapters keep their own relative timeline. Original files and downloaded chapter segments are unchanged. The packaged Linux interface does not yet include these Windows tabs or Channel C modes.
- **-10 sec / +10 sec** seek within the current track or song while preserving pause. Search filters the main playlist; double-click or Enter plays a row. Context menus and List tools, scrollbars and error dialogs follow the active theme.
- The integrated header shows the video title above WaveDeck, with vector help/window buttons. Maximizing respects the monitor's taskbar work area. Drag the maximized header to restore and move the window under the cursor; a click leaves it maximized and double-click toggles maximization.

Playlist imports omit entries identified as private, deleted or unplayable, and show the skipped count. Pagination still advances through original source positions, including completely unavailable pages, so skipping entries does not shorten large imports. If the first page contains no usable tracks, Go continues until it finds one or reaches the configured limit. Unknown or account-dependent availability is retained; playback still retries/skips videos that later prove inaccessible. Explicit individual private-video URLs can still use the connected account's permissions.

WaveDeck preloads up to the next three songs as soon as playback is ready, including chapters and the following playlist entries. It retains the planned shuffle order, limits preparation to three sources and releases unused sources when the queue or playback mode changes. Background preload failures are silent; error feedback occurs only when actually trying to play a failed song. Current audio keeps playing while a replacement prepares. Pause/resume, seeking, volume/EQ, repeat/shuffle and Stop remain available during downloads and preparation. Download progress is coalesced before reaching the UI; seeking and chapter reading use independent cancellation and cannot resurrect a stopped or replaced source. Playback errors produce a brief ding and status message, retry with fresh resolution, then skip to another playable song/track without a blocking dialog. This reduces interruptions; network failures and unavailable streams can still cause delays, and output is not guaranteed gapless. Import, preferences and other non-playback errors use themed dialogs.

## Application updates

Starting with **1.0.2**, WaveDeck checks the latest stable release on the public [GitHub repository](https://github.com/Lemonior/WaveDeck-Player/releases) when it opens. No GitHub account or token is needed. Startup checks run quietly and show **Update available** when a newer Windows/Linux x64 ZIP is ready. Disable these checks in Settings if preferred. **Settings → Check Updates** checks immediately and shows the release notes and **Download and install**.

Installation verifies the ZIP against the release SHA-256 checksums and checks every extracted file against its manifest. It waits for active downloads/imports and saves settings, queue, current song, playback position and pause state. Compiled player code requires a brief automatic restart; WaveDeck reopens itself and resumes the saved state, even if normal playlist/session remembering is disabled. Downloading or canceling the update leaves the player running. Updates require a writable, extracted portable release folder; development builds and older folders without an update manifest offer the release page instead.

Only files listed in the application manifests are replaced or removed. Previous changed files are kept in `.wavedeck-backups` inside the application folder, and ordinary installation failures trigger rollback. Music, account sessions and other profile data stay in their existing locations. Keep the application folder writable and separate from its data profile; symbolic-link installations are not supported. Linux updates restore executable permissions but do not install system dependencies. **Versions 1.0.0/1.0.1 need a one-time manual download of 1.0.2**, because those builds have no app updater.

## Google connection and watch history

**Settings → Sign in with Google** opens a normal HTTPS link through Windows, using your default browser. WaveDeck does not launch a named browser or replace your default. Finish signing in and open YouTube in the browser's main profile, then click **Connect to YouTube** in WaveDeck.

Connect explicitly imports an available session from a supported default-browser connector. It can fail when the browser protects or locks its session, uses a different profile, or is unsupported. There are now two Windows alternatives:

- **Separate sign-in…** opens a fresh, temporary profile in a compatible installed browser, using the default if compatible. Sign in there, open YouTube and click Connect. This reads only that WaveDeck-owned profile through its loopback browser endpoint, then closes and removes that temporary profile. Some Google policies can still reject the separate window.
- **Import session file…** reads a Netscape-format .txt export made inside your signed-in browser. **How to export** opens the [media tool's official instructions](https://github.com/yt-dlp/yt-dlp/wiki/FAQ#how-do-i-pass-cookies-to-yt-dlp). This works independently of protected browser databases. WaveDeck rejects malformed, expired and unsigned exports, discards unrelated sites, and encrypts the saved Google/YouTube session using Windows DPAPI. The selected source file remains yours to delete after connecting.

Browser security settings do not need to be disabled. Default-browser connection leaves that browser open; its temporary export is filtered and deleted. Disconnect deletes WaveDeck's stored session. Connection/disconnection take effect immediately. Real Google login and restricted-video access require an interactive account check.

This uses a browser session: [yt-dlp no longer supports YouTube OAuth](https://github.com/yt-dlp/yt-dlp/wiki/Extractors#logging-in-with-oauth). Restricted videos still require access on the account; YouTube can expire or reject sessions. Public videos usually need no connection. **Update YouTube tool** updates yt-dlp. Spotify integration is removed.

The app-style Google consent screen seen in other desktop apps is [Google OAuth](https://developers.google.com/identity/protocols/oauth2/native-app). It requires a registered Google Cloud desktop client and returns API authorization, which yt-dlp cannot use for YouTube media playback. WaveDeck currently uses the browser-session connection above; it does not request access to Gmail.

Optional **Add played YouTube videos to my account's watch history** submits a watch request after actual playback begins, including continued playback across a video's songs. Import, prefetch and download alone do not mark videos watched. YouTube controls whether this is accepted, and the account's history must be enabled. It records the parent video, not separate chapter songs. Real account access/history needs an interactive check.

## Shortcuts

| Key | Action |
| --- | --- |
| Space (configurable) | Play/pause |
| Ctrl+Alt+Space (Windows, configurable) | Play/pause while another app is active |
| Ctrl+Left / Ctrl+Right | Back / forward 10 seconds |
| Alt+Left / Alt+Right | Previous / next main playlist entry |
| Media keys | Play/pause, stop, previous, next |
| Ctrl+O | Add files |
| Ctrl+L | Focus URL |
| Ctrl+F | Search playlist |
| Ctrl+M | Mute |
| Delete / Enter in playlist | Remove / play selection |

Preferences, queue, playback checkpoint, connection and error log live in %LOCALAPPDATA%\WaveDeck. Downloads default to the Windows Music folder under WaveDeck. Playback restores paused on normal launch; explicit file opens start playing. Live streams without a known duration cannot be scrubbed. These new desktop integrations are implemented in Windows; the existing Linux build is unchanged.

## Build and validate

The application version lives in **`Directory.Build.props`** at the repository root. Change its single `<Version>1.0.0</Version>` value to version both applications, their assembly metadata, About displays and release ZIPs. New projects under this root inherit it too. [.NET imports this file automatically](https://learn.microsoft.com/en-us/visualstudio/msbuild/customize-by-directory).

Requires .NET 10 SDK on Windows.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Get-AudioTools.ps1
powershell -NoProfile -STA -ExecutionPolicy Bypass -File .\scripts\New-Icon.ps1
dotnet build .\MusicPlayer\MusicPlayer.slnx
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Publish.ps1
```

Editable SVG/PNG/ICO assets are in MusicPlayer\MusicPlayer\Assets. Native checks use isolated data directories:

```powershell
Start-Process .\WaveDeck\WaveDeck.exe -WindowStyle Hidden -ArgumentList '--self-test --data-dir D:\MusicPlayer\artifacts\portable-test' -Wait
Start-Process .\WaveDeck\WaveDeck.exe -WindowStyle Hidden -ArgumentList '--smoke-test --data-dir D:\MusicPlayer\artifacts\portable-ui' -Wait
Start-Process .\WaveDeck\WaveDeck.exe -WindowStyle Hidden -ArgumentList '--online-test --data-dir D:\MusicPlayer\artifacts\online-test' -Wait
```

Self-tests cover DSP, decoding, retry/cancellation, pagination, session filtering, direct/generic media, chapter boundaries and real segment downloads, nested playlist persistence and first-song download/play sequencing. WPF checks cover themes, geometry, menus, media/taskbar commands, chapter modes and natural handoff, and a virtualized 10,000-entry queue. Public online checks cover YouTube and SoundCloud. Listening quality, physical mouse gesture feel and real sign-in/restricted-media/history are interactive checks. Historical validation evidence is in `Context/`; each record identifies the build and environment it tested.

## Package and publish a release

On Windows with the .NET 10 SDK, build both portable ZIPs:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Package-Release.ps1
```

For version `1.0.0`, the output is `artifacts/releases/1.0.0/WaveDeck-1.0.0-win-x64.zip`, `WaveDeck-1.0.0-linux-x64.zip`, and `SHA256SUMS.txt`. Pass `-Platforms Windows` or `-Platforms Linux` to package one platform. Packaging uses isolated staging folders and does not replace a running player's files. Existing ZIPs are protected against accidental replacement; use `-Output <another-directory>` to rebuild the same version locally.

For each release, update the central version and `RELEASE-NOTES.md`, validate the changes, commit, and push `main`. Create and push the matching annotated tag (replace `1.0.0` with the central version):

```powershell
git tag -a v1.0.0 -m "WaveDeck 1.0.0"
git push origin v1.0.0
```

The [Release workflow](.github/workflows/release.yml) verifies that the tag matches the central version, builds both ZIPs, and publishes them with checksums through [GitHub Releases](https://cli.github.com/manual/gh_release_create). It can also be run manually against an existing version tag. Review the Actions run before announcing the release.

Generated builds (`WaveDeck/`, `WaveDeck-Linux/`, and future `WaveDeck-Android/`), downloaded tools, `bin/`, `obj/`, archives and `artifacts/` stay out of Git. Release ZIPs belong in GitHub Releases. `AGENTS.md` contains local working instructions and is intentionally ignored. Android has no build or release package yet.
