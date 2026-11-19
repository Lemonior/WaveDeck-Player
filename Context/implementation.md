# WaveDeck implementation context

This describes the initial 1.0 build. Current follow-up work is recorded in [v1.1-execution.md](v1.1-execution.md) and [v1.1-validation.md](v1.1-validation.md).

Build the existing C# WPF solution in `MusicPlayer/MusicPlayer.slnx` as a native Windows .NET 10 application. The launch executable must be `WaveDeck.exe`, with no `.App` suffix.

## Accepted scope

- Dark, beveled Windows XP-era player inspired by the supplied reference, with amber/cyan accents.
- Real sample-driven waveform, frequency spectrum and stereo phase channels; L/R peak meters.
- A working ten-band graphic equalizer below the stereo meters and above volume, with presets, preamp, stereo width, bypass comparison and clipping protection.
- Local audio files/folders, queue, search, shuffle, repeat, seeking, drag/drop, saved playlists and persistent preferences.
- A URL field with Stream, Download, and Download & play modes for YouTube videos/playlists and direct HTTP audio.
- Spotify links open in the official Spotify player. Native Spotify PCM processing/downloading is outside this version; the official APIs require authentication/Premium and prohibit downloading/altering content.
- Portable Windows folder distribution with bundled .NET runtime, yt-dlp, FFmpeg and Deno; editable SVG, PNG and ICO branding.

## Execution stages

1. Audio engine, URL resolver, persistence and dependency bootstrap.
2. WPF shell, controls, actual audio visualization, equalizer and settings.
3. Native build, audio/DSP/playlist/process checks, launch/UI inspection and portable publish.

## Completion

All three stages are implemented. The portable launcher is `D:\MusicPlayer\WaveDeck\WaveDeck.exe`; the development launcher is `MusicPlayer\MusicPlayer\bin\Debug\net10.0-windows\WaveDeck.exe`. See `validation.md` for the completed native, UI and live YouTube checks and their limits. Usage and rebuild instructions are in the root `README.md`.

## Validation rule

Report native/scripted checks separately from listening and third-party network behavior. YouTube can require authentication or change upstream; preserve visible errors and a dependency refresh action.
