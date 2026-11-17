using System;
using System.IO.MemoryMappedFiles;
using System.Text;
using System.Globalization;

namespace UmapyonPresence
{
    internal static class StandaloneAudioDiagnostics
    {
        internal static string Report(int pid)
        {
            if (pid < 1) return "Audio graph: game not running";
            try {
                using (var mapping = MemoryMappedFile.OpenExisting("Local\\UmaPresenceRuntime-" + pid, MemoryMappedFileRights.Read))
                using (var view = mapping.CreateViewAccessor(256, 8256, MemoryMappedFileAccess.Read)) {
                    int before = view.ReadInt32(8);var bytes = new byte[8256];view.ReadArray(0, bytes, 0, bytes.Length);
                    uint version = BitConverter.ToUInt32(bytes, 4);
                    if ((before & 1) != 0 || before != view.ReadInt32(8) || BitConverter.ToUInt32(bytes, 0) != 0x55504131 || (version != 1 && version != 2)) return "Audio graph: update in progress or incompatible";
                    uint count = BitConverter.ToUInt32(bytes, 12);if(count > 64)return "Audio graph: invalid track count";
                    var text = new StringBuilder("Native vocal graph (metadata only; no audio recorded)\r\n");
                    if (version == 2) {
                        text.AppendFormat(CultureInfo.InvariantCulture, "Playback bindings: stage {0}; seek reason {1} (0 ready, 1 outside concert, 2 bindings unavailable, 3 audio graph unavailable, 4 incomplete cue window); seek range {2:F3}–{3:F3}s\r\n", BitConverter.ToUInt32(bytes, 48), BitConverter.ToUInt32(bytes, 52), BitConverter.ToSingle(bytes, 40), BitConverter.ToSingle(bytes, 44));
                        text.AppendFormat("Formation duplicate hook: {0} checks, {1} allowed without swapping\r\n", BitConverter.ToUInt32(bytes, 56), BitConverter.ToUInt32(bytes, 60));
                    } else text.AppendLine("Detailed playback/formation diagnostics require the concert-v3 runtime; this session uses the earlier build.");
                    text.AppendFormat("Last arrangement: song {0}, part {1}, replacements {2}, original-singer fallbacks {3}\r\n", BitConverter.ToInt32(bytes, 16), BitConverter.ToInt32(bytes, 20), BitConverter.ToUInt32(bytes, 28), BitConverter.ToUInt32(bytes, 24));
                    text.AppendFormat("Tracked vocal players: {0}\r\n", count);
                    for (int i = 0; i < count; i++) { int p = 64 + i * 128;
                        text.AppendFormat(CultureInfo.InvariantCulture, "{0}: voice={1}, cue={2}, identity-certified={3}, offset={4:F4}s, native={5:F4}, factor={6:F4}, effective={7:F4} | {8}\r\n", i + 1, BitConverter.ToInt32(bytes, p + 4), BitConverter.ToInt32(bytes, p + 8), BitConverter.ToUInt32(bytes, p + 12) == 1, BitConverter.ToSingle(bytes, p + 16), BitConverter.ToSingle(bytes, p + 20), BitConverter.ToSingle(bytes, p + 24), BitConverter.ToSingle(bytes, p + 28), Encoding.ASCII.GetString(bytes, p + 32, 96).TrimEnd('\0'));
                    }
                    return text.ToString();
                }
            } catch (Exception ex) { return "Native audio diagnostics unavailable: " + ex.Message; }
        }
    }
}
