# WaveDeck validation — 2026-10-03

Historical validation for the initial 1.0 build. See [v1.1-validation.md](v1.1-validation.md) for the current delivered version.

## Delivered application

- Portable launcher: `D:\MusicPlayer\WaveDeck\WaveDeck.exe`.
- Development launcher: `D:\MusicPlayer\MusicPlayer\MusicPlayer\bin\Debug\net10.0-windows\WaveDeck.exe`.
- Editable solution: `D:\MusicPlayer\MusicPlayer\MusicPlayer.slnx`.
- Runtime: self-contained Windows x64, .NET 10.0.12. The running portable process loaded `D:\MusicPlayer\WaveDeck\coreclr.dll`, confirming it uses its bundled runtime.
- Branding: editable SVG, 256px PNG and ICO containing 16/32/48/256px frames, in `MusicPlayer/MusicPlayer/Assets`.

## Results

| Check group | Result | Evidence |
| --- | --- | --- |
| Native Windows build | Pass, zero warnings/errors | `dotnet build MusicPlayer/MusicPlayer.slnx --no-restore` |
| Release portable publish | Pass | `scripts/Publish.ps1` |
| Audio, DSP, persistence, process and queue tests | 20/20 passed | `artifacts/portable-test/results.json` |
| WPF control and layout checks | 12/12 passed | `artifacts/portable-ui/ui-results.json` |
| Live YouTube integration | 5/5 passed | `artifacts/portable-online/online-results.json` |
| Bundled runtime origin | Confirmed | `artifacts/portable-test/environment.json` |

Audio checks verify flat response, a measured +6 dB change at 1 kHz, frequency selectivity, bypass, output bounds, mono stereo-width setting, mute, FFT response, JSON/M3U persistence, relative playlist paths, corrupted settings recovery, URL validation, native helper startup, decoded duration/seek/EOF, decoder errors/cancellation, Windows playback state, FLAC metadata/playback, shuffle exhaustion/repeat-all, and sequential queue completion.

UI checks exercise local imports, playlist filtering, preset and custom bands, A/B bypass, pause/seek/resume, minimum-size layout, settings rendering, stop, repeat/shuffle controls, URL modes, automatic advancement on real audio EOF, and stopping after the final track with repeat off.

Live checks used the public 19-second video `https://www.youtube.com/watch?v=jNQXAC9IVRw` and imported two titles from `https://www.youtube.com/playlist?list=PLzH6n4zXuckpfMu_4Ff8E7Z1behQks5ba`. Video import, limited playlist import, actual streamed PCM, MP3 download to a folder containing spaces, native Windows streaming playback, and seeking while paused all passed. Tests use the same URL service and audio engine as the UI.

Rendered previews are in `artifacts/portable-ui/`: `empty-player.png`, `playing-player.png`, `minimum-player.png`, and `settings.png`. The preview visualizers use samples decoded from an actual generated WAV; device output was muted during tests.

## Tested environment

Windows build 10.0.26200, native x64. NAudio 2.2.1, FFmpeg 9.0.2 essentials, yt-dlp 2026.08.19, Deno 2.9.7. Windows 10 compatibility follows the target frameworks and dependencies; the live execution was on this workstation.

## Practical limits

Audible listening quality and every keyboard/mouse interaction require manual listening/use. The automated output-device checks were silent. Account-restricted, age-restricted, private, geographically unavailable, and live YouTube media were not validated. YouTube extraction may change; Settings provides tool updates and an optional user-selected cookies file. Native Spotify authentication, protected PCM processing and downloads are not implemented; Spotify links open externally in the official service.

## Dependency provenance

yt-dlp and Deno came from their official GitHub releases; FFmpeg came from the Windows essentials provider linked by the FFmpeg project. The bootstrap script verifies published checksums. Licenses/notices accompany the portable copy in its `tools` folder.

| File | SHA-256 |
| --- | --- |
| tools/yt-dlp.exe | `66674953FE251B89F4D08C5F0E35E0728679BD67AB3D7D05C0562AF101DD3E7A` |
| tools/ffmpeg.exe | `3256173F3F8BFFD7DF12227C68ADF68025EDB1832273A9530688A7BB1ED8EDEC` |
| tools/ffprobe.exe | `F0D36ECBBDD3BCFAC3EFA078C96C7271C2E68B3810595552AC3B7F17E9A65C52` |
| tools/deno.exe | `E020F3E232BD16E33768DEE528E5983349C962952051CED0A5D58AD42F5D9B33` |

Third-party references: [NAudio](https://github.com/naudio/NAudio), [yt-dlp usage](https://github.com/yt-dlp/yt-dlp), [yt-dlp JavaScript runtime setup](https://github.com/yt-dlp/yt-dlp/wiki/EJS), [FFmpeg downloads](https://ffmpeg.org/download.html), [Deno releases](https://github.com/denoland/deno/releases), and [Spotify playback policy](https://developer.spotify.com/documentation/web-api/reference/get-track).
