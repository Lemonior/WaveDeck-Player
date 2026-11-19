using MusicPlayer.Audio;
using MusicPlayer.Models;
using MusicPlayer.Services;
using NAudio.Wave;
using System.Text.Json;

namespace MusicPlayer.Validation;

public static partial class SelfTests
{
    private static async Task RunSoundV2TestsAsync(string root, Func<string, Func<Task>, Task> test, Func<Action, Task> sync)
    {
        float[] ToneSeconds(double frequency, int seconds = 3, float amplitude = .15f)
        {
            var values = new float[48000 * seconds * 2];
            for (var i = 0; i < values.Length / 2; i++) values[i * 2] = values[i * 2 + 1] = amplitude * (float)Math.Sin(2 * Math.PI * frequency * i / 48000);
            return values;
        }
        float[] Render(float[] input, SoundProfile sound, bool safeGain = false, float volume = 1, float[]? gains = null)
        {
            var processor = new EqualizerSampleProvider(new ArrayProvider(input), new()); processor.Configure(new(gains != null, gains ?? new float[10], 0, 1, safeGain, volume, sound));
            var output = new List<float>(); var buffer = new float[2056]; var reads = 0;
            while (true)
            {
                Array.Fill(buffer, 9.876f); var count = processor.Read(buffer, 4, 2048);
                Assert(buffer.Take(4).All(v => v == 9.876f) && buffer.Skip(2052).All(v => v == 9.876f), "Processing overwrote data outside the requested buffer.");
                if (count == 0) break;
                output.AddRange(buffer.AsSpan(4, count).ToArray());
                Assert(++reads < 1000, "Tempo EOF never finished.");
            }
            return output.ToArray();
        }
        double Frequency(float[] values)
        {
            var start = 48000; var end = Math.Min(values.Length - 10000, start + 96000); var crossings = 0;
            for (var i = start + 2; i < end; i += 2) if (values[i - 2] <= 0 && values[i] > 0) crossings++;
            return crossings * 48000d / ((end - start) / 2);
        }
        await test("Equalizer V2 neutral controls preserve exact samples and stream length", () => sync(() =>
        {
            var tone = ToneSeconds(440); var output = Render(tone, new()); Assert(output.SequenceEqual(tone), "Default sound processing is not transparent.");
        }));
        await test("Tempo changes duration while preserving pitch; pitch changes preserve duration", () => sync(() =>
        {
            var tone = ToneSeconds(440, 4);
            foreach (var (speed, pitch, tape) in new[] { (.5, 0d, false), (2d, 0d, false), (1d, 12d, false), (1.5, -12d, false), (1.5, 0d, true) })
            {
                var output = Render(tone, new() { Tempo = speed, PitchSemitones = pitch, TapeMode = tape });
                Assert(Math.Abs(output.Length / (double)tone.Length - 1 / speed) < .015, $"Tempo {speed}, pitch {pitch}: output duration is incorrect ({output.Length}).");
                var expected = 440 * Math.Pow(2, pitch / 12) * (tape ? speed : 1); var measured = Frequency(output);
                Assert(Math.Abs(measured - expected) / expected < .025, $"Tempo {speed}, pitch {pitch}, tape {tape}: expected {expected} Hz, measured {measured}.");
                Assert(output.All(float.IsFinite), "Pitch/tempo emitted a non-finite sample.");
            }
        }));
        await test("Bass, presence and air knobs emphasize their intended frequencies", () => sync(() =>
        {
            foreach (var (hz, p) in new (double, SoundProfile)[] { (50, new() { BassDb = 6 }), (2500, new() { PresenceDb = 6 }), (14000, new() { AirDb = 6 }) })
            {
                var tone = ToneSeconds(hz); var db = 20 * Math.Log10(Rms(Render(tone, p)) / Rms(tone));
                Assert(db > 4.5 && db < 6.5, $"Tone knob at {hz} Hz measured {db} dB.");
            }
        }));
        await test("Safe gain leaves non-clipping tone and equalizer boosts at their requested volume", () => sync(() =>
        {
            var profiles = new[] { new SoundProfile { BassDb = 6 }, new SoundProfile { PresenceDb = 6 }, new SoundProfile { AirDb = 6 }, new SoundProfile { BassDb = 6, PresenceDb = 6, AirDb = 6 } };
            foreach (var profile in profiles)
            {
                var tone = ToneSeconds(1000, amplitude: .05f);
                Assert(Render(tone, profile, true).SequenceEqual(Render(tone, profile)), "Safe gain turned down an unclipped song because a tone knob was boosted.");
            }
            foreach (var (hz, profile) in new (double, SoundProfile)[] { (50, profiles[0]), (2500, profiles[1]), (14000, profiles[2]) })
            {
                var tone = ToneSeconds(hz); var db = 20 * Math.Log10(Rms(Render(tone, profile, true)) / Rms(tone));
                Assert(db > 4.5 && db < 6.5, "Safe gain canceled the requested tone boost.");
            }
            var bands = new float[10]; bands[5] = 6; var mid = ToneSeconds(1000);
            Assert(Render(mid, new(), true, gains: bands).SequenceEqual(Render(mid, new(), gains: bands)), "Safe gain canceled a non-clipping equalizer boost.");
            var quietVolume = VolumeCurve.ToGain(.15); var loud = ToneSeconds(50, amplitude: .8f);
            Assert(Render(loud, new() { BassDb = 12 }, true, quietVolume).SequenceEqual(Render(loud, new() { BassDb = 12 }, false, quietVolume)), "Safe gain ignored the user's volume and attenuated output below clipping level.");
        }));
        await test("Safe gain protects actual peaks, preserves stereo balance and recovers after overload", () => sync(() =>
        {
            var samples = ToneSeconds(50, amplitude: .8f);
            for (var i = 1; i < samples.Length; i += 2) samples[i] *= .5f;
            var protectedOutput = Render(samples, new() { BassDb = 12 }, true);
            Assert(protectedOutput.All(v => float.IsFinite(v) && Math.Abs(v) <= 1), "The peak limiter exceeded digital full scale.");
            for (var i = 0; i < protectedOutput.Length; i += 2) Assert(Math.Abs(protectedOutput[i + 1] - protectedOutput[i] * .5f) < .00001, "Safe gain changed stereo balance while limiting.");
            var recoveredInput = samples.Concat(ToneSeconds(1000, amplitude: .05f)).ToArray();
            var recovered = Render(recoveredInput, new() { BassDb = 12 }, true);
            var raw = Render(recoveredInput, new() { BassDb = 12 });
            Assert(Math.Abs(Rms(recovered[^48000..]) / Rms(raw[^48000..]) - 1) < .001, "An earlier overload permanently lowered song volume.");
        }));
        await test("Tab presets and resets preserve the other tab's exact settings and master mix", () => sync(() =>
        {
            var original = new SoundProfile { BassDb = 5, PresenceDb = 2, AirDb = -3, Drive = .4f, Compression = .3f, Balance = -.2f, Tempo = 1.3, PitchSemitones = -4, TapeMode = true, Echo = .3f, EchoTimeMs = 600, EchoFeedback = .6f, Reverb = .2f, RoomSize = .8f, FilterHz = 9000, Resonance = 1.4f, Crush = .25f, Mix = .6f };
            foreach (var preset in SoundPresets.Knobs.Values.Append(new SoundProfile()))
            {
                var result = SoundPresets.ApplySection(original, preset, SoundSection.Knobs);
                Assert(result == (original with { BassDb = preset.BassDb, PresenceDb = preset.PresenceDb, AirDb = preset.AirDb, Drive = preset.Drive, Compression = preset.Compression, Balance = preset.Balance }), "A Knobs preset changed Effects or master mix.");
            }
            foreach (var preset in SoundPresets.Effects.Values.Append(new SoundProfile()))
            {
                var result = SoundPresets.ApplySection(original, preset, SoundSection.Effects);
                Assert(result.BassDb == original.BassDb && result.PresenceDb == original.PresenceDb && result.AirDb == original.AirDb && result.Drive == original.Drive && result.Compression == original.Compression && result.Balance == original.Balance && result.Mix == original.Mix, "An Effects preset changed Knobs or master mix.");
                Assert(result.Tempo == preset.Tempo && result.PitchSemitones == preset.PitchSemitones && result.TapeMode == preset.TapeMode && result.Echo == preset.Echo && result.FilterHz == preset.FilterHz && result.Crush == preset.Crush, "An Effects preset did not apply playback processing.");
            }
        }));
        await test("Sweep attenuates highs; drive adds harmonics; crush reduces precision; balance pans", () => sync(() =>
        {
            var high = ToneSeconds(9000); Assert(Rms(Render(high, new() { FilterHz = 1000 })) < Rms(high) * .025, "Low-pass sweep did not suppress highs.");
            var tone = ToneSeconds(440, amplitude: .3f); var driven = Render(tone, new() { Drive = .8f });
            Assert(driven.Zip(tone).Any(pair => Math.Abs(pair.First - pair.Second) > .1), "Drive did not change the waveform.");
            double third = 0, thirdCos = 0;
            for (var frame = 96000; frame < 144000; frame++) { var value = driven[frame * 2]; var angle = 2 * Math.PI * 1320 * frame / 48000; third += value * Math.Sin(angle); thirdCos += value * Math.Cos(angle); }
            Assert(Math.Sqrt(third * third + thirdCos * thirdCos) * 2 / 48000 > .01, "Drive added gain but no third harmonic.");
            var crushed = Render(tone, new() { Crush = 1 }); Assert(crushed.All(v => Math.Abs(v * 8 - MathF.Round(v * 8)) < .00001), "Crush did not quantize to the selected bit depth.");
            var panned = Render(tone, new() { Balance = -1 });
            Assert(panned.Where((_, i) => i % 2 == 0).SequenceEqual(tone.Where((_, i) => i % 2 == 0)) && panned.Where((_, i) => i % 2 == 1).All(v => v == 0), "Balance changed the wrong speaker.");
        }));
        await test("Echo repeats at the selected time and feedback; reverb produces a finite tail", () => sync(() =>
        {
            var impulse = new float[48000 * 2]; impulse[0] = impulse[1] = .5f;
            var echoed = Render(impulse, new() { Echo = 1, EchoTimeMs = 200, EchoFeedback = .5f });
            Assert(Math.Abs(echoed[9600 * 2] - .35f) < .0001 && Math.Abs(echoed[19200 * 2] - .175f) < .0001, "Echo timing/feedback is incorrect.");
            var room = Render(impulse, new() { Reverb = 1 }); Assert(room.Skip(2000).Any(v => Math.Abs(v) > .001), "Reverb did not create a room tail.");
            Assert(room.All(v => float.IsFinite(v) && Math.Abs(v) <= 1), "Reverb emitted invalid output.");
        }));
        await test("Compression reduces loud peaks, effect mix restores dry sound and global A/B is neutral", () => sync(() =>
        {
            var loud = ToneSeconds(440, amplitude: .85f); Assert(Rms(Render(loud, new() { Compression = 1 })) < Rms(loud) * .5, "Compression did not reduce the dynamic peaks.");
            var mixed = Render(loud, new() { Drive = 1, Echo = 1, FilterHz = 200, Mix = 0 }); Assert(mixed.SequenceEqual(loud), "Zero effect mix did not restore the original signal.");
            var safeAnalyzer = new AudioAnalyzer(); var safe = new EqualizerSampleProvider(new ArrayProvider(loud), safeAnalyzer);
            safe.Configure(new(false, new float[10], 0, 1, true, 1, new() { BassDb = 12, Mix = 0 })); var original = new float[loud.Length]; safe.Read(original, 0, original.Length);
            Assert(original.SequenceEqual(loud), "Safe gain attenuated bypassed tone knobs at zero wet mix.");
            var settings = new PlayerSettings { Volume = 1, Sound = new() { Tempo = 2, PitchSemitones = 12, Drive = 1, Echo = 1 } };
            var dry = EqParameters.FromSettings(settings, true);
            Assert(!dry.Enabled && dry.Sound == new SoundProfile(), "A/B retained playback or sound effects.");
        }));
        await test("Live tempo changes and repositioning drain/reset buffered processing at EOF", () => sync(() =>
        {
            var source = new SeekableSamples(ToneSeconds(440)); var provider = new TempoSampleProvider(source);
            var buffer = new float[4096]; provider.Configure(new() { Tempo = .7, PitchSemitones = 3 }); Assert(provider.Read(buffer, 0, buffer.Length) > 0, "Tempo did not start.");
            provider.Configure(new() { Tempo = 1.8, PitchSemitones = -2 }); Assert(provider.Read(buffer, 0, buffer.Length) > 0, "A live speed change stopped audio.");
            source.Position = source.Samples.Length; provider.Reposition(); Assert(provider.Read(buffer, 0, buffer.Length) == 0, "Seeking to EOF replayed stale samples.");
            source.Position = 0; provider.Reposition(); provider.Configure(new()); Assert(provider.Read(buffer, 0, buffer.Length) > 0, "Seek/restart after EOF stayed ended.");
        }));
        await test("Sound controls and tab persist; old presets remain neutral; invalid values normalize", () => sync(() =>
        {
            var storage = new Storage(Path.Combine(root, "sound-v2-settings")); var settings = new PlayerSettings { SoundTab = 2, Sound = new() { BassDb = 4, Tempo = 1.25, PitchSemitones = -2, Echo = .4f, Mix = .7f, TapeMode = true } }; storage.Save(settings);
            var loaded = storage.Load(); Assert(loaded.Sound == settings.Sound && loaded.SoundTab == 2, "Sound controls did not survive saving.");
            var old = JsonSerializer.Deserialize<PlayerSettings>("{\"Gains\":[1,0,0,0,0,0,0,0,0,0]}", Storage.JsonOptions)!; old.Normalize(); Assert(old.Sound == new SoundProfile(), "A legacy preset enabled unexpected effects.");
            var invalid = new SoundProfile { Tempo = double.NaN, PitchSemitones = 100, BassDb = float.PositiveInfinity, EchoFeedback = 2, FilterHz = -100, Mix = float.NaN }.Normalize();
            Assert(invalid.Tempo == 1 && invalid.PitchSemitones == 12 && invalid.BassDb == 0 && invalid.EchoFeedback == .8f && invalid.FilterHz == 20 && invalid.Mix == 1, "Sound limits are not safe and finite.");
        }));
    }
    private sealed class SeekableSamples(float[] samples) : ISampleProvider
    {
        public float[] Samples { get; } = samples;
        public int Position;
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        public int Read(float[] buffer, int offset, int count) { count = Math.Min(count, Samples.Length - Position); Array.Copy(Samples, Position, buffer, offset, count); Position += count; return count; }
    }
}
