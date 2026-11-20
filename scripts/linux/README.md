# WaveDeck for Linux

Keep this entire folder together. This is a self-contained **Linux x86_64** desktop build; .NET is included. It uses Avalonia and SDL2, with the same C# playlist, URL, downloader, chapter and equalizer code as the Windows edition.

On your Linux machine:

```bash
bash install-dependencies.sh
bash launch.sh
```

After executable permissions are set, open **WaveDeck** directly. Optional: `bash install-shortcut.sh` adds WaveDeck to your applications menu. The shortcut uses this folder's absolute location; rerun it after moving the folder. Paths containing spaces are supported.

FFmpeg/ffprobe and desktop audio/display libraries come from your Linux package manager. Verified official Linux yt-dlp and Deno binaries are included under tools. The dependency helper supports Debian/Ubuntu, Fedora and Arch package managers; historical validation records are in the source repository's `Context/` folder. Requires a glibc distribution and an X11 desktop (or XWayland), with a working audio session. Alpine/musl and ARM require different builds.

## Features

- Local files/folders/drag and drop, generic audio/video URLs, supported yt-dlp sites, streaming, background download and download-and-play.
- Paginated playlists: 500 entries initially, 10,000 by default, configurable up to 50,000. Load next / Load all continue the same URL mode.
- Automatic chapter checks at video starts, a nested song selector, previous/next/shuffle, shared Repeat One, relative song seeking, and actual individually cut audio downloads.
- Real ten-band EQ, presets, default reset, preamp, stereo width, safe gain, perceptual volume, live channels and three textured themes.
- Custom centered two-line header, styled controls/menus/errors, enlarged slider drag areas, ±10 seconds, keyboard play/pause options and optional middle-mouse play/pause.
- WaveDeck JSON and M3U playlists remember nested songs and downloaded file paths. Settings are stored in your Linux application-data folder, normally ~/.local/share/WaveDeck; downloads default to your Music/WaveDeck folder.
- Prepares up to three upcoming songs across chapters and playlist entries. Background preparation failures stay silent; actual playback failures retain normal retry/skip feedback.

## Application updates

WaveDeck 1.0.2 and later check public GitHub Releases quietly on startup. **Settings → Check Updates** checks immediately and offers **Download and install** for a newer stable Linux x64 release. No GitHub sign-in is needed; startup checks can be disabled. Installation verifies the ZIP and every extracted file, waits for active downloads/imports, then briefly restarts automatically and restores the queue, song/chapter, position and pause state. Keep the extracted portable application folder writable and separate from its data profile. Changed originals are backed up inside `.wavedeck-backups`. Updates restore executable permissions and keep system dependencies as installed. Versions 1.0.0/1.0.1 require one manual download of 1.0.2 to gain the updater.

**Stream** starts online playback. **Download** saves without starting it. **Download & play** starts the first completed file/song and keeps downloading the rest. Pending queued tracks can stream independently. A later completion respects newer manual navigation. Pause, seek, volume, EQ and Stop remain available while downloads or audio preparation run.

Google sign-in opens your normal default browser through xdg-open. The explicit Connect button imports its available session, keeps only Google/YouTube domains and deletes the temporary export. Linux session files use owner-only permissions (0600, account directory 0700); they are not encrypted by Windows DPAPI. Use a native Linux filesystem for personal session data. Browser protection, profiles and account restrictions can prevent access; optional history submission is site controlled.

This first Linux edition provides application keyboard/mouse controls. Windows taskbar thumbnail controls do not apply on Linux, and desktop-global MPRIS/media-key integration is not implemented yet. Windows retains its native taskbar/media support. Network or missing metadata can still delay transitions; strictly gapless playback is not guaranteed.

## Validation commands

```bash
./WaveDeck --self-test --data-dir /tmp/wavedeck-test
./WaveDeck --smoke-test --data-dir /tmp/wavedeck-ui-test
```

These use isolated fixtures; native audio tests should use `SDL_AUDIODRIVER=dummy` when a silent test is preferred. Account connection/history are not exercised automatically.
