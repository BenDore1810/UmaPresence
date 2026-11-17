using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace UmapyonPresence
{
    // Metadata only. Presence of a local recording is not proof that every part
    // can be routed; the native host additionally verifies hashes and cue graphs.
    internal static class ConcertRecordingCatalog
    {
        internal sealed class Document { public Row[] rows { get; set; } }
        internal sealed class Row { public int song { get; set; } public int voice { get; set; } public string acb { get; set; } public string awb { get; set; } public long awbBytes { get; set; } }
        private static string identity;
        private static Row[] rows;
        internal static HashSet<int> Available(Settings settings, int song, string catalogue = null)
        {
            if (song <= 0) return null;
            string path = catalogue ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"standalone","vocal-coverage.json");
            try {
                var info = new FileInfo(path); if (!info.Exists || info.Length > 2 * 1024 * 1024) return null;
                string key = info.FullName + ":" + info.LastWriteTimeUtc.Ticks;
                if (key != identity) { var data = Json.Decode<Document>(File.ReadAllText(path)); if(data == null || data.rows == null || data.rows.Length > 4096)return null; rows=data.rows; identity=key; }
                string root=Path.Combine(settings.GameDirectory ?? "","umamusume_Data","Persistent","dat");
                var result=new HashSet<int>();
                foreach(var row in rows.Where(row=>row.song==song && row.voice>0 && row.voice<2000)) {
                    if(!Digest(row.acb)||!Digest(row.awb)||row.awbBytes<16)continue;
                    string acb=Path.Combine(root,row.acb.Substring(0,2),row.acb),awb=Path.Combine(root,row.awb.Substring(0,2),row.awb);
                    if(File.Exists(acb)&&File.Exists(awb)&&new FileInfo(awb).Length==row.awbBytes)result.Add(row.voice);
                }
                return result;
            } catch(IOException) { return null; } catch(UnauthorizedAccessException) { return null; } catch(ArgumentException) { return null; } catch(InvalidOperationException) { return null; }
        }
        private static bool Digest(string value) { return value!=null && value.Length==32 && value.All(ch=>ch>='A'&&ch<='Z'||ch>='2'&&ch<='7'); }
    }
}
