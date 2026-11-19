# Channel C reaction fix - 2026-10-04

Reported: Channel C looks static instead of responding to a song. Version remains 1.0.0. Windows package destination: `WaveDeck/WaveDeck.exe`.

The old ambient modes used slow elapsed-time curves and a small amplitude response driven by peaks after the master volume. At low listening volume, shape changes were barely visible. It also advanced its animation inside OnRender, tying animation state to incidental redraws.

Implemented:

- Capture the visual signal after EQ and stereo width but before master volume, using the existing reusable analyzer buffers. Capture L/R peaks separately from the actual output. Output volume smoothing and sample protection retain their previous behavior. No second FFT, decoder or large audio buffer is added.
- Drive Aurora, Ribbons and Orbit shapes from RMS energy, bass/midrange/treble levels, envelope rises and sampled waveform detail. Time, color and shape changes advance only on active audio frames. Normal redraws, pause and Freeze hold the ambient state; the Off switch remains independent.
- The Windows player supplies frames from its existing live refresh timer. The shared audio change also compiles for Linux; the packaged Linux interface has not gained the Windows ambient modes.

Validation:

- Native Windows audio checks: `artifacts/channel-c-native/summary.txt`, 50/50 passed. Added regression verifies identical visual samples/FFT at full volume, 8 percent volume and mute, correct output meters and unchanged output samples. Existing EQ, decoder, chapters, playlist and desktop regression checks also pass.
- Targeted native WPF checks: `artifacts/channel-c-ui/ui-results.json`, four checks passed. Each of the three ambient styles is rendered at successive frames, different energy levels and different frequencies at equal amplitude, checking substantial changed shape pixels rather than only a nonempty image. Redraw, pause and Off remain stationary. Actual muted local playback feeds Channel C through the live timer, its output meters stay at zero and Freeze holds the snapshot.
- Rendered Ribbons successive frames and Orbit screenshot were visually inspected. Screenshots are under `artifacts/channel-c-ui/reactive-*.png`.
- Shared Linux Release linux-x64 compile passed with zero warnings/errors. No Linux runtime or new memory benchmark is claimed for this update.

Tests use generated tones and isolated profiles. No real account session is read or changed. Only non-sensitive current visual preferences and volume were inspected to confirm the low-volume case. A running user's player is left running during package replacement; restart the player to load the new assembly.
