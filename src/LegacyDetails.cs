using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace UmapyonPresence
{
    internal sealed class LegacyFactor
    {
        public int Id { get; set; }
        public int Type { get; set; }
        public int Stars { get; set; }
        public string Name { get; set; }
    }
    internal sealed class LegacyDetails
    {
        internal const string Dialog = "DialogManager/_dialogList/[0]/_data/CenterButtonCallBack/m_target";
        internal const string Record = Dialog + "/_onCloseDialogWithCharaSwitchButton/m_target/getIndexedSetupParam/m_target/buttonInfo/TrainedChara";
        internal int Card, Score, Id, Speed, Stamina, Power, Guts, Wisdom, Wins, Races, FactorCount;
        internal int TotalSkillPoints;
        internal int[] Factors;
        internal int[] GrandparentCards = new int[2];
        internal int[] GrandparentFactorCounts = { -1, -1 };
        internal int[][] GrandparentFactors = new int[2][];
        private static Dictionary<int, LegacyFactor> factors;
        private static DateTime nextFactorCheck;
        private static string factorStamp;
        internal static void Reload(Settings settings) { nextFactorCheck = DateTime.MinValue; factorStamp = null; Refresh(settings); if (factorStamp == null) throw new IOException("Factor reload failed; previous data retained"); }
        internal static void Refresh(Settings settings)
        {
            if (factors == null) factors = SongCatalog.Resource<LegacyFactor[]>("legacy-factors.json").ToDictionary(f => f.Id);
            if (settings == null || string.IsNullOrEmpty(settings.GameDirectory) || DateTime.UtcNow < nextFactorCheck) return;
            nextFactorCheck = DateTime.UtcNow.AddMinutes(1);
            string path = Path.Combine(settings.GameDirectory, "umamusume_Data", "Persistent", "master", "master.mdb");
            if (!File.Exists(path)) return;
            IntPtr database = IntPtr.Zero;
            try {
                var info = new FileInfo(path); string stamp = path + "|" + info.Length + "|" + info.LastWriteTimeUtc.Ticks;
                if (stamp == factorStamp || OutfitCatalog.Sqlite.Open(OutfitCatalog.Utf8(path), out database, 1, IntPtr.Zero) != 0) return;
                var current = new Dictionary<int, LegacyFactor>(factors);
                OutfitCatalog.Query(database, "select factor_id,factor_type,rarity from succession_factor", row => {
                    int id = OutfitCatalog.Sqlite.ColumnInt(row, 0), type = OutfitCatalog.Sqlite.ColumnInt(row, 1), stars = OutfitCatalog.Sqlite.ColumnInt(row, 2);
                    if (id <= 0 || type < 1 || type > 11 || stars < 1 || stars > 3) return;
                    LegacyFactor old; current.TryGetValue(id, out old);
                    current[id] = new LegacyFactor { Id = id, Type = type, Stars = stars, Name = old == null ? "Factor " + id : old.Name };
                });
                factors = current; factorStamp = stamp;
            } catch (Exception ex) { Log.Write("Legacy factor refresh skipped: " + ex.Message); }
            finally { if (database != IntPtr.Zero) OutfitCatalog.Sqlite.Close(database); }
        }
        internal string FactorSummary
        {
            get
            {
                if (factors == null) factors = SongCatalog.Resource<LegacyFactor[]>("legacy-factors.json").ToDictionary(f => f.Id);
                return string.Join(" ", (Factors ?? new int[0]).Where(factors.ContainsKey).Select(id => factors[id]).OrderBy(f => f.Type).Take(3).Select(f => f.Name + f.Stars + "★"));
            }
        }
        internal bool Matches(CareerSnapshot career) { return career != null && career.Legacy && career.Card == Card && career.EvaluationScore == Score && (career.RecordId == 0 || career.RecordId == Id); }
        internal static int WhiteCount(int[] ids)
        {
            if (ids == null) return -1;
            if (factors == null) factors = SongCatalog.Resource<LegacyFactor[]>("legacy-factors.json").ToDictionary(f => f.Id);
            if (ids.Any(id => !factors.ContainsKey(id))) return -1;
            return ids.Count(id => factors[id].Type >= 4);
        }
        private static int Value(string text, string query)
        {
            var match = Regex.Match(text, @"(?:^|\n)QUERY " + Regex.Escape(query) + @"\r?\nVALUE (-?\d+)(?:\r?\n|$)");
            int value; return match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : -1;
        }
        internal static LegacyDetails ParseInspection(string text, bool partial = false)
        {
            var value = new LegacyDetails { Id = Value(text, Record + "/@get_Id"), Card = Value(text, Record + "/@get_CardId"), Score = Value(text, Record + "/@get_RankScore"),
                Speed = Value(text, Record + "/@get_Speed"), Stamina = Value(text, Record + "/@get_Stamina"), Power = Value(text, Record + "/@get_Power"), Guts = Value(text, Record + "/@get_Guts"), Wisdom = Value(text, Record + "/@get_Wiz"),
                Wins = Value(text, Record + "/@get_SingleWinNum"), Races = Value(text, Record + "/@get_SingleTotalRaceNum") };
            int selected = Value(text, Dialog + "/_headerTitle/_innerOwn/_trainedCharaId"), count = Value(text, Record + "/FactorDataArray/@get_Length");
            if (selected != value.Id || !value.Valid() || count < 0 || count > 64) return null;
            value.FactorCount = count;
            value.Factors = Enumerable.Range(0, count).Select(i => Value(text, Record + "/FactorDataArray/[" + i + "]/@get_FactorId")).ToArray();
            if (!partial && value.Factors.Any(f => f <= 0)) return null;
            value.Factors = value.Factors.Where(f => f > 0).ToArray();
            for (int group = 0; group < 2; group++) {
                string parent = Record + "/@get_SuccessionChara" + (group + 1);
                int parentCard = Value(text, parent + "/@get_CardId"), parentCount = Value(text, parent + "/FactorDataArray/@get_Length");
                if (parentCard < 100000 || parentCard >= 200000 || parentCount < 0 || parentCount > 64) continue;
                value.GrandparentCards[group] = parentCard; value.GrandparentFactorCounts[group] = parentCount;
                var ids = Enumerable.Range(0, parentCount).Select(i => Value(text, parent + "/FactorDataArray/[" + i + "]/@get_FactorId")).ToArray();
                if (ids.All(id => id > 0)) value.GrandparentFactors[group] = ids;
            }
            return value;
        }
        private bool Valid() { var stats = new[] { Speed, Stamina, Power, Guts, Wisdom }; return Id > 0 && Card >= 100000 && Card < 200000 && ((Score > 0 && Score <= 999999 && stats.All(n => n > 0 && n <= 9999)) || (Score == 0 && stats.All(n => n == 0))) && Wins >= 0 && Races >= Wins && Races <= 10000; }
        internal static LegacyDetails ParsePacket(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 308 || BitConverter.ToUInt32(bytes, 0) != 0x4c455831 || BitConverter.ToInt32(bytes, 4) != 1) return null;
            var n = Enumerable.Range(0, 11).Select(i => BitConverter.ToInt32(bytes, 8 + i * 4)).ToArray();
            var value = new LegacyDetails { Card = n[0], Score = n[1], Id = n[2], Speed = n[3], Stamina = n[4], Power = n[5], Guts = n[6], Wisdom = n[7], Wins = n[8], Races = n[9] };
            if (!value.Valid() || n[10] < 0 || n[10] > 64) return null;
            if (bytes.Length >= 316 && BitConverter.ToUInt32(bytes, 308) == 0x3154504c)
                value.TotalSkillPoints = Math.Max(0, BitConverter.ToInt32(bytes, 312));
            value.Factors = Enumerable.Range(0, n[10]).Select(i => BitConverter.ToInt32(bytes, 52 + i * 4)).ToArray();
            return value.Factors.Any(f => f <= 0) ? null : value;
        }
        internal void ApplyGrandparents(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 552 || BitConverter.ToUInt32(bytes, 0) != 0x4c475031 || BitConverter.ToInt32(bytes, 4) != 1 || BitConverter.ToInt32(bytes, 8) != Card || BitConverter.ToInt32(bytes, 12) != Score || BitConverter.ToInt32(bytes, 16) != Id) return;
            for (int group = 0; group < 2; group++) {
                int offset = 24 + group * 264, card = BitConverter.ToInt32(bytes, offset), count = BitConverter.ToInt32(bytes, offset + 4);
                if (card < 100000 || card >= 200000 || count < 0 || count > 64) continue;
                var ids = Enumerable.Range(0, count).Select(i => BitConverter.ToInt32(bytes, offset + 8 + i * 4)).ToArray();
                if (ids.Any(id => id <= 0)) continue;
                GrandparentCards[group] = card; GrandparentFactorCounts[group] = count; GrandparentFactors[group] = ids;
            }
        }
        internal string WinsSummary()
        {
            string record = Wins + "/" + Races;
            return TotalSkillPoints > 0
                ? record + " W • " + TotalSkillPoints.ToString("N0", CultureInfo.InvariantCulture) + " pts"
                : record + " wins";
        }
        internal string Tooltip(string scenario)
        {
            string stats = Speed + "/" + Stamina + "/" + Power + "/" + Guts + "/" + Wisdom;
            string summary = WinsSummary();
            // Balance nonbreaking padding around the visible text. Trailing-only
            // padding makes Discord center the blank area and shifts wins left.
            string padding = "\u2007";
            summary = padding + summary + padding;
            int own = WhiteCount(Factors);
            var factors = new List<string>();
            if (own >= 0) factors.Add("White factors: " + own);
            for (int group = 0; group < 2; group++) { int count = WhiteCount(GrandparentFactors[group]); if (count >= 0) factors.Add("GP" + (group + 1) + ": " + count); }
            string family = own < 0 ? "" : "White factors: " + own;
            string grandparents = string.Join(" • ", factors.Skip(own >= 0 ? 1 : 0));
            string title = CareerExtras.Clean(JapaneseTooltips.ScenarioTitle(scenario));
            Func<string, string> protect = row => row.Replace(' ', '\u00a0');
            // Keep BOTH GP entries together. Allowing ordinary breaks here lets
            // GP1 join the white-factor row while GP2 wraps down on its own.
            Func<string> pack = () => string.Join(" ", new[] { protect(title), protect(stats), protect(summary), protect(family), protect(grandparents) }.Where(row => !string.IsNullOrEmpty(row)));
            // Preserve the established three groups. Tighten count-label spacing
            // before shortening a long title; never delete the scenario/stat rows.
            if (Encoding.UTF8.GetByteCount(pack()) > 128) family = family.Replace(": ", ":");
            if (Encoding.UTF8.GetByteCount(pack()) > 128) {
                string original = title;
                while (Encoding.UTF8.GetByteCount(pack()) > 128 && original.Length > 1) {
                    var elements = StringInfo.ParseCombiningCharacters(original);
                    original = original.Substring(0, elements[elements.Length - 1]); title = original.TrimEnd() + "…";
                }
            }
            return ActivityState.Limit(pack());
        }
        internal string BareTooltip()
        {
            // Original compact bare layout, independent of normal Legacy's
            // stat/scenario rows and padding. Never compress GP count spaces.
            string first = "Bare Legacy " + WinsSummary();
            int own = WhiteCount(Factors);
            string white = own < 0 ? "" : "White factors: " + own;
            var parents = new List<string>();
            for (int i = 0; i < 2; i++) { int count = WhiteCount(GrandparentFactors[i]); if (count >= 0) parents.Add("GP" + (i + 1) + ": " + count); }
            return string.Join(" ", new[] { first, white, string.Join(" • ", parents) }.Where(row => row.Length > 0).Select(row => row.Replace(' ', '\u00a0')));
        }
    }
    // Opt-in adapter for the diagnostic DLL already loaded in this game session.
    // Production reads the same verified fields from the LEX1 native extension.
    internal sealed class LegacyDetailsInspectionReader
    {
        private string token;
        private int pid;
        private DateTime sent, next, received;
        private LegacyDetails cached;
        private LegacyDetails pending;
        private string partialResponse;
        private int factorStart, factorGroup;
        private void ResetPending() { pending = null; partialResponse = null; factorStart = 0; factorGroup = 0; }
        internal LegacyDetails Read(int gamePid, CareerSnapshot career)
        {
            string directory = AppDomain.CurrentDomain.BaseDirectory;
            try
            {
                string marker = Path.Combine(directory, "bridge-inspect-extras.enabled");
                if (career == null || !career.Legacy || !File.Exists(marker) || File.ReadAllText(marker).Trim() != gamePid.ToString()) { cached = null; token = null; ResetPending(); return null; }
                if (pid != gamePid || (pending != null && !pending.Matches(career))) { pid = gamePid; cached = null; token = null; ResetPending(); next = DateTime.MinValue; }
                DateTime now = DateTime.UtcNow;
                string responsePath = Path.Combine(directory, "bridge-inspect.response");
                if (token != null && File.Exists(responsePath) && new FileInfo(responsePath).Length <= 262144 && File.GetLastWriteTimeUtc(responsePath) >= sent.AddSeconds(-1))
                {
                    string text = File.ReadAllText(responsePath, Encoding.UTF8).Replace("\r\n", "\n");
                    if (text.Contains("QUERY " + token + "\n")) {
                        string combined = text + (partialResponse ?? "");
                        var piece = LegacyDetails.ParseInspection(combined, true);
                        bool matches = piece != null && piece.Matches(career) && (pending == null || (pending.Id == piece.Id && pending.FactorCount == piece.FactorCount));
                        if (matches) {
                            int count = factorGroup == 0 ? piece.FactorCount : piece.GrandparentFactorCounts[factorGroup - 1];
                            bool complete = factorGroup == 0 ? piece.Factors.Length == count : count < 0 || piece.GrandparentFactors[factorGroup - 1] != null;
                            pending = piece; partialResponse = combined;
                            if (complete) {
                                factorStart = 0; factorGroup++;
                                if (factorGroup == 3) { cached = LegacyDetails.ParseInspection(combined); received = now; ResetPending(); }
                                else { if (cached == null && piece.Factors.Length == piece.FactorCount) { cached = piece; received = now; } next = now; }
                            } else if (factorStart + 28 < count) { factorStart += 28; next = now; }
                            else { cached = null; ResetPending(); }
                        } else { cached = null; ResetPending(); next = now; }
                        token = null;
                    }
                }
                if (token != null && now - sent > TimeSpan.FromSeconds(4)) { token = null; cached = null; ResetPending(); }
                if (token == null && now >= next)
                {
                    token = "SceneManager/__legacy_details_" + gamePid + "_" + Guid.NewGuid().ToString("N");
                    var queries = new List<string> { LegacyDetails.Dialog + "/_headerTitle/_innerOwn/_trainedCharaId" };
                    foreach (string suffix in new[] { "/@get_Id", "/@get_CardId", "/@get_RankScore" }) queries.Add(LegacyDetails.Record + suffix);
                    string root = LegacyDetails.Record;
                    if (factorGroup == 0) foreach (string suffix in new[] { "/@get_Speed", "/@get_Stamina", "/@get_Power", "/@get_Guts", "/@get_Wiz", "/@get_SingleWinNum", "/@get_SingleTotalRaceNum" }) queries.Add(root + suffix);
                    else { root += "/@get_SuccessionChara" + factorGroup; queries.Add(root + "/@get_CardId"); }
                    queries.Add(root + "/FactorDataArray/@get_Length");
                    // The already-loaded inspector accepts 8190 request bytes.
                    // Page the factor array so every request stays within that limit.
                    for (int i = factorStart; i < Math.Min(64, factorStart + 28); i++) queries.Add(root + "/FactorDataArray/[" + i + "]/@get_FactorId");
                    queries.Add(token); sent = now; next = now.AddSeconds(2);
                    File.WriteAllLines(Path.Combine(directory, "bridge-inspect.request"), queries, Encoding.ASCII);
                }
                return cached != null && now - received < TimeSpan.FromSeconds(15) && cached.Matches(career) ? cached : null;
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }
    }
}
