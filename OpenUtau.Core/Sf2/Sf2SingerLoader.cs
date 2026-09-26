using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenUtau.Core.Util;
using Serilog;

namespace OpenUtau.Core.Sf2 {
    public static class Sf2SingerLoader {
        public const string FileExt = ".sf2";

        public static IEnumerable<Ustx.USinger> FindAllSingers() {
            List<Ustx.USinger> singers = new List<Ustx.USinger>();
            foreach (var path in PathManager.Inst.SingersPaths) {
                if (!Directory.Exists(path)) {
                    continue;
                }
                IEnumerable<string> files;
                try {
                    files = Preferences.Default.LoadDeepFolderSinger
                        ? Directory.EnumerateFiles(path, "*.sf2", SearchOption.AllDirectories)
                        : Directory.EnumerateFiles(path, "*.sf2");
                } catch (Exception e) {
                    Log.Error(e, $"Failed to search sf2 singers in {path}");
                    continue;
                }
                foreach (var file in files) {
                    try {
                        singers.Add(new Sf2Singer(file));
                    } catch (Exception e) {
                        Log.Error(e, $"Failed to load sf2 singer {file}");
                    }
                }
            }
            return singers;
        }
    }
}
