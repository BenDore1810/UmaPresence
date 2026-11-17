using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace UmapyonPresence
{
    internal sealed class OutfitEntry
    {
        public int DressId { get; set; }
        public int CharacterId { get; set; }
        public string Url { get; set; }
        public string Style { get; set; }
        public DateTime NextLookup { get; set; }
    }
    internal static class OutfitCatalog
    {
        private static Dictionary<int, OutfitEntry> entries;
        private static Dictionary<string, int> cardDresses;
        private static readonly Dictionary<int, int> mainCounts = new Dictionary<int, int>();
        private static DateTime nextCheck;
        private static string masterStamp = "";
        public static string Status { get; private set; } = "Bundled outfit catalog";
        internal static void Reload(Settings settings) { nextCheck = DateTime.MinValue; masterStamp = ""; Refresh(settings); }
        private static string CachePath { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "outfit-images.cache.json"); } }

        private static void Load()
        {
            if (entries != null) return;
            entries = SongCatalog.Resource<List<OutfitEntry>>("outfit-catalog.json").ToDictionary(e => e.DressId);
            cardDresses = SongCatalog.Resource<Dictionary<string, int>>("card-dresses.json");
            try
            {
                if (File.Exists(CachePath)) foreach (var entry in Json.Decode<List<OutfitEntry>>(File.ReadAllText(CachePath, Encoding.UTF8)))
                    if (ValidWikiUrl(entry.Url)) entries[entry.DressId] = entry;
            }
            catch (Exception ex) { Log.Write("Portrait cache skipped: " + ex.Message); }
        }
        public static void Refresh(Settings settings)
        {
            GameText.Refresh(settings);
            LegacyDetails.Refresh(settings);
            Load();
            if (DateTime.UtcNow < nextCheck || string.IsNullOrWhiteSpace(settings.GameDirectory)) return;
            nextCheck = DateTime.UtcNow.AddSeconds(60);
            string path = Path.Combine(settings.GameDirectory, "umamusume_Data", "Persistent", "master", "master.mdb");
            if (!File.Exists(path)) return;
            var info = new FileInfo(path);
            string stamp = path + "|" + info.Length + "|" + info.LastWriteTimeUtc.Ticks;
            if (stamp == masterStamp) return;
            IntPtr database = IntPtr.Zero;
            try
            {
                if (Sqlite.Open(Utf8(path), out database, 1, IntPtr.Zero) != 0) throw new IOException("Cannot open game master read-only");
                var current = new Dictionary<int, OutfitEntry>();
                Query(database, "select id,chara_id from dress_data where use_race=1 and use_live_theater=1 and chara_id between 1000 and 1999", statement =>
                {
                    int id = Sqlite.ColumnInt(statement, 0), character = Sqlite.ColumnInt(statement, 1);
                    OutfitEntry entry;
                    if (!entries.TryGetValue(id, out entry) || entry.CharacterId != character)
                        entry = new OutfitEntry { DressId = id, CharacterId = character, Url = "", Style = "stand" };
                    current[id] = entry;
                });
                if (current.Count == 0) throw new IOException("Game racing-outfit table is empty");
                var counts = new Dictionary<int, int>();
                Query(database, "select music_id,live_member_number,backdancer_order from live_data where has_live=1", statement =>
                {
                    int members = Sqlite.ColumnInt(statement, 1), firstBackDancer = Sqlite.ColumnInt(statement, 2);
                    counts[Sqlite.ColumnInt(statement, 0)] = Math.Max(1, Math.Min(18, firstBackDancer > 0 ? Math.Min(members, firstBackDancer - 1) : members));
                });
                entries = current;
                var cards = new Dictionary<string, int>();
                Query(database, "select card_id,race_dress_id from card_rarity_data order by rarity", statement => { int card = Sqlite.ColumnInt(statement, 0), dress = Sqlite.ColumnInt(statement, 1); if (dress > 0) cards[card.ToString()] = dress; });
                if (cards.Count > 0) cardDresses = cards;
                mainCounts.Clear(); foreach (var pair in counts) mainCounts[pair.Key] = pair.Value;
                masterStamp = stamp;
                Status = "Game catalog refreshed 窶｢ " + entries.Count + " racing outfits";
                Log.Write(Status);
            }
            catch (Exception ex) { Status = "Using bundled catalog 窶｢ " + ex.Message; Log.Write(Status); }
            finally { if (database != IntPtr.Zero) Sqlite.Close(database); }
        }
        public static int MainCount(int songId)
        {
            int count;
            return mainCounts.TryGetValue(songId, out count) ? count : Math.Max(1, SongCatalog.Find(songId).MainCount);
        }
        public static bool IsRacing(int character, int dress)
        {
            Load(); OutfitEntry entry;
            return entries.TryGetValue(dress, out entry) && entry.CharacterId == character;
        }
        internal static int DressForCard(int card)
        {
            Load(); int dress;
            return cardDresses.TryGetValue(card.ToString(), out dress) && IsRacing(card / 100, dress) ? dress : IsRacing(card / 100, card) ? card : 0;
        }
        internal static int CardForDress(int character, int dress)
        {
            Load();
            return cardDresses.Where(pair => pair.Value == dress).Select(pair => { int card; return int.TryParse(pair.Key, out card) && card / 100 == character ? card : 0; }).FirstOrDefault(card => card > 0);
        }
        internal static string CareerPortrait(int card, Settings settings)
        {
            string uploaded;
            if (settings.CareerPortraitAssets != null && settings.CareerPortraitAssets.TryGetValue(card.ToString(), out uploaded) && !string.IsNullOrWhiteSpace(uploaded)) return uploaded;
            int character = card / 100, dress = DressForCard(card);
            string portrait = dress > 0 ? Portrait(character, dress, settings) : null;
            return portrait ?? SongCatalog.Portrait(character) ?? SongCatalog.OutfitPortrait(character, 0, settings);
        }
        internal static string BakeSource(int card, Settings settings)
        {
            Load(); int dress = DressForCard(card); OutfitEntry entry;
            if (!entries.TryGetValue(dress, out entry) || string.IsNullOrEmpty(entry.Url)) return null;
            if (entry.Style == "card") return "https://wsrv.nl/?url=" + Uri.EscapeDataString(entry.Url) + "&cx=26&cy=20&cw=188&ch=188&precrop&w=256&h=256&output=png";
            string source = Portrait(card / 100, dress, settings);
            return source == null ? null : source.Replace("&w=128&h=128", "&w=512&h=512");
        }
        public static string Portrait(int character, int dress, Settings settings)
        {
            Load(); OutfitEntry entry;
            if (!entries.TryGetValue(dress, out entry) || entry.CharacterId != character) return null;
            string custom;
            if (settings.ConcertOutfitPortraits != null && settings.ConcertOutfitPortraits.TryGetValue(dress.ToString(), out custom) && !string.IsNullOrWhiteSpace(custom)) return custom;
            if (string.IsNullOrEmpty(entry.Url))
            {
                // A racing costume must never silently select the student portrait.
                if (!entries.TryGetValue(character * 100 + 1, out entry) || string.IsNullOrEmpty(entry.Url)) return null;
            }
            if (!settings.ConcertZoomPortraits || entry.Style == "portrait") return entry.Url;
            string crop = entry.Style == "card" ? CropCoordinates(36, 40, 184, 184, settings.ConcertPortraitZoomFactor) : CropCoordinates(96, 0, 320, 320, settings.ConcertPortraitZoomFactor);
            return "https://wsrv.nl/?url=" + Uri.EscapeDataString(entry.Url) + crop + "&precrop&w=128&h=128&output=png";
        }
        internal static string CropCoordinates(double x, double y, double width, double height, double factor, bool percent = false)
        {
            factor = Math.Max(1.0, Math.Min(2.0, factor));
            double croppedWidth = width / factor, croppedHeight = height / factor;
            string format = percent ? "0.###" : "0";
            string suffix = percent ? "%25" : "";
            Func<double, string> number = value => value.ToString(format, CultureInfo.InvariantCulture) + suffix;
            return "&cx=" + number(x + (width - croppedWidth) / 2) + "&cy=" + number(y + (height - croppedHeight) / 2) + "&cw=" + number(croppedWidth) + "&ch=" + number(croppedHeight);
        }
        public static async Task RefreshMissing(ConcertSnapshot concert, Settings settings)
        {
            if (!settings.AutoRefreshOutfitPortraits || concert == null) return;
            Load();
            var pending = concert.MainPortraitSlots.Select(slot => concert.DressFor(slot)).Distinct()
                .Where(id => entries.ContainsKey(id) && string.IsNullOrEmpty(entries[id].Url) && DateTime.UtcNow >= entries[id].NextLookup)
                .Select(id => entries[id]).ToList();
            if (pending.Count == 0) return;
            foreach (var entry in pending) entry.NextLookup = DateTime.UtcNow.AddHours(24);
            try
            {
                string titles = string.Join("|", pending.Select(e => "File:Game Asset chara stand " + e.CharacterId + " " + e.DressId.ToString("D6") + ".png"));
                string url = "https://umamusu.wiki/w/api.php?action=query&format=json&prop=imageinfo&iiprop=url&titles=" + Uri.EscapeDataString(titles);
                string json = await ArtworkDownload.WikiText(url);
                int updated = ApplyLookup(json, pending);
                if (updated > 0)
                {
                    File.WriteAllText(CachePath, Json.Encode(entries.Values.Where(e => ValidWikiUrl(e.Url)).ToList()), new UTF8Encoding(false));
                    Status = "Added " + updated + " new outfit portraits automatically"; Log.Write(Status);
                }
            }
            catch (Exception ex) { Log.Write("Optional portrait refresh deferred: " + ex.Message); }
        }
        internal static int ApplyLookup(string json, List<OutfitEntry> pending)
        {
            var response = Json.Decode<WikiResponse>(json);
            int updated = 0;
            if (response.query != null && response.query.pages != null) foreach (var page in response.query.pages.Values)
            {
                if (page.imageinfo == null || page.imageinfo.Length == 0 || !ValidWikiUrl(page.imageinfo[0].url)) continue;
                var entry = pending.FirstOrDefault(e => (page.title ?? "").Replace('_', ' ') == "File:Game Asset chara stand " + e.CharacterId + " " + e.DressId.ToString("D6") + ".png");
                if (entry == null) continue;
                entry.Url = page.imageinfo[0].url; entry.Style = "stand"; updated++;
            }
            return updated;
        }
        private static bool ValidWikiUrl(string value)
        {
            Uri uri;
            return Uri.TryCreate(value, UriKind.Absolute, out uri) && uri.Scheme == "https" && uri.Host == "umamusu.wiki" && uri.AbsolutePath.StartsWith("/w/images/", StringComparison.Ordinal);
        }
        internal static byte[] Utf8(string value) { return Encoding.UTF8.GetBytes(value + "\0"); }
        internal static void Query(IntPtr database, string sql, Action<IntPtr> row)
        {
            IntPtr statement;
            if (Sqlite.Prepare(database, Utf8(sql), -1, out statement, IntPtr.Zero) != 0) throw new IOException("Game metadata query unavailable");
            try { int result; while ((result = Sqlite.Step(statement)) == 100) row(statement); if (result != 101) throw new IOException("Game metadata read interrupted"); }
            finally { Sqlite.Finalize(statement); }
        }

        private sealed class WikiResponse { public WikiQuery query { get; set; } }
        private sealed class WikiQuery { public Dictionary<string, WikiPage> pages { get; set; } }
        private sealed class WikiPage { public string title { get; set; } public WikiImage[] imageinfo { get; set; } }
        private sealed class WikiImage { public string url { get; set; } }
        internal static class Sqlite
        {
            [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_column_text", CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr ColumnText(IntPtr statement, int column);
            [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_column_bytes", CallingConvention = CallingConvention.Cdecl)] internal static extern int ColumnBytes(IntPtr statement, int column);
            [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_open_v2", CallingConvention = CallingConvention.Cdecl)] internal static extern int Open(byte[] path, out IntPtr db, int flags, IntPtr vfs);
            [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_close", CallingConvention = CallingConvention.Cdecl)] internal static extern int Close(IntPtr db);
            [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_prepare_v2", CallingConvention = CallingConvention.Cdecl)] internal static extern int Prepare(IntPtr db, byte[] sql, int bytes, out IntPtr statement, IntPtr tail);
            [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_step", CallingConvention = CallingConvention.Cdecl)] internal static extern int Step(IntPtr statement);
            [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_column_int", CallingConvention = CallingConvention.Cdecl)] internal static extern int ColumnInt(IntPtr statement, int column);
            [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_finalize", CallingConvention = CallingConvention.Cdecl)] internal static extern int Finalize(IntPtr statement);
        }
    }
}
