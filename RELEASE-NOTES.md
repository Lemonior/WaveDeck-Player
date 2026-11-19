# WaveDeck 1.0.0

First GitHub release of WaveDeck, a portable desktop music player.

- Windows x64: WPF player, live visualizers, equalizer/effects, playlists, URL streaming and downloads, chapter playback, and desktop/media controls.
- Linux x64: separate Avalonia player with shared playlist, URL, downloader, chapter and equalizer code. The latest Windows effects tabs and Channel C modes are not included in the Linux interface.
- Both ZIPs include the .NET runtime and bundled tool licenses. Linux still requires FFmpeg and desktop/audio dependencies; run `bash install-dependencies.sh`, then `bash launch.sh`.

Download `WaveDeck-1.0.0-win-x64.zip` for Windows or `WaveDeck-1.0.0-linux-x64.zip` for Linux. Extract the complete folder before launching. `SHA256SUMS.txt` contains the ZIP checksums. Android is planned; no Android build is included.

Account sign-in/history, audible listening quality, and behavior on other machines require interactive validation. Earlier validation records are in `Context/`; they describe the builds and environments recorded there.
