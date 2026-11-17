using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace UmapyonPresence
{
    internal static class Json
    {
        public static string Encode(object value) { return new JavaScriptSerializer().Serialize(value); }
        public static T Decode<T>(string value) { return new JavaScriptSerializer().Deserialize<T>(value); }
    }

    internal sealed class Settings
    {
        // Public application ID and asset names used by UmaLauncher. No token or login needed.
        public string ApplicationId { get; set; } = ReleasePolicy.PublicRelease ? "1563519446503522344" : "954453106765225995";
        public bool ScreenDetection { get; set; } = false;
        public bool OcrDetection { get; set; } = false;
        public int ScanIntervalSeconds { get; set; } = 8;
        public int UpdateIntervalSeconds { get; set; } = 15;
        public string OcrLanguage { get; set; } = "auto";
        public string LargeImage { get; set; } = "umaicon";
        public bool ShowCharacter { get; set; } = true;
        public bool StartMinimized { get; set; }
        public bool WelcomeDismissed { get; set; } = true;
        public bool TrayCloseHintShown { get; set; }
        public string ManualCharacter { get; set; } = "";
        public string ManualCharacterId { get; set; } = "";
        public string[] ProcessNames { get; set; } = new[] { "umamusume" };
        public string GameDirectory { get; set; } = "";
        public string GameIntegrationMode { get; set; } = ReleasePolicy.PublicRelease ? "Standalone" : "Hachimi";
        public bool StandaloneWindowsMediaControls { get; set; } = true;
        public string StandaloneSeekBarMode { get; set; } = "Paused";
        public bool ConcertAllowDuplicates { get; set; } = false;
        public bool StandaloneConcertPreferencesImported { get; set; }
        public string ConcertAudioMode { get; set; } = ReleasePolicy.PublicRelease ? "Original" : "Balanced";
        public int ConcertVoice1 { get; set; }
        public int ConcertVoice2 { get; set; }
        public bool ConcertBridge { get; set; } = true;
        public int ConcertMainPerformerCount { get; set; } = 0;
        public int ConcertPortraitIntervalSeconds { get; set; } = 3;
        public bool ConcertRotatePortraits { get; set; } = true;
        public bool ConcertPreferOriginalCover { get; set; } = false;
        public bool ConcertZoomPortraits { get; set; } = true;
        public double ConcertPortraitZoomFactor { get; set; } = 1.0;
        public bool AutoRefreshOutfitPortraits { get; set; } = true;
        public bool ConcertCycleSingerNames { get; set; } = false;
        public int ConcertSingerCycleSeconds { get; set; } = 5;
        public Dictionary<string, string> ConcertOutfitPortraits { get; set; } = new Dictionary<string, string>();
        public Dictionary<string, string> ConcertArtwork { get; set; } = new Dictionary<string, string>();
        public Dictionary<string, string> CareerPortraitAssets { get; set; } = new Dictionary<string, string>();

        public static string FilePath { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json"); } }
        public static Settings Load()
        {
            Settings result = File.Exists(FilePath) ? Json.Decode<Settings>(File.ReadAllText(FilePath, Encoding.UTF8)) : new Settings();
            if (result == null || result.ApplicationId == null || result.ApplicationId.Length < 5 || !result.ApplicationId.All(char.IsDigit))
                throw new InvalidDataException("settings.json: ApplicationId must be a Discord application ID.");
            result.ScanIntervalSeconds = Math.Max(5, Math.Min(60, result.ScanIntervalSeconds));
            result.UpdateIntervalSeconds = Math.Max(15, Math.Min(300, result.UpdateIntervalSeconds));
            result.ConcertPortraitIntervalSeconds = Math.Max(3, Math.Min(60, result.ConcertPortraitIntervalSeconds));
            if (result.GameIntegrationMode != "Hachimi" && result.GameIntegrationMode != "Standalone") result.GameIntegrationMode = "Hachimi";
            if (result.StandaloneSeekBarMode != "Paused" && result.StandaloneSeekBarMode != "Always" && result.StandaloneSeekBarMode != "Off") result.StandaloneSeekBarMode = "Paused";
            if (!new[] { "Balanced", "Original", "Lead", "Solo", "Duet" }.Contains(result.ConcertAudioMode)) result.ConcertAudioMode = "Balanced";
            result.ConcertVoice1 = Math.Max(0, Math.Min(1999, result.ConcertVoice1)); result.ConcertVoice2 = Math.Max(0, Math.Min(1999, result.ConcertVoice2));
            result.ConcertSingerCycleSeconds = Math.Max(5, Math.Min(60, result.ConcertSingerCycleSeconds));
            result.ConcertPortraitZoomFactor = double.IsNaN(result.ConcertPortraitZoomFactor) || double.IsInfinity(result.ConcertPortraitZoomFactor) ? 1.0 : Math.Max(1.0, Math.Min(2.0, result.ConcertPortraitZoomFactor));
            result.ProcessNames = (result.ProcessNames ?? new[] { "umamusume" }).Where(s => !string.IsNullOrWhiteSpace(s)).Select(Path.GetFileNameWithoutExtension).ToArray();
            if (result.ProcessNames.Length == 0) result.ProcessNames = new[] { "umamusume" };
            return result;
        }
        public void Save() { File.WriteAllText(FilePath, Json.Encode(this), new UTF8Encoding(false)); }
    }

    internal sealed class ActivityState
    {
        public string Kind = "Playing";
        public string Details = "Playing Umamusume";
        public string State = "DMM version";
        public string Character = "";
        public string CharacterId = "";
        public string Source = "Game process";
        public bool StrongEvidence;
        public string CareerStats = "";
        public CareerSnapshot Career;
        public RaceSnapshot Race;
        public ConcertSnapshot Concert;
        public long ConcertStart;

        public object ToActivity(Settings settings, long start)
        {
            return JapaneseTooltips.Apply(ToActivityCore(settings, start));
        }
        private object ToActivityCore(Settings settings, long start)
        {
            if (Concert != null) return Concert.ToActivity(settings, ConcertStart, start);
            if (Career != null) return Career.ToActivity(settings, start);
            if (Race != null) return Race.ToActivity(settings, start);
            string name = settings.ManualCharacter;
            string id = settings.ManualCharacterId;
            if (string.IsNullOrEmpty(name)) { name = Character; id = CharacterId; }
            var assets = new Dictionary<string, object>();
            if (!string.IsNullOrWhiteSpace(settings.LargeImage))
            {
                assets["large_image"] = settings.LargeImage;
                assets["large_text"] = "Umamusume • DMM";
            }
            if (settings.ShowCharacter && !string.IsNullOrWhiteSpace(name))
            {
                int characterNumber;
                if (Kind == "Training" && int.TryParse(id, out characterNumber))
                {
                    string portrait = SongCatalog.OutfitPortrait(characterNumber, 0, settings);
                    if (!string.IsNullOrEmpty(portrait))
                    {
                        assets["large_image"] = portrait;
                        assets["large_text"] = Limit(name);
                        assets["small_image"] = settings.LargeImage;
                        assets["small_text"] = "Career mode";
                    }
                }
                if (settings.ApplicationId == "954453106765225995" && !string.IsNullOrEmpty(id) && CharacterCatalog.HasIcon(id))
                {
                    assets["large_image"] = "chara_" + id;
                    assets["large_text"] = Limit(name);
                    assets["small_image"] = settings.LargeImage;
                    assets["small_text"] = "DMM version";
                }
            }
            string state = State;
            if (Kind == "Training" && !string.IsNullOrEmpty(CareerStats)) state = CareerStats;
            if (settings.ShowCharacter && !string.IsNullOrWhiteSpace(name)) state = name + (state == "DMM version" ? "" : " • " + state);
            return new Dictionary<string, object> {
                { "type", 0 }, { "name", "Uma Musume : Pretty Derby" }, { "details", Limit(Kind == "Training" && State != "Raising an Umamusume" ? Details + " • " + State : Details) }, { "state", Limit(state) },
                { "timestamps", new Dictionary<string, object> { { "start", start } } }, { "assets", assets }, { "instance", false }
            };
        }

        internal static string Limit(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "Umamusume";
            // Discord limits activity text to 128 UTF-8 bytes.
            while (Encoding.UTF8.GetByteCount(value) > 128)
            {
                int remove = char.IsLowSurrogate(value[value.Length - 1]) && value.Length > 1 ? 2 : 1;
                value = value.Substring(0, value.Length - remove);
            }
            return value.Length < 2 ? value + " " : value;
        }
        public static ActivityState Manual(string mode)
        {
            var result = new ActivityState { Kind = mode, Source = "Tray selection" };
            switch (mode)
            {
                case "Home": result.Details = "Main Menu"; result.State = "Home"; break;
                case "Training": result.Details = "Training"; result.State = "Raising an Umamusume"; break;
                case "Race": result.Details = "Racing"; result.State = "At the racetrack"; break;
                case "Story": result.Details = "Reading stories"; result.State = "Story"; break;
                case "Live": result.Details = "Watching a live"; result.State = "Live Theater"; break;
                case "Gacha": result.Details = "Scouting"; result.State = "Gacha"; break;
            }
            return result;
        }
    }

    internal sealed class CharacterEntry
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Japanese { get; set; }
        public bool HasIcon { get; set; }
    }

    internal static class CharacterCatalog
    {
        private static List<CharacterEntry> entries;
        public static List<CharacterEntry> Entries
        {
            get
            {
                if (entries == null)
                {
                    try
                    {
                        using (var stream = typeof(CharacterCatalog).Assembly.GetManifestResourceStream("characters.json"))
                        using (var reader = new StreamReader(stream, Encoding.UTF8))
                            entries = Json.Decode<List<CharacterEntry>>(reader.ReadToEnd());
                    }
                    catch { entries = new List<CharacterEntry>(); }
                    entries = entries ?? new List<CharacterEntry>();
                }
                return entries;
            }
        }
        public static bool HasIcon(string id) { return Entries.Any(c => c.Id == id && c.HasIcon); }
        public static CharacterEntry FindIn(string text)
        {
            string normalized = TextClassifier.Normalize(text);
            var found = Entries.Where(c => (c.Name.Length >= 5 && normalized.Contains(TextClassifier.Normalize(c.Name))) ||
                (!string.IsNullOrEmpty(c.Japanese) && normalized.Contains(TextClassifier.Normalize(c.Japanese)))).ToList();
            // Several names on a race/story screen are ambiguous. Never guess.
            return found.Count == 1 ? found[0] : null;
        }
    }

    internal static class Log
    {
        private static readonly object sync = new object();
        private static bool removedOldLog;
        public static readonly string FilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "presence.log");
        public static void Write(string message)
        {
            lock (sync)
            {
                try
                {
                    if (!removedOldLog)
                    {
                        try { File.Delete(FilePath + ".old"); removedOldLog = true; }
                        catch (IOException) { }
                        catch (UnauthorizedAccessException) { }
                    }
                    string line = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz") + " " + message + Environment.NewLine;
                    if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 256 * 1024) File.WriteAllText(FilePath, line, Encoding.UTF8);
                    else File.AppendAllText(FilePath, line, Encoding.UTF8);
                }
                catch { }
            }
        }
    }
}
