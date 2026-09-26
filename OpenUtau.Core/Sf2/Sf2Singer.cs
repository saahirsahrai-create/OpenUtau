using System.Collections.Generic;
using System.IO;
using System.Text;
using MeltySynth;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Core.Sf2 {
    /// <summary>
    /// Wraps a single .sf2 (SoundFont2) file as a "singer" so it can be
    /// picked from the existing singer list and attached to a track exactly
    /// like any other singer. No voicebank/oto data is involved: notes are
    /// played straight through the SoundFont preset (MIDI-style synth)
    /// instead of being resampled from recorded phonemes.
    /// </summary>
    public class Sf2Singer : USinger {
        public override string Id => id;
        public override string Name => name;
        public override Dictionary<string, string> LocalizedNames => new Dictionary<string, string>();
        public override USingerType SingerType => USingerType.Sf2;
        public override string BasePath => basePath;
        public override string Author => string.Empty;
        public override string Voice => name;
        public override string Location => filePath;
        public override string Web => string.Empty;
        public override string Version => string.Empty;
        public override string OtherInfo => string.Empty;
        public override IList<string> Errors => errors;
        public override string Avatar => string.Empty;
        public override byte[] AvatarData => null;
        public override string Portrait => null;
        public override float PortraitOpacity => 0.67f;
        public override int PortraitHeight => 0;
        // No lyric/phoneme processing is needed: pass lyrics through as-is.
        public override string DefaultPhonemizer => "OpenUtau.Core.DefaultPhonemizer";
        public override Encoding TextFileEncoding => Encoding.UTF8;
        public override IList<USubbank> Subbanks => subbanks;

        public readonly string filePath;
        readonly string basePath;
        readonly string id;
        readonly string name;
        readonly List<string> errors = new List<string>();
        readonly List<USubbank> subbanks = new List<USubbank>();

        /// <summary>
        /// Bank/preset (patch) selected inside the SoundFont for this singer.
        /// Defaults to the first preset (usually bank 0, patch 0).
        /// </summary>
        public int bankNumber = 0;
        public int patchNumber = 0;

        SoundFont soundFont;
        readonly object loadLock = new object();

        public Sf2Singer(string filePath) {
            this.filePath = filePath;
            basePath = Path.GetDirectoryName(filePath);
            name = Path.GetFileNameWithoutExtension(filePath);
            id = $"sf2:{name}";
            found = true;
        }

        /// <summary>
        /// Lazily loads and caches the SoundFont data (can be a few MB, so
        /// avoid touching disk on every render call).
        /// </summary>
        public SoundFont GetSoundFont() {
            if (soundFont == null) {
                lock (loadLock) {
                    if (soundFont == null) {
                        using (var stream = File.OpenRead(filePath)) {
                            soundFont = new SoundFont(stream);
                        }
                        loaded = true;
                    }
                }
            }
            return soundFont;
        }

        public override void EnsureLoaded() {
            try {
                GetSoundFont();
            } catch (System.Exception e) {
                errors.Add(e.Message);
            }
        }

        public override bool TryGetOto(string phoneme, out UOto oto) {
            // SF2 rendering does not use oto/alias lookup; always succeed
            // with a dummy entry so upstream phonemizer/UI code that expects
            // an oto to exist does not break.
            oto = UOto.OfDummy(phoneme);
            return true;
        }
    }
}
