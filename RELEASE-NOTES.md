# WaveDeck 1.0.2

Automatic release checks, installation from Settings, and smoother autoplay.

- WaveDeck checks public GitHub Releases quietly when it opens and shows **Update available** for a newer stable release. No GitHub sign-in is needed. Startup checks can be disabled in Settings.
- **Settings → Check Updates** checks immediately, shows release notes and offers **Download and install**. Downloads and extracted files are verified with SHA-256 before installation.
- Installation waits for active downloads/imports, saves settings and playback state, then automatically restarts and resumes the queue, song/chapter and position. Loaded player code needs this brief restart; you do not need to reopen the app yourself. Previous changed files are backed up, with rollback on ordinary installation failures.
- Preloads up to the next **three songs** during playback, across chapters and playlist entries, retaining shuffle order. Background preload failures are silent. Normal retry/skip notification sounds remain when actually trying to play an unavailable song.
- Update controls and messages support English and Português (Brasil).

Download `WaveDeck-1.0.2-win-x64.zip` for Windows or `WaveDeck-1.0.2-linux-x64.zip` for Linux. Extract the complete folder before launching. Both ZIPs include the .NET runtime; Linux requires the dependencies described in its README. ZIP checksums are in `SHA256SUMS.txt`.

**One-time installation:** 1.0.0 and 1.0.1 do not contain the app updater. Download and extract 1.0.2 manually once; subsequent compatible releases can install from the app. Installation requires a writable portable release folder separate from the data profile. Development builds and older folders without `update-manifest.json` open the release page instead.

Preloading reduces preparation delays; network playback is not guaranteed gapless. Windows checks use controlled fixtures; native Linux desktop behavior, account sign-in/history, real restricted streams and subjective listening quality need interactive validation. See `Context/1.0.2-validation.md` for the validation scope.
