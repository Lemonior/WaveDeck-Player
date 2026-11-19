# WaveDeck preset controls and safe gain validation - 2026-10-04

Delivered Windows x64 version 1.0.0 at `D:\MusicPlayer\WaveDeck\WaveDeck.exe`.

Implemented the user's independent preset scopes and single-row header. All three tabs have Reset and Presets buttons. The former Recipes label and EQ V2 checkbox are removed. Only the top presets replace the complete sound setup. Local presets/reset preserve the other tabs, master volume and master sound mix. The final output limiter protects actual peaks without preemptively attenuating boosted knobs/bands.

Evidence:

- `artifacts/preset-controls-native-final/summary.txt`: **61/61 checks passed**, native Windows .NET 10.0.12, exit 0. Includes the previous 58 regression checks plus non-clipping tone/EQ boost transparency at normal and quiet volume, intended boosted frequency levels, stereo-linked overload protection/recovery, and exact isolation of tone/effects preset/reset fields.
- `artifacts/preset-controls-ui-final/ui-results.json`: **11 focused integration checks passed**, exit 0. Actual WPF Reset button and Presets menu dispatch proves all three scopes work independently during muted Windows audio playback. Global Electronic/Flat presets replace all tabs; A/B, full saved presets, paused seeks, chapter-relative processing, defaults, visualizer output and unchanged original audio remain covered.
- One-row header alignment, visible/unclipped local buttons and knobs, every theme, and minimum/maximized transport geometry pass. OldSchool Equalizer/Knobs and Modern Effects/Retro Knobs renders were visually inspected. PNGs are in `artifacts/preset-controls-ui-final`.
- Installed build: `artifacts/preset-controls-installed-ui/ui-results.json`: **11 checks passed**, exit 0. Published DLL SHA-256 `FC7C1430B304D978C4E11AFDB65F91291D91444A93292F5D49C24FCA08081169` equals the tested staging DLL. Executable product version is 1.0.0.
- Shared Linux project Release linux-x64 compiles with zero warnings/errors. Its packaged interface was not changed and no Linux runtime test is claimed.
- The user's running player was retained. Its mapped application executable and DLL were moved into `artifacts/before-preset-controls-4182e599bb1649dc8bed546bb0b9cedb` before publication; the process was not terminated. Reopening the normal executable starts the update.

Tests use isolated settings and generated audio. Subjective loudness/music quality still needs listening with the user's recordings; compressor, filter and pan controls intentionally alter level/timbre. The full background-media-key desktop integration suite was not repeated because the running user player retains those keys. No live network-service acceptance or new RAM benchmark is claimed.
