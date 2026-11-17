using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using System.Globalization;

namespace UmapyonPresence
{
    internal sealed class CareerSnapshot
    {
        public int Card, Turn, Scenario, Speed, Stamina, Power, Guts, Wisdom, Skill, Race;
        public bool Results, Legacy, BareLegacy;
        public bool PracticePartner;
        public int EvaluationScore = -1, SkillCount = -1, RecordId;
        public LegacyDetails LegacyDetails;
        public string RivalTrainer;
        public int RivalPlacement, RivalMarginKind;
        public float RivalGapLengths, RivalGapSeconds;
        public CareerExtras Extras;
        internal static CareerSnapshot ApplyResult(byte[] bytes, CareerSnapshot training)
        {
            if (bytes == null || bytes.Length < 32 || BitConverter.ToUInt32(bytes, 0) != 0x43525331 || BitConverter.ToInt32(bytes, 4) != 1) return training;
            int card = BitConverter.ToInt32(bytes, 8), scenario = BitConverter.ToInt32(bytes, 12), score = BitConverter.ToInt32(bytes, 16), count = BitConverter.ToInt32(bytes, 20), flags = BitConverter.ToInt32(bytes, 24);
            bool bare = (flags & 12) == 12 && score == 0 && (flags & 1) == 0 && (flags & 2) != 0 && count >= 0;
            if (card < 100000 || card >= 200000 || scenario < 0 || scenario > 1000 || (!bare && (score < 1 || score > 999999 || (flags & 1) == 0)) || count < -1 || count > 512) return training;
            var result = training != null && training.Card == card ? (CareerSnapshot)training.MemberwiseClone() : new CareerSnapshot { Card = card, Scenario = scenario };
            result.Results = true; result.Legacy = (flags & 4) != 0; result.BareLegacy = bare; result.Extras = null; result.Scenario = scenario; result.EvaluationScore = score; result.SkillCount = (flags & 2) != 0 && count >= 0 ? count : -1;
            result.RivalTrainer = null; result.RivalPlacement = result.RivalMarginKind = 0; result.RivalGapLengths = result.RivalGapSeconds = 0;
            result.PracticePartner = (flags & 32) != 0 && result.Legacy;
            result.LegacyDetails = null;
            result.RecordId = result.Legacy ? BitConverter.ToInt32(bytes, 28) : 0;
            return result;
        }
        internal bool ApplyRivalProfile(byte[] bytes)
        {
            bool v2 = bytes != null && (bytes.Length == 120 || bytes.Length == 128) && BitConverter.ToUInt32(bytes, 0) == 0x32444c52;
            bool v1 = bytes != null && bytes.Length == 112 && BitConverter.ToUInt32(bytes, 0) == 0x31444c52;
            if ((!v1 && !v2) || BitConverter.ToInt32(bytes, 4) != 1 || BitConverter.ToInt32(bytes, 8) != Card || !Legacy) return false;
            int placement = BitConverter.ToInt32(bytes, 12);
            int end = Array.IndexOf(bytes, (byte)0, 16, 96);
            if (end <= 16 || (PracticePartner ? !v2 || placement != 0 : BareLegacy || placement < 1 || placement > 64)) return false;
            string trainer;
            try { trainer = new UTF8Encoding(false, true).GetString(bytes, 16, end - 16).Trim(); }
            catch (DecoderFallbackException) { return false; }
            if (trainer.Length == 0 || trainer.Any(char.IsControl)) return false;
            int marginKind = v2 ? BitConverter.ToInt32(bytes, 112) : 0;
            float gapLengths = v2 ? BitConverter.ToSingle(bytes, 116) : 0;
            if (v2 && (marginKind < 0 || marginKind > 5 || float.IsNaN(gapLengths) || float.IsInfinity(gapLengths) || (marginKind == 5 ? gapLengths <= 0 || gapLengths > 10000 : gapLengths != 0))) return false;
            if (PracticePartner && marginKind != 0) return false;
            RivalTrainer = trainer;
            RivalPlacement = placement;
            RivalMarginKind = marginKind;
            RivalGapLengths = gapLengths;
            float gapSeconds = !PracticePartner && bytes.Length == 128 && BitConverter.ToUInt32(bytes, 120) == 0x31544c52 ? BitConverter.ToSingle(bytes, 124) : 0;
            RivalGapSeconds = gapSeconds > 0 && gapSeconds < 3600 ? gapSeconds : 0;
            return true;
        }
        internal static CareerSnapshot Parse(byte[] bytes)
        {
            return ParseFields(bytes, false);
        }
        internal static CareerSnapshot ParseAvailable(byte[] bytes, string diagnostics)
        {
            var complete = Parse(bytes);
            if (complete != null) return complete;
            return diagnostics != null && diagnostics.StartsWith("Scene: SingleMode ", StringComparison.Ordinal) ? ParseFields(bytes, true) : null;
        }
        private static CareerSnapshot ParseFields(byte[] bytes, bool partial)
        {
            if (bytes == null || bytes.Length < 48 || BitConverter.ToUInt32(bytes, 0) != 0x43415231 || (!partial && BitConverter.ToInt32(bytes, 4) != 1)) return null;
            var n = new int[10];
            for (int i = 0; i < n.Length; i++) n[i] = BitConverter.ToInt32(bytes, 8 + i * 4);
            if (n[0] < 100000 || n[0] >= 200000 || n[1] < (partial ? 0 : 1) || n[1] > 200 || n.Skip(3).Take(5).Any(v => v < 1 || v > 9999) || n[8] < 0 || n[8] > 99999) return null;
            return new CareerSnapshot { Card = n[0], Turn = n[1], Scenario = n[2], Speed = n[3], Stamina = n[4], Power = n[5], Guts = n[6], Wisdom = n[7], Skill = partial && n[8] == 0 ? -1 : n[8], Race = n[9] };
        }
        internal static string TurnText(int turn)
        {
            int index = turn - 1;
            if (index < 12) index = index / 2 + 6;
            string[] months = { "January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December" };
            return "Y" + (index / 24 + 1) + ", " + (index % 2 == 1 ? "Late " : "Early ") + months[(index / 2) % 12];
        }
        private string ScenarioName
        {
            get
            {
                string[] names = { "Career mode", "URA Finals", "Aoharu Cup", "Grand Live", "Make a New Track", "Grand Masters", "Project L'Arc", "U.A.F. Ready GO!", "Great Food Festival", "Run! Mecha Umamusume", "The Twinkle Legends" };
                return Scenario >= 0 && Scenario < names.Length ? names[Scenario] : "Career scenario " + Scenario;
            }
        }
        internal string Stats { get { return Speed + "/" + Stamina + "/" + Power + "/" + Guts + "/" + Wisdom + (Skill >= 0 ? " | " + Skill : ""); } }
        internal string Title { get { return Turn > 0 ? "Training - " + TurnText(Turn) : "Training"; } }
        private string CharacterName { get { var character = CharacterCatalog.Entries.FirstOrDefault(c => c.Id == (Card / 100).ToString()); return character == null ? "Umamusume" : character.Name; } }
        private string ScenarioTitle(Settings settings)
        {
            if (Scenario == 14) return "Tracen Ramen";
            string value = CareerLabels.Scenario(Scenario, settings) ?? ScenarioName;
            return CareerExtras.Clean(value);
        }
        private string LegacyTitle { get { return PracticePartner ? "Practice Partner" : RivalTrainer != null ? "Rivals" : BareLegacy ? "Bare Legacy" : "Legacy"; } }
        private const string HoverRowBreak = "\u2028";

