using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MeltySynth;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using Serilog;

namespace OpenUtau.Core.Sf2 {
    /// <summary>
    /// Renders a phrase by driving the singer's SoundFont (.sf2) instrument
    /// with note-on/note-off events derived from the notes on the track,
    /// instead of the classic resampler/wavtool pipeline. Existing note,
    /// pitch and MIDI editing (drag, resize, pitch bend, tuning) all still
    /// feed this renderer through the same RenderPhrase, so nothing about
    /// how notes are edited changes.
    /// </summary>
    public class Sf2Renderer : IRenderer {
        const int fs = 44100;
        const float tailMs = 200f; // let the release ring out a little

        static readonly object lockObj = new object();

        static readonly HashSet<string> supportedExp = new HashSet<string>() {
            Format.Ustx.DYN,
            Format.Ustx.PITD,
            Format.Ustx.VEL,
        };

        public USingerType SingerType => USingerType.Sf2;

        public bool SupportsRenderPitch => false;
        public bool SupportsPhonemeEnvelope => false;

        public bool SupportsExpression(UExpressionDescriptor descriptor) {
            return supportedExp.Contains(descriptor.abbr);
        }

        public RenderResult Layout(RenderPhrase phrase) {
            return new RenderResult() {
                leadingMs = 0,
                positionMs = phrase.positionMs,
                estimatedLengthMs = phrase.durationMs + tailMs,
            };
        }

        public Task<RenderResult> Render(RenderPhrase phrase, Progress progress, int trackNo, CancellationTokenSource cancellation, bool isPreRender = false, RenderPhraseEvents? renderEvents = null) {
            return Task.Run(() => {
                lock (lockObj) {
                    var result = Layout(phrase);
                    if (cancellation.IsCancellationRequested) {
                        return result;
                    }
                    string progressInfo = $"Track {trackNo + 1}: {this} \"{string.Join(" ", phrase.notes.Select(n => n.lyric))}\"";
                    try {
                        result.samples = RenderNotes(phrase, cancellation);
                    } catch (Exception e) {
                        Log.Error(e, "Sf2 render failed.");
                        result.samples = new float[(int)(result.estimatedLengthMs / 1000.0 * fs)];
                    }
                    if (result.samples != null) {
                        Renderers.ApplyDynamics(phrase, result);
                    }
                    progress.Complete(phrase.notes.Length, progressInfo);
                    return result;
                }
            });
        }

        float[] RenderNotes(RenderPhrase phrase, CancellationTokenSource cancellation) {
            var singer = phrase.singer as Sf2Singer;
            if (singer == null) {
                throw new InvalidOperationException("Sf2Renderer used with a non-sf2 singer.");
            }
            var soundFont = singer.GetSoundFont();
            var synthesizer = new Synthesizer(soundFont, fs);
            // Bank select (MSB) + program change to the configured preset.
            synthesizer.ProcessMidiMessage(0, 0xB0, 0x00, singer.bankNumber & 0x7F);
            synthesizer.ProcessMidiMessage(0, 0xC0, singer.patchNumber & 0x7F, 0);

            int totalSamples = (int)Math.Ceiling((phrase.durationMs + tailMs) / 1000.0 * fs);
            var left = new float[totalSamples];
            var right = new float[totalSamples];

            // Build sample-accurate note-on/note-off events, relative to the
            // start of the phrase (which Layout() reports as leadingMs=0).
            var events = new List<(int sample, Action apply)>();
            foreach (var note in phrase.notes) {
                if (note.tone <= 0 || string.IsNullOrEmpty(note.lyric) || note.lyric == "R" || note.lyric == "-") {
                    continue; // rests / carry-over notes produce no new note-on
                }
                int onSample = Math.Max(0, (int)((note.positionMs - phrase.positionMs) / 1000.0 * fs));
                int offSample = Math.Min(totalSamples, (int)((note.positionMs - phrase.positionMs + note.durationMs) / 1000.0 * fs));
                int key = Math.Clamp(note.tone, 0, 127);
                int velocity = 100;
                events.Add((onSample, () => synthesizer.NoteOn(0, key, velocity)));
                events.Add((offSample, () => synthesizer.NoteOff(0, key)));
            }
            events.Sort((a, b) => a.sample.CompareTo(b.sample));

            int cursor = 0;
            foreach (var evt in events) {
                if (cancellation.IsCancellationRequested) {
                    break;
                }
                int chunk = Math.Max(0, Math.Min(evt.sample, totalSamples) - cursor);
                if (chunk > 0) {
                    synthesizer.Render(
                        left.AsSpan(cursor, chunk),
                        right.AsSpan(cursor, chunk));
                    cursor += chunk;
                }
                evt.apply();
            }
            if (cursor < totalSamples && !cancellation.IsCancellationRequested) {
                synthesizer.Render(
                    left.AsSpan(cursor, totalSamples - cursor),
                    right.AsSpan(cursor, totalSamples - cursor));
            }

            // Downmix to mono, matching the mono-float pipeline other
            // renderers (Classic/Vogen/DiffSinger) already produce.
            var mono = new float[totalSamples];
            for (int i = 0; i < totalSamples; i++) {
                mono[i] = (left[i] + right[i]) * 0.5f;
            }
            return mono;
        }

        public RenderPitchResult LoadRenderedPitch(RenderPhrase phrase) {
            return null; // SF2 playback does not produce a derived pitch curve.
        }

        public UExpressionDescriptor[] GetSuggestedExpressions(USinger singer, URenderSettings renderSettings) {
            return Array.Empty<UExpressionDescriptor>();
        }

        public override string ToString() => Renderers.SF2;
    }
}
