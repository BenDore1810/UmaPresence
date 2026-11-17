using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace UmapyonPresence
{
    // Local maintenance only. This layer does not format any presence tooltip.
    internal static class DataMaintenance
    {
        internal static string LastGameRefresh = "Not refreshed manually";
        internal static string LastTranslationRefresh = "Not refreshed manually";
        private sealed class RefreshHistory { public string Game { get; set; } public string Translations { get; set; } }
        private static string HistoryPath { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "maintenance-history.json"); } }
        static DataMaintenance()
        {
            try { if (File.Exists(HistoryPath)) { var history = Json.Decode<RefreshHistory>(File.ReadAllText(HistoryPath)); LastGameRefresh = history.Game ?? LastGameRefresh; LastTranslationRefresh = history.Translations ?? LastTranslationRefresh; } } catch (Exception ex) { Log.Write("Refresh history unavailable: " + ex.Message); }
        }
        private static void SaveHistory()
        {
            try { File.WriteAllText(HistoryPath, Json.Encode(new RefreshHistory { Game = LastGameRefresh, Translations = LastTranslationRefresh }), new UTF8Encoding(false)); } catch (Exception ex) { Log.Write("Could not save refresh history: " + ex.Message); }
        }
        private static string automaticStamp;
        private static Dictionary<string, Dictionary<string, string>> automaticTranslations;
        internal static void RefreshAutomatic(Settings settings)
        {
            string path = MasterPath(settings); if (!File.Exists(path)) return;
            var file = new FileInfo(path); string stamp = path + "|" + file.Length + "|" + file.LastWriteTimeUtc.Ticks;
            var translations = ReadTranslations(settings);
            bool masterChanged = automaticStamp != stamp;
            if (!masterChanged && ReferenceEquals(automaticTranslations, translations)) return;
            try { ReloadSongs(settings); if (masterChanged) CareerRanks.Reload(settings); }
            catch (Exception ex) { Log.Write("Automatic song/rank refresh: " + ex.Message); }
            automaticStamp = stamp;
            automaticTranslations = translations;
        }
        internal static string MasterPath(Settings settings) { return Path.Combine(settings.GameDirectory ?? "", "umamusume_Data", "Persistent", "master", "master.mdb"); }
        internal static string TranslationPath(Settings settings) { return Path.Combine(settings.GameDirectory ?? "", "hachimi", "localized_data", "text_data_dict.json"); }
        internal static string TextColumn(IntPtr row, int column)
        {
            int size = OutfitCatalog.Sqlite.ColumnBytes(row, column); IntPtr pointer = OutfitCatalog.Sqlite.ColumnText(row, column);
            if (size < 1 || size > 32768 || pointer == IntPtr.Zero) return "";
            var bytes = new byte[size]; System.Runtime.InteropServices.Marshal.Copy(pointer, bytes, 0, size); return Encoding.UTF8.GetString(bytes);
        }
        private static readonly Dictionary<string, Dictionary<string, string>> noTranslations = new Dictionary<string, Dictionary<string, string>>();
        private static readonly object translationLock = new object();
        private static string translationPath, translationStamp, translationStatus;
        private static DateTime translationCheck;
        private static Dictionary<string, Dictionary<string, string>> validTranslations;

        internal static Dictionary<string, Dictionary<string, string>> ReadTranslations(Settings settings, bool refresh = false)
        {
            if (string.IsNullOrWhiteSpace(settings.GameDirectory)) return noTranslations;
            string path = Path.GetFullPath(TranslationPath(settings));
            lock (translationLock) {
                if (!string.Equals(path, translationPath, StringComparison.OrdinalIgnoreCase)) {
                    translationPath = path;
                    translationStamp = null;
                    translationCheck = DateTime.MinValue;
                    validTranslations = null;
                }
                if (!refresh && DateTime.UtcNow < translationCheck) return validTranslations ?? noTranslations;
                translationCheck = DateTime.UtcNow.AddSeconds(5);
                try {
                    var file = new FileInfo(path);
                    string stamp = file.Exists ? file.Length + "|" + file.LastWriteTimeUtc.Ticks : "missing";
                    if (!refresh && translationStamp == stamp) return validTranslations ?? noTranslations;
                    translationStamp = stamp;
                    if (!file.Exists) translationStatus = "Hachimi translations not found. " + TranslationFallback();
                    else {
                        const int limit = 16 * 1024 * 1024;
                        if (file.Length > limit) throw new InvalidDataException("Translation dictionary exceeds 16 MB.");
                        string json;
                        using (var input = new StreamReader(path, Encoding.UTF8)) {
                            var content = new StringBuilder();
                            var buffer = new char[8192];
                            int read;
                            while ((read = input.Read(buffer, 0, buffer.Length)) != 0) {
                                if (content.Length + read > limit) throw new InvalidDataException("Translation dictionary exceeds the text limit.");
                                content.Append(buffer, 0, read);
                            }
                            json = content.ToString();
                        }
                        var serializer = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = limit };
                        var dictionary = serializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(json);
                        if (dictionary == null || dictionary.Values.Any(category => category == null))
                            throw new InvalidDataException("Translation dictionary contains invalid categories.");
                        validTranslations = dictionary;
                        translationStatus = "Hachimi translations reloaded: " + dictionary.Count + " categories.";
                    }
                } catch (Exception error) when (error is IOException || error is InvalidDataException || error is UnauthorizedAccessException
                    || error is ArgumentException || error is InvalidOperationException) {
                    translationStatus = "Could not read Hachimi translations. " + TranslationFallback();
                    Log.Write("Translation refresh skipped: " + error.Message);
                }
                return validTranslations ?? noTranslations;
            }
        }
        private static string TranslationFallback() => validTranslations == null ? "Using bundled labels." : "Keeping the last working translations.";
        internal static string FindTranslation(string category, int id, Settings settings)
        {
            if (ReadTranslations(settings).TryGetValue(category, out Dictionary<string, string> translations)
                && translations.TryGetValue(id.ToString(), out string value) && !string.IsNullOrWhiteSpace(value) && value != "None")
                return value;
            return null;
        }
        internal static string ReloadSongs(Settings settings)
        {
            IntPtr database = IntPtr.Zero;
            try {
                if (OutfitCatalog.Sqlite.Open(OutfitCatalog.Utf8(MasterPath(settings)), out database, 1, IntPtr.Zero) != 0) throw new IOException("Cannot open game master read-only");
                var titles = new Dictionary<int, string>();
                OutfitCatalog.Query(database, "select \"index\",text from text_data where category=16", row => { titles[OutfitCatalog.Sqlite.ColumnInt(row, 0)] = TextColumn(row, 1); });
                var bundled = SongCatalog.Resource<List<SongEntry>>("songs.json").ToDictionary(song => song.Id);
                Dictionary<string, string> translations;
                ReadTranslations(settings).TryGetValue("16", out translations);
                var songs = new List<SongEntry>();
                OutfitCatalog.Query(database, "select music_id,live_member_number,backdancer_order from live_data where has_live=1", row => {
                    int id = OutfitCatalog.Sqlite.ColumnInt(row, 0), members = OutfitCatalog.Sqlite.ColumnInt(row, 1), back = OutfitCatalog.Sqlite.ColumnInt(row, 2);
                    string original, title; SongEntry known;
                    // Total formation size is metadata, independent of the bridge's
                    // 18 captured cast slots. l'Arc de gloire has 20 performers.
                    if (!titles.TryGetValue(id, out original) || string.IsNullOrWhiteSpace(original) || id < 1 || members < 1 || members > 64) return;
                    bundled.TryGetValue(id, out known);
                    title = known == null ? original : known.Title;
                    string translated;
                    if (translations != null && translations.TryGetValue(id.ToString(), out translated) && !string.IsNullOrWhiteSpace(translated) && translated != "None") title = translated;
                    songs.Add(new SongEntry { Id = id, Title = title, OriginalTitle = original, Members = members, MainCount = Math.Max(1, Math.Min(members, back > 0 ? back - 1 : members)) });
                });
                if (songs.Count == 0) throw new IOException("No valid concert songs in game master");
                SongCatalog.SetGameSongs(songs.GroupBy(song => song.Id).Select(group => group.First()).ToList());
                return songs.Count + " concert songs loaded";
            } finally { if (database != IntPtr.Zero) OutfitCatalog.Sqlite.Close(database); }
        }
        internal static string RefreshGame(Settings settings)
        {
            if (!File.Exists(MasterPath(settings))) throw new FileNotFoundException("Choose the game folder containing umamusume_Data\\Persistent\\master\\master.mdb first.");
            var report = new List<string>();
            Action<string, Action> run = (name, action) => { try { action(); report.Add(name + ": refreshed"); } catch (Exception ex) { report.Add(name + ": FAILED — " + ex.Message); } };
            run("Original game labels", () => GameText.Reload(settings));
            run("Outfits / cast metadata", () => OutfitCatalog.Reload(settings));
            report.Add(OutfitCatalog.Status);
            run("Legacy factors", () => LegacyDetails.Reload(settings));
            try { report.Add(ReloadSongs(settings)); } catch (Exception ex) { report.Add("Songs: FAILED — " + ex.Message); }
            try { report.Add(CareerRanks.Reload(settings)); } catch (Exception ex) { report.Add("Ranks: FAILED — " + ex.Message); }
            LastGameRefresh = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\r\n" + string.Join("\r\n", report);
            SaveHistory();
            Log.Write("Manual game-data refresh: " + LastGameRefresh.Replace("\r\n", "; ")); return LastGameRefresh;
        }
        internal static string RefreshTranslations(Settings settings)
        {
            ReadTranslations(settings, refresh: true);
            string songs = File.Exists(MasterPath(settings)) ? ReloadSongs(settings) : "Song metadata unchanged (game master unavailable)";
            LastTranslationRefresh = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\r\n" +
                (string.IsNullOrWhiteSpace(settings.GameDirectory) ? "Choose a game folder to reload Hachimi translations." : translationStatus) + "\r\n" + songs;
            SaveHistory();
            return LastTranslationRefresh;
        }
        internal static bool ValidAsset(string asset) { return !string.IsNullOrWhiteSpace(asset) && Regex.IsMatch(asset, "^[a-z0-9_]{1,128}$"); }
        internal static void RegisterAsset(Settings settings, int songId, string asset)
        {
            if (songId < 1 || !ValidAsset(asset)) throw new ArgumentException("Use a positive song ID and a lowercase Discord asset name containing letters, digits or underscores.");
            if (settings.ConcertArtwork == null) settings.ConcertArtwork = new Dictionary<string, string>();
            string previous; bool hadPrevious = settings.ConcertArtwork.TryGetValue(songId.ToString(), out previous);
            settings.ConcertArtwork[songId.ToString()] = asset;
            try { settings.Save(); } catch { if (hadPrevious) settings.ConcertArtwork[songId.ToString()] = previous; else settings.ConcertArtwork.Remove(songId.ToString()); throw; }
        }
        internal static string BridgeStatus(Settings settings, int gamePid)
        {
            string configured = "Not found", loaded = gamePid == 0 ? "Game not running" : "Not verified";
            try {
                string path = Path.Combine(settings.GameDirectory ?? "", "hachimi", "config.json");
                if (File.Exists(path)) {
                    var config = Json.Decode<Dictionary<string, object>>(File.ReadAllText(path)); object libraries;
                    if (config.TryGetValue("load_libraries", out libraries)) {
                        var matches = Regex.Matches(Json.Encode(libraries), @"umapyon_bridge[^""\\/]*\.dll", RegexOptions.IgnoreCase);
                        configured = matches.Count == 0 ? "No UmaPresence bridge configured" : string.Join(", ", matches.Cast<Match>().Select(match => match.Value));
                    }
                }
                if (gamePid != 0) using (var process = Process.GetProcessById(gamePid)) {
                    var modules = process.Modules.Cast<ProcessModule>().Where(module => module.ModuleName.StartsWith("umapyon_bridge", StringComparison.OrdinalIgnoreCase)).Select(module => module.ModuleName).ToArray();
                    loaded = modules.Length == 0 ? "No module found" : string.Join(", ", modules);
                }
            } catch (Exception ex) { loaded = "Cannot verify loaded module: " + ex.Message; }
            return "Configured for next game launch: " + configured + "\r\nLoaded in current game: " + loaded + "\r\nData refresh does not install or reload a bridge.";
        }
        internal static string Sources(Settings settings)
        {
            Func<string, string> describe = path => File.Exists(path) ? path + "\r\nModified: " + File.GetLastWriteTime(path).ToString("yyyy-MM-dd HH:mm:ss") : path + "\r\nNot found";
            return "GAME MASTER\r\n" + describe(MasterPath(settings)) + "\r\n\r\nHACHIMI TRANSLATIONS\r\n" + describe(TranslationPath(settings)) +
                "\r\n\r\nLAST MANUAL GAME REFRESH\r\n" + LastGameRefresh + "\r\n\r\nLAST TRANSLATION RELOAD\r\n" + LastTranslationRefresh;
        }
    }
}
