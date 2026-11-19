# WaveDeck header, channel layout and import validation - 2026-10-04

Delivered Windows x64 version 1.0.0: `D:\MusicPlayer\WaveDeck\WaveDeck.exe`.

- Dragging the maximized header restores the normal window beneath the grabbed cursor position and continues moving it. A stationary click retains maximization. Double-click maximizes/restores. Normal dragging starts its native move loop after crossing the Windows movement threshold.
- Each visible channel receives 87.5% of its former layout share, with a 32-DIP minimum; the mixer receives the extra height. Disabled channels collapse to zero-height rows and their entire space grows the mixer. Normal, minimum and maximized layouts retain transport controls.
- New imports omit known private/deleted/unavailable/unplayable entries. Skipped items still advance source pagination. Go automatically continues past an entirely skipped initial page. Unknown or account-dependent availability remains eligible, with existing playback retry/skip handling if it later fails. Explicit single private-video URLs retain access through the connected account.

Final installed-copy evidence (all processes exited 0):

- `artifacts/layout-import-installed-native/summary.txt`: **63/63 native Windows checks passed**. Includes mixed unavailable metadata, legitimate similarly named tracks, unlisted/account-dependent entries, explicit private URLs, and 10,000 source positions with an entirely private first page. Existing audio, persistence, desktop, chapter and network-fixture regressions pass.
- `artifacts/layout-import-installed-ui-final/ui-results.json`: **4 focused checks passed**. Tests all 8 channel combinations, all 3 themes, normal/minimum/maximized windows, and all 3 mixer tabs. Actual physical Windows mouse input proves stationary clicking, restore-and-drag with retained size/cursor anchor, and both double-click directions on the scaled display.
- `artifacts/layout-import-installed-sound-final/ui-results.json`: **11 live playback/UI checks passed**. Independent tab presets/resets, complete presets, A/B, modeless details, paused seeking, chapter-relative playback, defaults, reactive visualization and unchanged source files remain covered using muted generated audio.
- Installed DLL SHA-256 **C32AB16C10FEBAFEF87D9DFDE2DF876512451192225687F892816C3D320094A4** matches final staging. Product version is 1.0.0.
- Shared Linux Release linux-x64 build passes with zero warnings/errors. The packaged Linux interface was not updated.
- The running user player was preserved; its mapped executable/DLL were retained in `artifacts/before-layout-import-2ae402b840fb4ccfb0d63d11ddb05bc8`. No user player was terminated. Reopening the normal executable loads the update.

OldSchool maximized layouts with all channels and with only Channel C were visually inspected. Final render captures are under `artifacts/layout-import-installed-ui-final`; existing mixer/theme captures are under `artifacts/layout-import-installed-sound-final`. Earlier failed diagnostic attempts remain separate artifacts and are not acceptance evidence. No new live YouTube-playlist access, mixed-monitor DPI acceptance, subjective audio quality or memory benchmark is claimed. The complete background-key desktop suite was not repeated while the user's player retains those registrations.

References consulted: [yt-dlp playlist extractor](https://github.com/yt-dlp/yt-dlp/blob/master/yt_dlp/extractor/youtube/_tab.py), [Microsoft physical cursor coordinates](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setphysicalcursorpos).
