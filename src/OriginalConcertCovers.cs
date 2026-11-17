using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace UmapyonPresence
{
    internal sealed class OriginalConcertCover
    {
        public string Url { get; set; }
        public string Source { get; set; }
        public string WikiPage { get; set; }
    }
    internal static class OriginalConcertCovers
    {
        private static readonly object sync = new object();
        private static Dictionary<string, OriginalConcertCover> covers;
        private static readonly Dictionary<int, DateTime> retry = new Dictionary<int, DateTime>();
        private static string CachePath { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "original-cover-cache.json"); } }
        internal static string Status { get; private set; } = "Original song artwork available from bundled catalog";
        internal static bool ValidUrl(string text)
        {
            Uri url;
            if (string.IsNullOrWhiteSpace(text) || !Uri.TryCreate(text, UriKind.Absolute, out url) || url.Scheme != "https" || url.Port != 443 || url.UserInfo.Length != 0 || url.Query.Length != 0 || url.Fragment.Length != 0) return false;
            return (url.Host == "umamusu.wiki" && Regex.IsMatch(url.AbsolutePath, @"^/w/images/[0-9a-f]/[0-9a-f]{2}/[^/]+\.(?:png|jpg|jpeg|webp)$", RegexOptions.IgnoreCase)) ||
                (url.Host == "cdn.discordapp.com" && Regex.IsMatch(url.AbsolutePath, @"^/app-assets/954453106765225995/\d+\.png$"));
        }
        private static void Load()
        {
            if (covers != null) return;
            covers = SongCatalog.Resource<Dictionary<string, OriginalConcertCover>>("original-concert-covers.json");
            try {
                if (File.Exists(CachePath) && new FileInfo(CachePath).Length <= 1024 * 1024) {
                    var saved = Json.Decode<Dictionary<string, OriginalConcertCover>>(File.ReadAllText(CachePath, Encoding.UTF8));
                    if (saved != null) foreach (var entry in saved) if (entry.Value != null && ValidUrl(entry.Value.Url)) covers[entry.Key] = entry.Value;
                }
            } catch (Exception ex) { Log.Write("Original cover cache skipped: " + ex.Message); }
        }
        internal static string Url(int id)
        {
            lock (sync) {
                Load(); OriginalConcertCover cover;
                if (!covers.TryGetValue(id.ToString(), out cover) || cover == null || !ValidUrl(cover.Url)) return null;
                // Public original artwork, resized by the same proxy used for portraits.
                // Discord cannot retrieve local PNGs or another application's asset key.
                string image = "https://wsrv.nl/?url=" + Uri.EscapeDataString(cover.Url.Substring(8)) + "&w=512&h=512&fit=contain&output=png";
                return image.Length <= 256 ? image : null;
            }
        }
        internal static string Source(int id)
        {
            lock (sync) { Load(); OriginalConcertCover cover; return covers.TryGetValue(id.ToString(), out cover) && cover != null && ValidUrl(cover.Url) ? cover.Source + " 窶｢ " + cover.Url : "Original cover unavailable for song " + id; }
        }
        private static Task<string> Fetch(string query)
        {
            return ArtworkDownload.WikiText("https://umamusu.wiki/w/api.php?action=query&format=json&" + query);
        }
        internal static string CoverFile(string json, SongEntry song)
        {
            try {
                var response = new JavaScriptSerializer { MaxJsonLength = 1024 * 1024 }.DeserializeObject(json) as Dictionary<string, object>;
                var query = (Dictionary<string, object>)response["query"];
                foreach (var value in ((Dictionary<string, object>)query["pages"]).Values) {
                    var page = (Dictionary<string, object>)value;
                    if (!page.ContainsKey("revisions")) continue;
                    var revision = (Dictionary<string, object>)((object[])page["revisions"])[0];
                    var main = (Dictionary<string, object>)((Dictionary<string, object>)revision["slots"])["main"];
                    string source = (string)main["*"];
                    // Read only the Song infobox, and verify its identity before using cover.
                    if (!Regex.IsMatch(source, @"^\s*\{\{Song\b", RegexOptions.IgnoreCase)) continue;
                    var fields = Regex.Matches(source, @"(?:^|\n)\|\s*(title_jp|title_en|cover)\s*=\s*([^\r\n|]*)").Cast<Match>().ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value.Trim());
                    var names = new[] { song.Title, song.OriginalTitle }.Where(n => !string.IsNullOrWhiteSpace(n)).Select(TextClassifier.Normalize).ToArray();
                    string jp, en, file;
                    bool matches = fields.TryGetValue("title_jp", out jp) && names.Contains(TextClassifier.Normalize(jp)) || fields.TryGetValue("title_en", out en) && names.Contains(TextClassifier.Normalize(en));
                    if (matches && fields.TryGetValue("cover", out file) && Regex.IsMatch(file, @"^[^{}\[\]<>/\\|]+\.(?:png|jpg|jpeg|webp)$", RegexOptions.IgnoreCase)) return "File:" + file;
                }
            } catch (Exception ex) { Log.Write("Original cover metadata skipped: " + ex.Message); }
            return null;
        }
        internal static string ImageUrl(string json, string filename)
        {
            try {
                var response = new JavaScriptSerializer { MaxJsonLength = 1024 * 1024 }.DeserializeObject(json) as Dictionary<string, object>;
                var query = (Dictionary<string, object>)response["query"];
                foreach (var value in ((Dictionary<string, object>)query["pages"]).Values) {
                    var page = (Dictionary<string, object>)value;
                    if (!page.ContainsKey("title") || !string.Equals(((string)page["title"]).Replace('_', ' '), filename.Replace('_', ' '), StringComparison.OrdinalIgnoreCase) || !page.ContainsKey("imageinfo")) continue;
                    var image = (Dictionary<string, object>)((object[])page["imageinfo"])[0];
                    string url = Convert.ToString(image["url"]);
                    if (Convert.ToInt32(image["width"]) == Convert.ToInt32(image["height"]) && Convert.ToInt32(image["width"]) > 0 && ValidUrl(url)) return url;
                }
            } catch (Exception ex) { Log.Write("Original cover image skipped: " + ex.Message); }
            return null;
        }
        internal static async Task RefreshMissing(ConcertSnapshot concert, Settings settings)
        {
            if (concert == null || !concert.Active || concert.MusicId <= 0 || Url(concert.MusicId) != null) return;
            string registered;
            if (!settings.ConcertPreferOriginalCover && settings.ConcertArtwork != null && settings.ConcertArtwork.TryGetValue(concert.MusicId.ToString(), out registered) && !string.IsNullOrWhiteSpace(registered)) return;
            lock (sync) { DateTime next; if (retry.TryGetValue(concert.MusicId, out next) && DateTime.UtcNow < next) return; retry[concert.MusicId] = DateTime.UtcNow.AddHours(1); }
            var song = SongCatalog.Find(concert.MusicId);
            try {
                Status = "Looking up original song cover: " + concert.MusicId;
                string titles = string.Join("|", new[] { song.Title, song.OriginalTitle }.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct());
                string file = CoverFile(await Fetch("prop=revisions&rvprop=content&rvsection=0&rvslots=main&redirects=1&titles=" + Uri.EscapeDataString(titles)), song);
                if (file == null) file = "File:Jacket icon l " + concert.MusicId + ".png";
                string url = ImageUrl(await Fetch("prop=imageinfo&iiprop=url%7Csize&titles=" + Uri.EscapeDataString(file)), file);
                if (url == null) { Status = "Original cover unavailable for song " + concert.MusicId + "; using app icon"; return; }
                lock (sync) { Load(); covers[concert.MusicId.ToString()] = new OriginalConcertCover { Url = url, Source = "Umamusume Wiki song cover" }; File.WriteAllText(CachePath, Json.Encode(covers), new UTF8Encoding(false)); }
                Status = "Original cover found for song " + concert.MusicId;
            } catch (Exception ex) { Status = "Original cover lookup: " + ex.Message; }
        }
    }
}
