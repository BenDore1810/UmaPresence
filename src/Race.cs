using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Text;

namespace UmapyonPresence
{
    internal sealed class RaceEntry
    {
        public int CharacterId, Position, Gate, LegacyScore = -1, LegacySkills = -1, OfficialMarginKind;
        public float OfficialGapLengths;
        public string TrainerName = "";
        public bool MarkedRival;
        internal RaceMetrics Metrics;
        public string Name { get { var c = CharacterCatalog.Entries.FirstOrDefault(x => x.Id == CharacterId.ToString()); return c == null ? "Uma " + CharacterId : c.Name; } }
        public string ShortName { get { var words = Name.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries); return words.Length < 2 ? Name : words[0].Substring(0, 1) + ". " + words[words.Length - 1]; } }
    }
    internal sealed class RaceSnapshot
    {
        internal const string SweepHeading = "🏆 PODIUM SWEEP ! 🏆";
        public bool Results, Preparing;
        public int Program, ProgramType, Course, Distance, Kind, Runners, Page, RivalPage;
        // Canonical optional TRK1 values; zero means unavailable, never inferred.
        public int Season, Weather, GroundCondition;
        public double Meters;
        public RaceEntry[] Team = new RaceEntry[0];
        public RaceEntry[] Rivals = new RaceEntry[0];
        internal string Key { get { return Program + "|" + Course + "|" + Results + "|" + Preparing + "|" + string.Join(",", Team.Select(e => e.Gate + ":" + e.CharacterId)); } }
        internal bool PodiumSweep { get { return Results && Team.Length == 3 && Team.Select(e => e.Gate).Distinct().Count() == 3 && Team.Select(e => e.Position).OrderBy(p => p).SequenceEqual(new[] { 1, 2, 3 }); } }
        internal static RaceSnapshot Parse(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 76 || BitConverter.ToUInt32(bytes, 0) != 0x52414331 || BitConverter.ToInt32(bytes, 4) != 1) return null;
            int phase = BitConverter.ToInt32(bytes, 8), count = BitConverter.ToInt32(bytes, 28);
            int distance = BitConverter.ToInt32(bytes, 20), total = BitConverter.ToInt32(bytes, 68);
            float meters = BitConverter.ToSingle(bytes, 24);
            if ((phase != 1 && phase != 2 && phase != 3) || count < 0 || count > 3 || distance < 0 || distance > 10000 || total < 0 || total > 64 || float.IsNaN(meters) || float.IsInfinity(meters) || meters < 0 || meters > 11000) return null;
            var entries = new List<RaceEntry>();
            for (int i = 0; i < count; i++)
            {
                int id = BitConverter.ToInt32(bytes, 32 + i * 4), rank = BitConverter.ToInt32(bytes, 44 + i * 4), gate = BitConverter.ToInt32(bytes, 56 + i * 4);
                if (id < 1000 || id >= 2000 || rank < (phase == 3 ? 0 : 1) || rank > 64 || gate < 0 || gate > 64) return null;
                entries.Add(new RaceEntry { CharacterId = id, Position = rank, Gate = gate });
            }
            // Native HUD/registration slots are authoritative; gates are identifiers, not display order.
            var hudTeam = entries.ToArray();
            var snapshot = new RaceSnapshot { Results = phase == 2, Preparing = phase == 3, Program = BitConverter.ToInt32(bytes, 12), ProgramType = bytes.Length >= 80 ? BitConverter.ToInt32(bytes, 76) : 0, Course = BitConverter.ToInt32(bytes, 16), Distance = distance, Meters = meters, Team = hudTeam, Runners = total, Kind = BitConverter.ToInt32(bytes, 72) };
            if (bytes.Length >= 100 && BitConverter.ToUInt32(bytes, 80) == 0x54524b31 && BitConverter.ToInt32(bytes, 84) == 1) {
                int season = BitConverter.ToInt32(bytes, 88), weather = BitConverter.ToInt32(bytes, 92), ground = BitConverter.ToInt32(bytes, 96);
                snapshot.Season = season >= 1 && season <= 4 ? season : 0;
                snapshot.Weather = weather >= 1 && weather <= 4 ? weather : 0;
                snapshot.GroundCondition = ground >= 1 && ground <= 4 ? ground : 0;
            }
            return snapshot;
        }
        internal static bool ApplyRivals(byte[] bytes, RaceSnapshot race)
        {
            if (race == null || bytes == null) return false;
            bool v3 = bytes.Length == 2332 && BitConverter.ToUInt32(bytes, 0) == 0x33564952 && BitConverter.ToInt32(bytes, 4) == 3;
            bool v2 = bytes.Length == 2188 && BitConverter.ToUInt32(bytes, 0) == 0x32564952 && BitConverter.ToInt32(bytes, 4) == 2;
            bool v1 = bytes.Length == 2044 && BitConverter.ToUInt32(bytes, 0) == 0x31564952 && BitConverter.ToInt32(bytes, 4) == 1;
            if (!v1 && !v2 && !v3) return false;
            int recordSize = v3 ? 128 : v2 ? 120 : 112;
            int active = BitConverter.ToInt32(bytes, 8), count = BitConverter.ToInt32(bytes, 12), program = BitConverter.ToInt32(bytes, 16), kind = BitConverter.ToInt32(bytes, 20), programType = BitConverter.ToInt32(bytes, 24);
            if ((active != 0 && active != 1) || count < 0 || count > 18 || kind < 0 || kind > 5) return false;
            if (active == 0) { race.Rivals = new RaceEntry[0]; return count == 0; }
            if (!race.Results || kind != race.Kind || program != race.Program || programType != race.ProgramType || count == 0) return false;
            var entries = new List<RaceEntry>(); var gates = new HashSet<int>(); var strictUtf8 = new UTF8Encoding(false, true);
            for (int i = 0; i < count; i++) {
                int offset = 28 + i * recordSize;
                int id = BitConverter.ToInt32(bytes, offset), rank = BitConverter.ToInt32(bytes, offset + 4), gate = BitConverter.ToInt32(bytes, offset + 8), flags = BitConverter.ToInt32(bytes, offset + 12);
                int score = v2 || v3 ? BitConverter.ToInt32(bytes, offset + 16) : -1, skills = v2 || v3 ? BitConverter.ToInt32(bytes, offset + 20) : -1;
                int marginKind = v3 ? BitConverter.ToInt32(bytes, offset + 120) : 0;
                float gapLengths = v3 ? BitConverter.ToSingle(bytes, offset + 124) : 0;
                if (id < 1000 || id >= 2000 || rank < 1 || rank > 64 || gate < 1 || gate > 64 || (flags & ~((v3 ? 15 : v2 ? 7 : 1))) != 0 || !gates.Add(gate)) return false;
                if ((v2 || v3) && (((flags & 2) != 0 && (score < 1 || score > 999999)) || ((flags & 2) == 0 && score != 0) || ((flags & 4) != 0 && (skills < 0 || skills > 512)) || ((flags & 4) == 0 && skills != 0))) return false;
                if (v3 && (((flags & 8) != 0 && (marginKind < 1 || marginKind > 5 || float.IsNaN(gapLengths) || float.IsInfinity(gapLengths) || (marginKind == 5 ? gapLengths <= 0 || gapLengths > 10000 : gapLengths != 0))) || ((flags & 8) == 0 && (marginKind != 0 || gapLengths != 0)))) return false;
                if (race.Team.Any(own => own.Gate == gate)) return false;
                int trainerOffset = offset + (v1 ? 16 : 24);
                int length = 0; while (length < 96 && bytes[trainerOffset + length] != 0) length++;
                if (length == 0) return false;
                string trainer;
                try { trainer = strictUtf8.GetString(bytes, trainerOffset, length).Trim(); } catch (DecoderFallbackException) { return false; }
                if (string.IsNullOrWhiteSpace(trainer) || trainer.Any(char.IsControl)) return false;
                entries.Add(new RaceEntry { CharacterId = id, Position = rank, Gate = gate, TrainerName = trainer, MarkedRival = (flags & 1) != 0, LegacyScore = (flags & 2) != 0 ? score : -1, LegacySkills = (flags & 4) != 0 ? skills : -1, OfficialMarginKind = (flags & 8) != 0 ? marginKind : 0, OfficialGapLengths = (flags & 8) != 0 ? gapLengths : 0 });
            }
            if (entries.Select(e => e.Position).Distinct().Count() != entries.Count) return false;
            race.Rivals = entries.OrderByDescending(e => e.MarkedRival).ThenBy(e => e.Position).ThenBy(e => e.Gate).ToArray();
            return true;
        }
        internal static string Ordinal(int value)
        {
            int lastTwo = value % 100;
            return value + (lastTwo >= 11 && lastTwo <= 13 ? "th" : value % 10 == 1 ? "st" : value % 10 == 2 ? "nd" : value % 10 == 3 ? "rd" : "th");
        }
        internal string Details
        {
            get
            {
                string mode = Kind == 1 ? "CM" : Kind == 2 ? "Room" : Kind == 3 ? "LoH" : Kind == 4 ? "Career" : Kind == 5 ? "Master Challenge" : "Race";
                if (Results) { string name = RaceCatalog.Name(Program, ProgramType); return "Results — " + mode + (name == null ? "" : " • " + name) + (Kind == 4 && Rivals.Length > 0 ? " • Rivals" : ""); }
                if (Preparing) { string name = RaceCatalog.Name(Program, ProgramType); return "Preparing — " + mode + (name == null ? "" : " • " + name); }
                string liveMode = Kind == 5 ? "MC" : mode;
                if (Distance <= 0) return "Live — " + liveMode;
                int meters = Math.Min(Distance, Math.Max(0, (int)Math.Floor(Meters / 10) * 10));
                return "Live — " + liveMode + " • " + meters + "/" + Distance + " m • " + (int)(100.0 * meters / Distance) + "%";
            }
        }
        internal string Line
        {
            get
            {
                if (Team.Length == 0) return Results ? "Race finished" : "Race in progress";
                if (Preparing) { var preparingEntry = Team[Math.Abs(Page % Team.Length)]; return (preparingEntry.Gate > 0 ? "Gate " + preparingEntry.Gate + " • " : "") + preparingEntry.Name; }
                if (!Results) {
                    if (Team.Length == 1) return Team[0].Name + " — " + Ordinal(Team[0].Position);
                    if (Team.Length == 2) return string.Join(" • ", Team.Select(e => e.ShortName + " " + Ordinal(e.Position)));
                    return string.Join(" • ", Team.Select(e => Ordinal(e.Position)));
                }
                var entry = Team[Math.Abs(Page % Team.Length)];
                return entry.Name + " — " + Ordinal(entry.Position) + (entry.Position == 1 ? " 👑" : "");
            }
        }
        internal ActivityState ToState() { return new ActivityState { Kind = "Race", Details = Details, State = Line, Source = "Native race bridge", Race = this }; }
        internal string RivalTooltip
        {
            get {
                if (!Results || Kind != 4 || Rivals.Length == 0) return null;
                int index = Math.Abs(RivalPage % Rivals.Length); var rival = Rivals[index];
                var rows = new List<string> { FitUtf8(rival.TrainerName, 96) + " • " + rival.Name + " — " + Ordinal(rival.Position) };
                if (rival.LegacyScore > 0) {
                    string gauge = CareerRanks.Gauge(rival.LegacyScore);
                    string progress = CareerRanks.ProgressText(rival.LegacyScore);
                    if (!string.IsNullOrEmpty(gauge)) rows.Add(gauge);
                    rows.Add(rival.LegacyScore.ToString("N0", CultureInfo.InvariantCulture) + (progress == null ? "" : " • " + progress));
                }
                if (rival.LegacySkills >= 0) rows.Add(rival.LegacySkills + " acquired " + (rival.LegacySkills == 1 ? "skill" : "skills"));
                return RivalRows(rows);
            }
        }
        private static string RivalRows(List<string> rows)
        {
            string trainer = rows.Count > 0 ? rows[0].Split(new[] { " • " }, StringSplitOptions.None)[0] : "";
            string[] tail = rows.Skip(1).Select(row => row.Replace(' ', '\u00a0')).ToArray();
            string join(string name) { var all = new List<string>(); if (rows.Count > 0) all.Add(name + rows[0].Substring(trainer.Length).Replace(' ', '\u00a0')); all.AddRange(tail); return string.Join(" ", all); }
            string value = join(trainer);
            while (Encoding.UTF8.GetByteCount(value) > 128 && trainer.Length > 0) { trainer = RemoveLastTextElement(trainer); value = join(trainer); }
            if (Encoding.UTF8.GetByteCount(value) > 128 && rows.Count > 0) {
                string first = rows[0]; string rest = string.Join(" ", tail); string nameAndPlace = first.Substring(Math.Min(first.IndexOf(" • ", StringComparison.Ordinal) + 3, first.Length)).Replace(' ', '\u00a0');
                value = nameAndPlace + (rest.Length == 0 ? "" : " " + rest);
                while (Encoding.UTF8.GetByteCount(value) > 128 && nameAndPlace.Length > 1) { nameAndPlace = RemoveLastTextElement(nameAndPlace); value = nameAndPlace + (rest.Length == 0 ? "" : " " + rest); }
            }
            return ActivityState.Limit(value);
        }
        private static string RemoveLastTextElement(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            int[] starts = StringInfo.ParseCombiningCharacters(value);
            return starts.Length <= 1 ? "" : value.Substring(0, starts[starts.Length - 1]);
        }
        private RaceEntry CurrentRival
        {
            get { return Rivals.Length == 0 ? null : Rivals[Math.Abs(RivalPage % Rivals.Length)]; }
        }
        private static string FitUtf8(string value, int maxBytes)
        {
            if (string.IsNullOrEmpty(value)) return "";
            while (Encoding.UTF8.GetByteCount(value) > maxBytes && value.Length > 1) {
                int remove = char.IsLowSurrogate(value[value.Length - 1]) && value.Length > 1 ? 2 : 1;
                value = value.Substring(0, value.Length - remove);
            }
            return value.TrimEnd();
        }
        internal object ToActivity(Settings settings, long launched)
        {
            bool threeResults = Results && Team.Length == 3;
            string tooltip = Preparing ? "" : string.Join(threeResults ? "\n" : " • ", Team.Select(e => ((Results || Team.Length < 3 ? e.Name : e.ShortName) + " " + Ordinal(e.Position)).Replace(' ', '\u00a0')));
            var focused = Team.Length == 0 ? null : Team[Math.Abs(Page % Team.Length)];
            bool hasMetrics = !Preparing && focused != null && focused.Metrics != null;
            string rivalTooltip = RivalTooltip;
            if (!string.IsNullOrEmpty(rivalTooltip)) tooltip = rivalTooltip;
            else if (hasMetrics) tooltip = focused.Metrics.Tooltip(focused, Results, PodiumSweep);
            else if (string.IsNullOrEmpty(rivalTooltip) && PodiumSweep) tooltip = SweepHeading.Replace(' ', '\u00a0') + " " + tooltip;
            string course = RaceCatalog.CourseLabel(Course, Program, Distance, ProgramType);
            if (Preparing) tooltip = RaceTrackProfile.Tooltip(this);
            else if (string.IsNullOrEmpty(rivalTooltip) && !hasMetrics && Team.Length <= 2 && !string.IsNullOrEmpty(course))
            {
                int qualifier = course.IndexOf(" ("); if (qualifier >= 0) course = course.Substring(0, qualifier);
                course = course.Replace(" m", "m").Replace(" • ", " - ");
                tooltip += (tooltip.Length > 0 ? "\u2028" : "") + "Track: " + course;
            }
            string largeImage = settings.LargeImage;
            var currentRival = CurrentRival;
            if (!string.IsNullOrEmpty(rivalTooltip) && settings.ApplicationId == "954453106765225995" && settings.ShowCharacter && currentRival != null && CharacterCatalog.HasIcon(currentRival.CharacterId.ToString())) largeImage = "chara_" + currentRival.CharacterId;
            var assets = new Dictionary<string, object> { { "large_image", largeImage }, { "large_text", ActivityState.Limit(tooltip) } };
            if (!string.IsNullOrEmpty(rivalTooltip)) {
                assets["small_image"] = settings.LargeImage;
                string ownResult = hasMetrics ? focused.Metrics.Tooltip(focused, Results, PodiumSweep) : focused == null ? "Career results" : focused.Name + " — " + Ordinal(focused.Position);
                assets["small_text"] = ActivityState.Limit(ownResult);
            }
            return new Dictionary<string, object> {
                { "type", 0 }, { "name", "Uma Musume : Pretty Derby" }, { "details", ActivityState.Limit(Details) }, { "state", ActivityState.Limit(Line) },
                { "assets", assets },
                { "timestamps", new Dictionary<string, object> { { "start", launched } } }, { "instance", false }
            };
        }
    }
    internal sealed class RaceTracker
    {
        private string key, selected, published;
        private string[] order = new string[0];
        private RaceSnapshot observed;
        private int page;
        private DateTime next = DateTime.MaxValue;
        private string rivalKey, rivalSelected, rivalPublished;
        private string[] rivalOrder = new string[0];
        private int rivalPage;
        private DateTime rivalNext = DateTime.MaxValue;
        private static string EntryKey(RaceEntry entry) { return entry.Gate + ":" + entry.CharacterId; }
        private static string RivalEntryKey(RaceEntry entry) { return EntryKey(entry) + ":" + entry.TrainerName + ":" + entry.MarkedRival; }
        private static string Context(RaceSnapshot race) { return race.ProgramType + "|" + race.Program + "|" + race.Course + "|" + race.Distance + "|" + race.Results + "|" + race.Preparing + "|" + string.Join(",", race.Team.Select(EntryKey).OrderBy(value => value, StringComparer.Ordinal)); }
        public ActivityState Observe(RaceSnapshot snapshot, DateTime now)
        {
            if (snapshot == null) { Reset(); return null; }
            string context = Context(snapshot);
            if (context != key) { Reset(); key = context; order = snapshot.Team.Select(EntryKey).ToArray(); }
            snapshot.Team = snapshot.Team.OrderBy(entry => Array.IndexOf(order, EntryKey(entry))).ToArray();
            string currentRivalKey = string.Join(",", snapshot.Rivals.Select(RivalEntryKey).OrderBy(value => value, StringComparer.Ordinal));
            if (currentRivalKey != rivalKey) { rivalKey = currentRivalKey; rivalOrder = snapshot.Rivals.Select(RivalEntryKey).ToArray(); rivalPage = 0; rivalNext = DateTime.MaxValue; rivalSelected = rivalPublished = null; }
            snapshot.Rivals = snapshot.Rivals.OrderBy(entry => Array.IndexOf(rivalOrder, RivalEntryKey(entry))).ToArray();
            if ((snapshot.Results || snapshot.Preparing || snapshot.Team.Any(e => e.Metrics != null)) && now >= next && snapshot.Team.Length > 1) { page = (page + 1) % snapshot.Team.Length; next = DateTime.MaxValue; }
            if (snapshot.Team.Length > 0) selected = EntryKey(snapshot.Team[page % snapshot.Team.Length]);
            if (snapshot.Results && snapshot.Rivals.Length > 1 && now >= rivalNext) { rivalPage = (rivalPage + 1) % snapshot.Rivals.Length; rivalNext = DateTime.MaxValue; }
            if (snapshot.Rivals.Length > 0) rivalSelected = RivalEntryKey(snapshot.Rivals[rivalPage % snapshot.Rivals.Length]);
            snapshot.Page = page;
            snapshot.RivalPage = rivalPage;
            observed = snapshot;
            return snapshot.ToState();
        }
        public void Published(DateTime now) { Published(observed, now); }
        public void Published(RaceSnapshot snapshot, DateTime now)
        {
            if (snapshot == null || Context(snapshot) != key) return;
            if (snapshot.Team.Length > 0) {
                string acknowledged = EntryKey(snapshot.Team[Math.Abs(snapshot.Page % snapshot.Team.Length)]);
                if (acknowledged == selected && acknowledged != published) { published = acknowledged; next = now.AddSeconds(3); }
            }
            if (snapshot.Rivals.Length > 0) {
                string acknowledgedRival = RivalEntryKey(snapshot.Rivals[Math.Abs(snapshot.RivalPage % snapshot.Rivals.Length)]);
                if (acknowledgedRival == rivalSelected && acknowledgedRival != rivalPublished) { rivalPublished = acknowledgedRival; rivalNext = now.AddSeconds(3); }
            }
        }
        public void Reset() { key = selected = published = rivalKey = rivalSelected = rivalPublished = null; observed = null; order = rivalOrder = new string[0]; page = rivalPage = 0; next = rivalNext = DateTime.MaxValue; }
    }
    internal sealed class RaceCatalogEntry
    {
        public string Name { get; set; }
        public string Course { get; set; }
    }
    internal static class RaceCatalog
    {
        private static Dictionary<string, RaceCatalogEntry> races;
        private static Dictionary<string, string> courses;
        private static void Load() { if (races != null) return; races = SongCatalog.Resource<Dictionary<string, RaceCatalogEntry>>("race-catalog.json"); courses = SongCatalog.Resource<Dictionary<string, string>>("race-courses.json"); }
        private static RaceCatalogEntry Find(int id, int type)
        {
            Load(); RaceCatalogEntry entry;
            if (type >= 1 && type <= 3) return races.TryGetValue((type == 1 ? "p:" : type == 2 ? "i:" : "r:") + id, out entry) ? entry : null;
            var matches = new[] { "i:", "r:" }.Where(prefix => races.ContainsKey(prefix + id)).Select(prefix => races[prefix + id]).ToArray();
            return matches.Length > 0 && matches.All(e => e.Name == matches[0].Name && e.Course == matches[0].Course) ? matches[0] : null;
        }
        internal static string Name(int program, int type = 0) { var entry = Find(program, type); return entry == null ? null : entry.Name; }
        internal static string CourseLabel(int course, int program, int distance, int type = 0)
        {
            Load(); string label; RaceCatalogEntry entry;
            if (courses.TryGetValue(course.ToString(), out label)) return label;
            entry = Find(program, type); return entry != null ? entry.Course : distance > 0 ? distance + " m" : "";
        }
    }
}
