# WaveDeck independent presets and output gain - 2026-10-04

User requirements: fix the volume reduction when tone knobs change; rename Recipes to Presets; provide Reset and Presets on Knobs and Effects; remove the EQ V2 enable checkbox and align tabs with the preset/A-B/Default header. Follow-up: all tab presets must be independent, and only the top presets can replace the complete setup.

Implementation:

- One header row contains Equalizer, Knobs, Effects, the complete preset selector, A/B and Default. A/B remains the control for temporarily bypassing the entire setup.
- Equalizer, Knobs and Effects each have scoped Reset and Presets buttons. Local EQ presets replace only the ten bands; local EQ Reset also resets preamp and width. Tone presets/reset change only the six tone controls. Effects presets/reset change only speed, pitch, tape mode, echo/timing/feedback, reverb/room, filter/resonance and crush. Local controls preserve the other tabs, volume, master mix and A/B state.
- The top selector merges the familiar EQ profiles and complete sound profiles, without duplicate names. These selections reset preamp/width and replace both tone and effects along with the EQ curve. Custom accurately indicates a subsequently edited setup. Top Default and saved full sound presets retain their full scope.
- Safe gain no longer deducts estimated tone/EQ boosts from the entire signal. A stereo-linked limiter reacts to real output peaks after preamp/master-volume smoothing, with instantaneous peak protection and an 80 ms recovery constant. It leaves non-clipping output unchanged and preserves speaker balance. Final finite-value sanitization/clamping remains in place. Filters/compression/panning retain their intended behavior.

Validation uses generated audio and isolated profiles. Tests cover unaffected output with boosts and quiet master volume, intended target-frequency gains, actual clipping protection, stereo ratios and recovery, exact preservation of unrelated preset fields, real WPF button/menu dispatch, complete top preset changes, and every theme's one-row geometry. The existing running player is preserved during publication; the updated executable takes effect when reopened.
