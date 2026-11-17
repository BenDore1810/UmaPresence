using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Windows.Media.Control;

namespace UmapyonPresence
{
    internal sealed class SongEntry
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string OriginalTitle { get; set; }
        public int Members { get; set; }
        public int MainCount { get; set; }
    }
    internal static class SongCatalog
    {
        private static List<SongEntry> songs;
        internal static SongEntry[] All { get { Find(0); return songs.ToArray(); } }
        internal static void SetGameSongs(List<SongEntry> current)
        {
            // An incomplete refresh must not turn a known song into "Song <id>".
            var merged = Resource<List<SongEntry>>("songs.json").ToDictionary(song => song.Id);
            foreach (var song in current) if (song != null && song.Id > 0 && !string.IsNullOrWhiteSpace(song.Title)) merged[song.Id] = song;
            songs = merged.Values.OrderBy(song => song.Id).ToList();
        }
        private static HashSet<string> assets;
        private static Dictionary<string, string> portraits;
        private static Dictionary<string, string> studentPortraits;
        private static string Crop(string url, string crop)
        {
            string source = url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? url.Substring(8) : url;
            return "https://wsrv.nl/?url=" + Uri.EscapeDataString(source) + crop + "&precrop&w=128&h=128&fit=cover&output=png";
        }
        public static string OutfitPortrait(int id, int dress, Settings settings)
        {
            string url;
            if (OutfitCatalog.IsRacing(id, dress)) return OutfitCatalog.Portrait(id, dress, settings);
            string original = Portrait(id);
            if (studentPortraits == null) studentPortraits = Resource<Dictionary<string, string>>("student-portraits.json");
            if (studentPortraits.TryGetValue(id.ToString(), out url)) {
                // Official list thumbnails are 296x389 (one is 298x389). Widen the
                // head crop to match racing art, preserve ears, and avoid encoded percentages.
                double zoom = settings.ConcertZoomPortraits ? Math.Max(1, Math.Min(2, settings.ConcertPortraitZoomFactor)) : 1;
                int edge = (int)Math.Round(296 / zoom), x = (296 - edge) / 2;
                return Crop(url, "&cx=" + x + "&cy=0&cw=" + edge + "&ch=" + edge);
            }
            return original == null || !settings.ConcertZoomPortraits ? original : Crop(original, "&cx=24&cy=30&cw=76&ch=76");
        }
        public static string Portrait(int id)
        {
            if (portraits == null) portraits = Resource<Dictionary<string, string>>("character-assets.json");
            string url;
            return portraits.TryGetValue(id.ToString(), out url) ? url : null;
        }
        internal static string FilledPortrait(string url)
        {
            if (string.IsNullOrEmpty(url) || !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return url;
            if (url.StartsWith("https://wsrv.nl/?", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://images.weserv.nl/?", StringComparison.OrdinalIgnoreCase)) return url + "&bg=EEEAF4";
            return "https://wsrv.nl/?url=" + Uri.EscapeDataString(url) + "&w=128&h=128&output=png&bg=EEEAF4";
        }
        internal static T Resource<T>(string name)
        {
            using (var stream = typeof(SongCatalog).Assembly.GetManifestResourceStream(name))
            using (var reader = new StreamReader(stream, Encoding.UTF8)) return Json.Decode<T>(reader.ReadToEnd());
        }
        public static SongEntry Find(int id)
        {
            if (songs == null) songs = Resource<List<SongEntry>>("songs.json");
            return songs.FirstOrDefault(s => s.Id == id) ?? new SongEntry { Id = id, Title = "Song " + id, Members = 0 };
        }
        public static string ImageKey(int id, Settings settings)
        {
            string configured;
            if (!settings.ConcertPreferOriginalCover && settings.ConcertArtwork != null && settings.ConcertArtwork.TryGetValue(id.ToString(), out configured) && !string.IsNullOrWhiteSpace(configured)) return configured;
            string original = OriginalConcertCovers.Url(id);
            if (settings.ConcertPreferOriginalCover) return original ?? settings.LargeImage;
            if (settings.ApplicationId != "954453106765225995") return original ?? settings.LargeImage;
            if (assets == null) assets = new HashSet<string>(Resource<List<string>>("music-assets.json"));
            return assets.Contains("music_" + id) ? "music_" + id : original ?? settings.LargeImage;
        }
    }
    internal sealed class ConcertSnapshot
    {
        public bool Active, Paused;
        public int MusicId;
        public int[] CastIds = new int[0];
        public int[] DressIds = new int[0];
        public double Position, Duration;
        public ulong Tick;
        public int Status;
        public bool NativeCareerResults, NativeLegacyDetails, NativeMenus;
        public int MainPerformerCount = 3;
        public int PortraitIndex;
        public double SingerCycleElapsed;
        public int DressFor(int slot) { return slot >= 0 && slot < DressIds.Length ? DressIds[slot] : 0; }
        public int[] MainPortraitSlots
        {
            get { return Enumerable.Range(0, Math.Min(CastIds.Length, Math.Max(1, Math.Min(18, MainPerformerCount)))).Where(i => CastIds[i] > 0).GroupBy(i => CastIds[i] + ":" + DressFor(i)).Select(g => g.First()).ToArray(); }
        }
        internal int[] PortraitSlots(Settings settings)
        {
            // Costume identity is a page even when several pages share one student image.
            return MainPortraitSlots;
        }
        public string Title { get { return SongCatalog.Find(MusicId).Title; } }
        private static string GroupSuffix(int count)
        {
            var labels = new[] { "", "", "Duo", "Trio", "Quartet", "Quintet", "Sextet", "Septet", "Octet", "Nonet", "Decet" };
            return count < 2 ? "" : " " + (count < labels.Length ? labels[count] : "×" + count);
        }
        public string Singers
        {
            get
            {
                // The ordered list is the actual concert formation, not the song's default artist.
                // The first main formation slots precede the back dancers.
                return string.Join(", ", CastIds.Take(Math.Max(1, Math.Min(18, MainPerformerCount))).Where(id => id > 0).GroupBy(id => id).Select(group => {
                    return ConcertDetails.CharacterName(group.Key, new Settings()) + GroupSuffix(group.Count());
                }).Where(name => name != null));
            }
        }
        public static string TimeText(double seconds) { return TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(@"mm\:ss"); }
        public string SingerLine(Settings settings)
        {
            string all = Singers;
            if (!settings.ConcertCycleSingerNames || string.IsNullOrEmpty(all)) return all;
            string[] names = all.Split(new[] { ", " }, StringSplitOptions.None);
            int page = (int)(Math.Max(0, SingerCycleElapsed) / Math.Max(5, settings.ConcertSingerCycleSeconds)) % names.Length;
            return names[page];
        }
        public object ToActivity(Settings settings, long anchor, long sessionStarted = 0)
        {
            string singers = settings.ShowCharacter ? SingerLine(settings) : "";
            if (string.IsNullOrEmpty(singers)) singers = "Live concert";
            string label = Paused ? "⏸ Paused • " + TimeText(Position) + " / " + TimeText(Duration) : "▶ Umamusume • Live Theater";
            var timestamps = new Dictionary<string, object>();
            if (!Paused) { timestamps["start"] = anchor; timestamps["end"] = anchor + (long)Math.Ceiling(Duration); }
            else timestamps["start"] = sessionStarted > 0 ? sessionStarted : anchor;
            // Discord renders large_text as the album line on Listening cards too.
            // Keep the singer list in the shared album/hover field for a clean Listening card.
            var artwork = new Dictionary<string, object> { { "large_image", SongCatalog.ImageKey(MusicId, settings) }, { "large_text", ActivityState.Limit(singers) } };
            var mainSlots = PortraitSlots(settings);
            int leadSlot = mainSlots.Length == 0 ? -1 : mainSlots[settings.ConcertRotatePortraits ? Math.Abs(PortraitIndex % mainSlots.Length) : 0];
            int leadId = leadSlot < 0 ? 0 : CastIds[leadSlot];
            int dress = DressFor(leadSlot);
            string portrait = settings.ShowCharacter ? SongCatalog.OutfitPortrait(leadId, dress, settings) : null;
            if (portrait != null)
            {
                artwork["small_image"] = SongCatalog.FilledPortrait(portrait);
                artwork["small_text"] = ConcertDetails.CharacterTooltip(leadId, dress, ConcertDetails.CharacterName(leadId, settings), settings);
            }
            var result = new Dictionary<string, object> { { "type", Paused ? 0 : 2 }, { "name", ActivityState.Limit(Title) }, { "status_display_type", 0 }, { "details", ActivityState.Limit(Paused ? label : Title) }, { "state", ActivityState.Limit(Paused ? singers : label) }, { "assets", artwork }, { "instance", false } };
            // Listening + both timestamps requests Discord's native animated time bar.
            // Paused concerts show game-session elapsed time, independent of portrait updates.
            // The song position remains frozen in the details line.
            result["timestamps"] = timestamps;
            return result;
        }
    }
    internal sealed class ConcertReader : IDisposable
    {
        [DllImport("kernel32.dll")] internal static extern ulong GetTickCount64();
        private MemoryMappedFile mapping;
        private MemoryMappedViewAccessor view;
        private int pid;
        private readonly LegacyInspectionReader legacyInspection = new LegacyInspectionReader();
        private readonly LegacyDetailsInspectionReader legacyDetailsInspection = new LegacyDetailsInspectionReader();
        private readonly BareLegacyInspectionReader bareLegacyInspection = new BareLegacyInspectionReader();
        private readonly PaddockInspectionReader paddockInspection = new PaddockInspectionReader();
        private readonly RaceResultInspectionReader raceResultInspection = new RaceResultInspectionReader();
        private readonly OfficialMarginInspection officialMarginInspection = new OfficialMarginInspection();
        internal bool EnableInspectionCompatibility { get; set; }
        public string Status { get; private set; } = "Waiting for UmaPresence telemetry";
        public string OutfitDiagnostics { get; private set; } = "";
        public string CareerDiagnostics { get; private set; } = "";
        public string CareerRaw { get; private set; } = "";
        public string CareerResultRaw { get; private set; } = "";
        public CareerSnapshot Career { get; private set; }
        public NativeMenuSnapshot Menu { get; private set; }
        public RaceSnapshot Race { get; private set; }
        public string RaceDiagnostics { get; private set; } = "";
        public string RaceMetricDiagnostics { get; private set; } = "";
        public ConcertSnapshot Read(int gamePid)
        {
            Career = null;
            Menu = null;
            CareerResultRaw = "";
            Race = null;
            try
            {
                if (gamePid != pid) { Dispose(); pid = gamePid; }
                if (view == null)
                {
                    mapping = MemoryMappedFile.OpenExisting("Local\\UmapyonLive-" + gamePid, MemoryMappedFileRights.Read);
                    view = mapping.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
                }
                if (view.ReadUInt32(0) != 0x554d4150 || view.ReadInt32(4) != 1 || view.ReadInt32(12) != gamePid) { Status = "Unsupported bridge data"; return null; }
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    int seq = view.ReadInt32(8);
                    if ((seq & 1) != 0) continue;
                    var data = new byte[256]; view.ReadArray(0, data, 0, data.Length);
                    byte[] raceBytes = null, raceRivalBytes = null, raceMetricBytes = null;
                    if (view.Capacity >= 16384 && view.ReadUInt32(9216) == 0x524d5431) { raceMetricBytes = new byte[152]; view.ReadArray(9216, raceMetricBytes, 0, raceMetricBytes.Length); }
                    if (view.Capacity >= 8192 && view.ReadUInt32(4096) == 0x52414331)
                    {
                        raceBytes = new byte[100]; view.ReadArray(4096, raceBytes, 0, raceBytes.Length);
                        int length = Math.Min(3000, Math.Max(0, view.ReadInt32(4352)));
                        var debug = new byte[length]; view.ReadArray(4356, debug, 0, length); RaceDiagnostics = Encoding.UTF8.GetString(debug);
                    }
                    if (view.Capacity >= 12572 && view.ReadUInt32(10240) == 0x33564952) { raceRivalBytes = new byte[2332]; view.ReadArray(10240, raceRivalBytes, 0, raceRivalBytes.Length); }
                    else if (view.Capacity >= 12428 && view.ReadUInt32(10240) == 0x32564952) { raceRivalBytes = new byte[2188]; view.ReadArray(10240, raceRivalBytes, 0, raceRivalBytes.Length); }
                    else if (view.Capacity >= 12284 && view.ReadUInt32(10240) == 0x31564952) { raceRivalBytes = new byte[2044]; view.ReadArray(10240, raceRivalBytes, 0, raceRivalBytes.Length); }
                    byte[] careerBytes = null, careerResultBytes = null, menuBytes = null, legacyDetailsBytes = null, grandparentBytes = null, careerExtrasBytes = null;
                    byte[] rivalProfile = null;
                    if (view.Capacity >= 12696 && view.ReadUInt32(12576) == 0x32444c52) { rivalProfile = new byte[view.Capacity >= 12704 ? 128 : 120]; view.ReadArray(12576, rivalProfile, 0, rivalProfile.Length); }
                    else if (view.Capacity >= 12544 && view.ReadUInt32(12432) == 0x31444c52) { rivalProfile = new byte[112]; view.ReadArray(12432, rivalProfile, 0, rivalProfile.Length); }
                    if (view.Capacity >= 16384 && view.ReadUInt32(9728) == 0x43455831) { careerExtrasBytes = new byte[232]; view.ReadArray(9728, careerExtrasBytes, 0, careerExtrasBytes.Length); }
                    if (view.Capacity >= 16384 && view.ReadUInt32(8192) == 0x4c475031) { grandparentBytes = new byte[552]; view.ReadArray(8192, grandparentBytes, 0, grandparentBytes.Length); }
                    if (view.Capacity >= 8192 && view.ReadUInt32(7680) == 0x4c455831) { legacyDetailsBytes = new byte[316]; view.ReadArray(7680, legacyDetailsBytes, 0, legacyDetailsBytes.Length); }
                    if (view.Capacity >= 8192 && view.ReadUInt32(7424) == 0x4d454e31) { menuBytes = new byte[200]; view.ReadArray(7424, menuBytes, 0, menuBytes.Length); }
                    if (view.Capacity >= 4096 && view.ReadUInt32(2320) == 0x43415231)
                    {
                        careerBytes = new byte[48]; view.ReadArray(2320, careerBytes, 0, careerBytes.Length);
                        CareerRaw = string.Join(",", Enumerable.Range(0, 12).Select(i => BitConverter.ToInt32(careerBytes, i * 4)));
                        int debugLength = Math.Min(1500, Math.Max(0, view.ReadInt32(2376)));
                        var debugBytes = new byte[debugLength]; view.ReadArray(2380, debugBytes, 0, debugLength);
                        CareerDiagnostics = Encoding.UTF8.GetString(debugBytes);
                        if (view.ReadUInt32(3904) == 0x43525331) { careerResultBytes = new byte[32]; view.ReadArray(3904, careerResultBytes, 0, careerResultBytes.Length); CareerResultRaw = string.Join(",", Enumerable.Range(0, 8).Select(i => BitConverter.ToInt32(careerResultBytes, i * 4))); }
                    }
                    if (view.Capacity >= 4096 && view.ReadUInt32(256) == 0x4f444247)
                    {
                        int length = Math.Min(2047, Math.Max(0, view.ReadInt32(260)));
                        var text = new byte[length]; view.ReadArray(264, text, 0, length);
                        OutfitDiagnostics = Encoding.UTF8.GetString(text);
                    }
                    System.Threading.Thread.MemoryBarrier();
                    if (seq != view.ReadInt32(8) || seq != BitConverter.ToInt32(data, 8)) continue;
                    var snapshot = Parse(data, gamePid, GetTickCount64());
                    if (snapshot == null) { Status = "Bridge heartbeat stale or invalid"; return null; }
                    Career = CareerSnapshot.ApplyResult(careerResultBytes, CareerSnapshot.ParseAvailable(careerBytes, CareerDiagnostics));
                    if (careerResultBytes != null && (BitConverter.ToInt32(careerResultBytes, 24) & (16 | 32)) != 0 &&
                        (Career == null || !Career.ApplyRivalProfile(rivalProfile))) Career = null;
                    if (Career != null) Career.Extras = CareerExtras.Parse(careerExtrasBytes, Career);
                    if (Career != null && Career.Legacy) {
                        var details = LegacyDetails.ParsePacket(legacyDetailsBytes);
                        if (details != null) details.ApplyGrandparents(grandparentBytes);
                        if (legacyDetailsBytes == null && EnableInspectionCompatibility) details = legacyDetailsInspection.Read(gamePid, Career);
                        if (details != null && details.Matches(Career)) Career.LegacyDetails = details;
                    }
                    Menu = NativeMenuSnapshot.Parse(menuBytes);
                    if (EnableInspectionCompatibility && snapshot.NativeLegacyDetails && snapshot.NativeMenus) legacyInspection.Retire();
                    else if (EnableInspectionCompatibility)
                    {
                        var inspectedCareer = legacyInspection.Read(gamePid);
                        if (!snapshot.NativeLegacyDetails) Career = inspectedCareer ?? Career;
                        if (!snapshot.NativeMenus) Menu = legacyInspection.Menu;
                    }
                    Race = RaceSnapshot.Parse(raceBytes);
                    if (EnableInspectionCompatibility) Race = paddockInspection.Read(gamePid, Menu, Race) ?? Race;
                    if (EnableInspectionCompatibility) Race = raceResultInspection.Read(gamePid, Menu, Race) ?? Race;
                    if (raceRivalBytes != null) RaceSnapshot.ApplyRivals(raceRivalBytes, Race);
                    RaceMetrics.Apply(raceMetricBytes, Race);
                    if (EnableInspectionCompatibility) officialMarginInspection.Read(gamePid, Race);
                    Career = bareLegacyInspection.Read(gamePid, Career) ?? Career;
                    RaceMetricDiagnostics = raceMetricBytes == null ? "RMT1 unavailable" : "RMT1 active=" + BitConverter.ToInt32(raceMetricBytes, 4) + " stage=" + BitConverter.ToInt32(raceMetricBytes, 24) + " official=" + BitConverter.ToInt32(raceMetricBytes, 28) + " phase=" + BitConverter.ToInt32(raceMetricBytes, 20);
                    Status = snapshot.Status == 2 ? "Bridge stopped: incompatible game metadata" : snapshot.Active ? "Live playback telemetry connected" : "Bridge connected • outside concert playback";
                    if (Career != null) Status = Career.Legacy ? "Legacy detail telemetry connected" : Career.Results ? "Career result telemetry connected" : "Career telemetry connected";
                    if (Race != null && (Career == null || !Career.Legacy)) Status = Race.Results ? "Race result telemetry connected" : "Race progress telemetry connected";
                    return snapshot;
                }
                Status = "Waiting for complete bridge sample";
            }
            catch (FileNotFoundException) { Status = "Bridge not loaded • restart the game after installation"; }
            catch (UnauthorizedAccessException) { Status = "Windows denied access to the local bridge mapping"; }
            catch (Exception ex) { Status = "Bridge: " + ex.Message; Dispose(); }
            return null;
        }
        internal static ConcertSnapshot Parse(byte[] data, int expectedPid, ulong now)
        {
            if (data == null || data.Length < 256 || BitConverter.ToUInt32(data, 0) != 0x554d4150 || BitConverter.ToInt32(data, 4) != 1 || BitConverter.ToInt32(data, 12) != expectedPid || (BitConverter.ToInt32(data, 8) & 1) != 0) return null;
            ulong tick = BitConverter.ToUInt64(data, 16);
            if (tick > now || now - tick > 10000) return null;
            int count = BitConverter.ToInt32(data, 36);
            double position = BitConverter.ToDouble(data, 40), duration = BitConverter.ToDouble(data, 48);
            if (count < 0 || count > 18 || double.IsNaN(position) || double.IsInfinity(position) || double.IsNaN(duration) || double.IsInfinity(duration) || position < 0 || duration < 0 || duration > 7200 || position > duration + 5) return null;
            var result = new ConcertSnapshot { Tick = tick, Active = BitConverter.ToInt32(data, 24) == 1, Paused = BitConverter.ToInt32(data, 28) == 1, MusicId = BitConverter.ToInt32(data, 32), Position = position, Duration = duration, Status = BitConverter.ToInt32(data, 128), CastIds = new int[count] };
            if (BitConverter.ToUInt32(data, 208) == 0x314e4143) { int capabilities = BitConverter.ToInt32(data, 212); result.NativeCareerResults = (capabilities & 1) != 0; result.NativeLegacyDetails = (capabilities & 2) != 0; result.NativeMenus = (capabilities & 4) != 0; }
            for (int i = 0; i < count; i++) result.CastIds[i] = BitConverter.ToInt32(data, 56 + i * 4);
            if (BitConverter.ToUInt32(data, 204) == 0x44525331)
            {
                result.DressIds = new int[count];
                for (int i = 0; i < count; i++) { int dress = BitConverter.ToInt32(data, 132 + i * 4); result.DressIds[i] = dress > 0 && dress < 1000000 ? dress : 0; }
            }
            if (result.Active && (result.Duration <= 0 || result.MusicId <= 0)) return null;
            return result;
        }
        public void Dispose() { if (view != null) view.Dispose(); if (mapping != null) mapping.Dispose(); view = null; mapping = null; }
    }
    internal sealed class ConcertTracker
    {
        private long anchor;
        private int music;
        private bool wasPaused;
        public ActivityState Observe(ConcertSnapshot sample, DateTimeOffset now)
        {
            if (sample == null || !sample.Active) { Reset(); return null; }
            double current = sample.Position + (sample.Paused ? 0 : Math.Max(0, (ConcertReader.GetTickCount64() - sample.Tick) / 1000.0));
            long candidate = (long)Math.Round(now.ToUnixTimeMilliseconds() / 1000.0 - current);
            if (anchor == 0 || music != sample.MusicId || wasPaused != sample.Paused || Math.Abs(candidate - anchor) > 1) anchor = candidate;
            music = sample.MusicId; wasPaused = sample.Paused;
            return new ActivityState { Kind = "Live", Details = sample.Title, State = sample.Singers + (sample.Paused ? " • Paused" : ""), Source = "Native concert bridge", Concert = sample, ConcertStart = anchor };
        }
        public void Reset() { anchor = 0; music = 0; wasPaused = false; }
    }
    internal sealed class ConcertArtworkBuilder
    {
        private GlobalSystemMediaTransportControlsSessionManager manager;
        private int savedSong;
        private DateTime retry;
        public string Status { get; private set; } = "Waiting for song cover";
        internal void RetryCover() { savedSong = 0; retry = DateTime.MinValue; }
        public async Task SaveCover(ConcertSnapshot concert)
        {
            if (concert == null || !concert.Active || savedSong == concert.MusicId || DateTime.UtcNow < retry) return;
            retry = DateTime.UtcNow.AddSeconds(10);
            try
            {
                if (manager == null) manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                foreach (var session in manager.GetSessions().Where(s => s.SourceAppUserModelId == "Cygames.Gallop" || s.SourceAppUserModelId.IndexOf("umamusume", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    var media = await session.TryGetMediaPropertiesAsync();
                    var song = SongCatalog.Find(concert.MusicId);
                    string mediaName = TextClassifier.Normalize(media.Title);
                    if (mediaName != TextClassifier.Normalize(song.Title) && mediaName != TextClassifier.Normalize(song.OriginalTitle)) continue;
                    if (media.Thumbnail == null) continue;
                    using (var input = await media.Thumbnail.OpenReadAsync())
                    using (var stream = input.AsStreamForRead())
                    using (var cover = Image.FromStream(stream))
                    using (var specialStream = typeof(Program).Assembly.GetManifestResourceStream("special-week.png"))
                    using (var special = Image.FromStream(specialStream))
                    using (var image = Compose(special, cover))
                    {
                        string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "generated-artwork"); Directory.CreateDirectory(folder);
                        string filename = Path.Combine(folder, "concert_" + concert.MusicId + ".png");
                        image.Save(filename, ImageFormat.Png);
                        savedSong = concert.MusicId; Status = "Diagonal artwork ready: " + filename;
                        Log.Write(Status);
                    }
                    return;
                }
                Status = "Waiting for Hachimi media thumbnail (enable_smtc)";
            }
            catch (Exception ex) { Status = "Cover artwork: " + ex.Message; }
        }
        internal static Bitmap Compose(Image special, Image cover)
        {
            var bitmap = new Bitmap(512, 512, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                using (var clip = new GraphicsPath())
                {
                    clip.AddPolygon(new[] { new Point(0, 0), new Point(512, 0), new Point(0, 512) }); graphics.SetClip(clip); graphics.DrawImage(special, new Rectangle(0, 0, 512, 512));
                }
                graphics.ResetClip();
                using (var clip = new GraphicsPath())
                {
                    clip.AddPolygon(new[] { new Point(512, 0), new Point(512, 512), new Point(0, 512) }); graphics.SetClip(clip); graphics.DrawImage(cover, new Rectangle(0, 0, 512, 512));
                }
                graphics.ResetClip();
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var pen = new Pen(Color.White, 4)) graphics.DrawLine(pen, 512, 0, 0, 512);
            }
            return bitmap;
        }
    }
}
