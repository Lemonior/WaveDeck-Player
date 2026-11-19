# WaveDeck 1.0.1

English and Brazilian Portuguese interface support.

- Added **Language** directly below **Theme** in Settings: **English** and **Português (Brasil)**.
- Saving applies the language immediately and remembers it for the next launch. Cancel leaves the current language unchanged.
- Translated the Windows and Linux controls, settings, menus, tooltips, playback/download messages and account-connection instructions. Song titles, file paths, saved playback options and sound settings retain their original values.
- Google/YouTube connection continues to use the existing external-browser session flow. Official Google OAuth sign-in requires a registered app and does not authenticate yt-dlp playback; this release does not add OAuth or Gmail access.

Download `WaveDeck-1.0.1-win-x64.zip` for Windows or `WaveDeck-1.0.1-linux-x64.zip` for Linux. Extract the complete folder before launching. Both ZIPs include the .NET runtime; Linux requires the dependencies described in its README. ZIP checksums are in `SHA256SUMS.txt`.

Account sign-in/history, audible listening quality, and behavior on other machines require interactive validation. Validation records in `Context/` identify their tested build and environment.
