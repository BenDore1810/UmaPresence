using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace UmapyonPresence
{
    // Original game labels are independent of Hachimi and only read from the master database.
    internal static class GameText
    {
        private static Dictionary<string, string> labels = new Dictionary<string, string>();
        private static DateTime next;
        private static string stamp;
        internal static void Reload(Settings settings) { next = DateTime.MinValue; stamp = null; Refresh(settings); if (stamp == null) throw new IOException("Game label reload failed; previous data retained"); }
        internal static void Refresh(Settings settings)
        {
            if (settings == null || string.IsNullOrEmpty(settings.GameDirectory) || DateTime.UtcNow < next) return;
            next = DateTime.UtcNow.AddMinutes(1);
            string path = Path.Combine(settings.GameDirectory, "umamusume_Data", "Persistent", "master", "master.mdb");
            if (!File.Exists(path)) return;
            IntPtr database = IntPtr.Zero;
            try {
                var info = new FileInfo(path); string current = path + "|" + info.Length + "|" + info.LastWriteTimeUtc.Ticks;
                if (current == stamp) return;
                if (OutfitCatalog.Sqlite.Open(OutfitCatalog.Utf8(path), out database, 1, IntPtr.Zero) != 0) return;
                var original = new Dictionary<string, string>();
                OutfitCatalog.Query(database, "select category,\"index\",text from text_data where category in (5,6,7,14,16,17,119)", row => {
                    int length = OutfitCatalog.Sqlite.ColumnBytes(row, 2); IntPtr text = OutfitCatalog.Sqlite.ColumnText(row, 2);
                    if (length <= 0 || length > 32768 || text == IntPtr.Zero) return;
                    var bytes = new byte[length]; Marshal.Copy(text, bytes, 0, length);
                    original[OutfitCatalog.Sqlite.ColumnInt(row, 0) + ":" + OutfitCatalog.Sqlite.ColumnInt(row, 1)] = Encoding.UTF8.GetString(bytes);
                });
                labels = original; stamp = current;
            } catch (IOException) { } catch (UnauthorizedAccessException) { }
            finally { if (database != IntPtr.Zero) OutfitCatalog.Sqlite.Close(database); }
        }
        internal static string Find(string category, int id, Settings settings)
        {
            Refresh(settings); string text; return labels.TryGetValue(category + ":" + id, out text) ? text : null;
        }
    }
}