        private static string TrainerRow(string trainer, int budget)
        {
            trainer = CareerExtras.Clean(trainer);
            string row = JapaneseTooltips.Protect(trainer);
            if (Encoding.UTF8.GetByteCount(row) <= budget) return row;
            int[] elements = StringInfo.ParseCombiningCharacters(trainer);
            for (int i = elements.Length - 1; i > 0; i--) {
                row = JapaneseTooltips.Protect(trainer.Substring(0, elements[i]).TrimEnd() + "…");
                if (Encoding.UTF8.GetByteCount(row) <= budget) return row;
            }
            return budget >= 3 ? "…" : "";
        }

        private string ProfileTrainerRow(int budget)
        {
            string name = CareerExtras.Clean(RivalTrainer);
            if (name.Length == 0) return "";
            string labeled = JapaneseTooltips.Protect("Trainer : " + name);
            bool shortLabel = Encoding.UTF8.GetByteCount(labeled.Replace("\u2060", "")) <= 32;
            return shortLabel && Encoding.UTF8.GetByteCount(labeled) <= budget ? labeled : TrainerRow(name, budget);
        }

        private string PracticePartnerTooltip(Settings settings)
        {
            string trainer = ProfileTrainerRow(44);
            int budget = 128 - (trainer.Length == 0 ? 0 : Encoding.UTF8.GetByteCount(trainer) + 3);
            string portrait = CareerLabels.PortraitTooltip(CharacterName, Card, settings, budget, HoverRowBreak);
            return trainer.Length == 0 ? portrait : trainer + HoverRowBreak + portrait;
        }
        private string RivalTooltip()
        {
            string placement = RaceSnapshot.Ordinal(RivalPlacement) + (RivalPlacement == 1 ? " 👑" : "");
            string margin = null;
            if (RivalMarginKind == 5 && RivalGapLengths > 0) {
                string length = RivalGapLengths.ToString("0.##", CultureInfo.InvariantCulture) + " L";
                margin = RivalPlacement == 1 ? "Won by " + length : RivalPlacement == 2 ? "Lost by " + length : length + " behind 1st";
            } else if (RivalMarginKind >= 1 && RivalMarginKind <= 3 && RivalPlacement <= 2) {
                string label = RivalMarginKind == 1 ? "a nose" : RivalMarginKind == 2 ? "a head" : "a neck";
                margin = RivalPlacement == 1 ? "Won by " + label : "Lost by " + label;
            } else if (RivalMarginKind == 4 && RivalPlacement <= 2) {
                margin = RivalPlacement == 1 ? "The Rest Nowhere" : "Large margin behind 1st";
            }
            if (RivalGapSeconds > 0 && RivalMarginKind < 4) {
                string seconds = RivalGapSeconds < 0.005f ? "<0.01s" : RivalGapSeconds.ToString("0.00", CultureInfo.InvariantCulture) + "s";
                if (margin != null) margin += " (" + seconds + ")";
                else margin = RivalPlacement == 1 ? "Won by " + seconds : "+" + seconds + " to 1st";
            }
            string result = placement.Replace(' ', '\u202f');
            if (!string.IsNullOrEmpty(margin)) result += "\u00a0•\u00a0" + margin.Replace(' ', '\u202f');
            string trainer = ProfileTrainerRow(Math.Min(44, 128 - Encoding.UTF8.GetByteCount(result) - 3));
            return trainer.Length == 0 ? result : trainer + HoverRowBreak + result;
        }
        internal string ResultLine
        {
            get {
                if (BareLegacy) {
                    if (LegacyDetails == null || !LegacyDetails.Matches(this)) return "Bare Legacy";
                    int white = UmapyonPresence.LegacyDetails.WhiteCount(LegacyDetails.Factors);
                    return LegacyDetails.WinsSummary() + (white >= 0 ? " • " + white + " white factors" : "");
                }
                return (CareerRanks.Label(EvaluationScore) == null ? "" : CareerRanks.Label(EvaluationScore) + " • ") + EvaluationScore.ToString("N0", CultureInfo.InvariantCulture) + (SkillCount >= 0 ? " • " + SkillCount + (SkillCount == 1 ? " skill" : " skills") : "");
            }
        }
        internal ActivityState ToState()
        {
            return new ActivityState { Kind = Legacy ? "Legacy" : "Training", Details = Results ? CharacterName + " — " + (Legacy ? LegacyTitle : ScenarioTitle(new Settings())) : Title, State = Results ? ResultLine : Stats, Source = "Native career bridge", StrongEvidence = true, Career = this };
        }
        internal object ToActivity(Settings settings, long start)
        {
            int characterId = Card / 100;
            var character = CharacterCatalog.Entries.FirstOrDefault(c => c.Id == characterId.ToString());
            string portrait = settings.ShowCharacter ? OutfitCatalog.CareerPortrait(Card, settings) : null;
            var assets = new Dictionary<string, object> {
                { "large_image", portrait ?? settings.LargeImage },
                { "large_text", settings.ShowCharacter && character != null ? CareerLabels.PortraitTooltip(character.Name, Card, settings) : "Umamusume • DMM" },
                { "small_image", settings.LargeImage }, { "small_text", CareerExtras.Rows(new[] { CareerLabels.Scenario(Scenario, settings) ?? ScenarioName }) }
            };
            if (Results && !BareLegacy && settings.ShowCharacter) {
                var rows = new List<string> { CareerRanks.Gauge(EvaluationScore), EvaluationScore.ToString("N0", CultureInfo.InvariantCulture) + (CareerRanks.ProgressText(EvaluationScore) == null ? "" : " • " + CareerRanks.ProgressText(EvaluationScore)), SkillCount >= 0 ? SkillCount + " acquired " + (SkillCount == 1 ? "skill" : "skills") : "" };
                assets["large_text"] = RivalTrainer == null ? ActivityState.Limit(CareerExtras.Rows(rows)) : RivalTooltip();
            }
            if (PracticePartner && settings.ShowCharacter) assets["large_text"] = PracticePartnerTooltip(settings);
            if (!Results && Extras != null) assets["small_text"] = Extras.Tooltip(ScenarioTitle(settings));
            if (BareLegacy) assets["small_text"] = "Bare\u00a0Legacy";
            if (Legacy && LegacyDetails != null && LegacyDetails.Matches(this)) assets["small_text"] = BareLegacy ? LegacyDetails.BareTooltip() : LegacyDetails.Tooltip(ScenarioTitle(settings));
            return new Dictionary<string, object> {
                { "type", 0 }, { "name", "Uma Musume : Pretty Derby" },
                { "details", ActivityState.Limit(Results ? (settings.ShowCharacter ? CharacterName : "Career result") + " — " + (Legacy ? LegacyTitle : ScenarioTitle(settings)) : Title) }, { "state", Results ? ResultLine : Stats },
                { "assets", assets }, { "timestamps", new Dictionary<string, object> { { "start", start } } }, { "instance", false }
            };
        }
    }
    internal sealed class CareerRankEntry { public int Id { get; set; } public int Min { get; set; } public int Max { get; set; } public string Name { get; set; } }
    internal static class CareerRanks
    {
        private static CareerRankEntry[] entries;
        internal static string Reload(Settings settings)
        {
            IntPtr database = IntPtr.Zero;
            try {
                string path = DataMaintenance.MasterPath(settings);
                if (OutfitCatalog.Sqlite.Open(OutfitCatalog.Utf8(path), out database, 1, IntPtr.Zero) != 0) throw new IOException("Cannot open game master read-only");
                var bundled = SongCatalog.Resource<CareerRankEntry[]>("career-ranks.json").ToDictionary(row => row.Id);
                var current = new List<CareerRankEntry>();
                OutfitCatalog.Query(database, "select id,min_value,max_value from single_mode_rank order by min_value", row => {
                    int id = OutfitCatalog.Sqlite.ColumnInt(row, 0), min = OutfitCatalog.Sqlite.ColumnInt(row, 1), max = OutfitCatalog.Sqlite.ColumnInt(row, 2);
                    if (id <= 0 || min < 0 || max < min) throw new IOException("Invalid game rank range");
                    CareerRankEntry known; bundled.TryGetValue(id, out known);
                    current.Add(new CareerRankEntry { Id = id, Min = min, Max = max, Name = known == null ? null : known.Name });
                });
                if (current.Count == 0 || current.Zip(current.Skip(1), (a, b) => a.Max >= b.Min).Any(overlap => overlap)) throw new IOException("Invalid game rank table");
                entries = current.ToArray();
                return current.Count + " rank thresholds loaded; " + current.Count(row => !Named(row)) + " unnamed ranges withheld";
            } finally { if (database != IntPtr.Zero) OutfitCatalog.Sqlite.Close(database); }
        }
        private static CareerRankEntry[] Entries { get { return entries ?? (entries = SongCatalog.Resource<CareerRankEntry[]>("career-ranks.json")); } }
        private static CareerRankEntry Find(int score) { return Entries.FirstOrDefault(e => score >= e.Min && score <= e.Max); }
        internal static string Label(int score) { var rank = Find(score); return Named(rank) ? rank.Name : null; }
        private static bool Named(CareerRankEntry rank) { return rank != null && !string.IsNullOrEmpty(rank.Name) && !rank.Name.StartsWith("Rank ", StringComparison.Ordinal); }
        private static CareerRankEntry Next(CareerRankEntry rank) { return rank == null ? null : Entries.FirstOrDefault(e => e.Min == rank.Max + 1 && Named(e)); }
        private static double Progress(int score, CareerRankEntry rank) { return rank == null ? 0 : Math.Max(0, Math.Min(1, (score - rank.Min) / (double)(rank.Max + 1 - rank.Min))); }
        internal static string Gauge(int score)
        {
            var rank = Find(score); var next = Next(rank); if (next == null) return Label(score);
            int point = (int)Math.Round(Progress(score, rank) * 4);
            return rank.Name + " " + new string('━', point) + "◆" + new string('─', 4 - point) + " " + next.Name;
        }
        internal static string ProgressText(int score) { var rank = Find(score); var next = Next(rank); return next == null ? null : (int)(Progress(score, rank) * 100) + "% toward " + next.Name; }
    }
    internal static class CareerLabels
    {
        private static readonly Dictionary<string, Dictionary<string, string>> labels =
            SongCatalog.Resource<Dictionary<string, Dictionary<string, string>>>("career-labels.json");
        private static string Find(string category, int id, Settings settings)
        {
            string translated = DataMaintenance.FindTranslation(category, id, settings);
            if (translated != null) return translated;
            Dictionary<string, string> values; string value;
            return labels.TryGetValue(category, out values) && values.TryGetValue(id.ToString(), out value) && !string.IsNullOrWhiteSpace(value) && value != "None" ? value : GameText.Find(category, id, settings);
        }
        internal static string Outfit(int card, Settings settings) { string value = Find("5", card, settings); return string.IsNullOrEmpty(value) ? "" : "\n" + value; }
        internal static string PortraitTooltip(string name, int card, Settings settings, int maxBytes = 128, string separator = " ")
        {
            string first = CareerExtras.Clean(name).Replace(' ', '\u00a0');
            string outfit = CareerExtras.Clean(Outfit(card, settings));
            if (outfit.Length == 0) return separator == "\u2028" ? JapaneseTooltips.Protect(first) : ActivityState.Limit(first);
            outfit = ConcertDetails.UnwrapCostume(outfit);
            outfit = System.Text.RegularExpressions.Regex.Replace(outfit, @"\s*\[([A-Z0-9]{1,3})\]$", " ($1)").Replace(' ', '\u00a0');
            if (separator == "\u2028") {
                first = JapaneseTooltips.Protect(first);
                // Include bracket boundaries in protection and byte accounting.
                outfit = JapaneseTooltips.Protect("[" + outfit + "]");
                int available = maxBytes - Encoding.UTF8.GetByteCount(first + separator);
                if (Encoding.UTF8.GetByteCount(outfit) <= available) return first + separator + outfit;
                string shortened = JapaneseTooltips.Bounded(outfit.Replace("\u2060", ""), available);
                return shortened.Length == 0 ? first : first + separator + shortened;
            }
            int budget = maxBytes - Encoding.UTF8.GetByteCount(first + separator) - 2;
            if (budget < 3) return ActivityState.Limit(first);
            if (Encoding.UTF8.GetByteCount(outfit) > budget) {
                while (outfit.Length > 0 && Encoding.UTF8.GetByteCount(outfit) > budget - 3) {
                    int[] positions = StringInfo.ParseCombiningCharacters(outfit);
                    outfit = outfit.Substring(0, positions[positions.Length - 1]);
                }
                outfit += "…";
            }
            return first + separator + "[" + outfit + "]";
        }
        internal static string Scenario(int scenario, Settings settings) { return Find("119", scenario, settings); }
    }
}
