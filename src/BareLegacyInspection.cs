using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace UmapyonPresence
{
    // PID-scoped compatibility for the diagnostic bridge already loaded in the
    // current game. Native packets take priority after the next bridge load.
    internal sealed class BareLegacyInspectionReader
    {
        internal const string Common = "DialogManager/_dialogList/_items/[0]";
        internal const string Dialog = Common + "/_data/CenterButtonCallBack/m_target";
        internal const string Record = Dialog + "/_onCloseDialogWithCharaSwitchButton/m_target/getIndexedSetupParam/m_target/buttonInfo/TrainedChara";
        private string token;
        private DateTime sent, next, received;
        private int pid, group, start, expectedCount = -1, expectedParent;
        private CareerSnapshot cached, pending;
        private readonly List<int> ids = new List<int>();
        private static int Value(string text, string query)
        {
            var match = Regex.Match(text, @"(?:^|\n)QUERY " + Regex.Escape(query) + @"\nVALUE (-?\d+)(?:\n|$)");
            int value; return match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : -1;
        }
        internal static CareerSnapshot Parse(string text)
        {
            text = (text ?? "").Replace("\r\n", "\n");
            int id = Value(text, Record + "/@get_Id"), card = Value(text, Record + "/@get_CardId"), scenario = Value(text, Record + "/@get_ScenarioId");
            int skills = Value(text, Record + "/@get_AcquiredSkillArray/@get_Length"), wins = Value(text, Record + "/@get_SingleWinNum"), races = Value(text, Record + "/@get_SingleTotalRaceNum");
            if (Value(text, Common + "/@get_IsDispClosing") != 0 || Value(text, Common + "/@get_IsDispClosed") != 0 || Value(text, Dialog + "/@get_gameObject/@get_activeInHierarchy") != 1 || Value(text, Record + "/@get_IsSuccessionOnly") != 1 || Value(text, Record + "/@get_RankScore") != 0 ||
                id <= 0 || id != Value(text, Dialog + "/_headerTitle/_innerOwn/_trainedCharaId") || card != Value(text, Dialog + "/_headerTitle/_headerBase/_characterButton/Info/Id") || card < 100000 || card >= 200000 || scenario < 0 || scenario > 1000 || skills < 0 || skills > 512 || wins < 0 || races < wins || races > 10000) return null;
            return new CareerSnapshot { Card = card, RecordId = id, Scenario = scenario, Results = true, Legacy = true, BareLegacy = true, EvaluationScore = 0, SkillCount = skills,
                LegacyDetails = new LegacyDetails { Card = card, Score = 0, Id = id, Wins = wins, Races = races } };
        }
        private void Reset() { cached = pending = null; token = null; group = start = 0; expectedCount = -1; ids.Clear(); }
        internal CareerSnapshot Read(int gamePid, CareerSnapshot native)
        {
            string directory = AppDomain.CurrentDomain.BaseDirectory, marker = Path.Combine(directory, "bridge-inspect-bare.enabled");
            try {
                if (native != null || !File.Exists(marker) || File.ReadAllText(marker).Trim() != gamePid.ToString()) { Reset(); return null; }
                if (pid != gamePid) { pid = gamePid; Reset(); next = DateTime.MinValue; }
                DateTime now = DateTime.UtcNow;
                string response = Path.Combine(directory, "bridge-inspect.response");
                if (token != null && File.Exists(response) && new FileInfo(response).Length <= 262144 && File.GetLastWriteTimeUtc(response) >= sent.AddSeconds(-1)) {
                    string text = File.ReadAllText(response, Encoding.UTF8).Replace("\r\n", "\n");
                    if (text.Contains("QUERY " + token + "\n")) {
                        var current = Parse(text); token = null;
                        if (current == null) { Reset(); next = now.AddSeconds(1); }
                        else {
                            if (pending != null && (pending.RecordId != current.RecordId || pending.Card != current.Card || pending.SkillCount != current.SkillCount || pending.LegacyDetails.Wins != current.LegacyDetails.Wins || pending.LegacyDetails.Races != current.LegacyDetails.Races)) { Reset(); next = now; }
                            else {
                                if (pending == null) pending = current;
                                string root = Record + (group == 0 ? "" : "/@get_SuccessionChara" + group);
                                int count = Value(text, root + "/FactorDataArray/@get_Length"), parent = group == 0 ? current.Card : Value(text, root + "/@get_CardId");
                                bool absent = group > 0 && (parent < 100000 || parent >= 200000);
                                bool valid = absent || (count >= 0 && count <= 64);
                                if (start == 0) { expectedCount = count; expectedParent = parent; }
                                else if (count != expectedCount || parent != expectedParent) valid = false;
                                if (cached != null && cached.RecordId == current.RecordId && cached.Card == current.Card && valid) received = now;
                                if (!absent && valid) for (int i = start; i < Math.Min(count, start + 12); i++) { int id = Value(text, root + "/FactorDataArray/[" + i + "]/@get_FactorId"); if (id <= 0) { valid = false; break; } ids.Add(id); }
                                if (!valid) { Reset(); next = now.AddSeconds(1); }
                                else if (absent || ids.Count == count) {
                                    if (!absent) {
                                        if (group == 0) pending.LegacyDetails.Factors = ids.ToArray();
                                        else { pending.LegacyDetails.GrandparentCards[group - 1] = parent; pending.LegacyDetails.GrandparentFactors[group - 1] = ids.ToArray(); }
                                    }
                                    group++; start = 0; expectedCount = -1; ids.Clear();
                                    if (group == 3) { cached = pending; pending = null; group = 0; received = now; next = now.AddSeconds(1); }
                                    else next = now;
                                } else { start += 12; next = now; }
                            }
                        }
                    }
                }
                if (token != null && now - sent > TimeSpan.FromSeconds(4)) { Reset(); next = now; }
                if (token == null && now >= next) {
                    var queries = new List<string> { Common + "/@get_IsDispClosing", Common + "/@get_IsDispClosed", Dialog + "/@get_gameObject/@get_activeInHierarchy", Dialog + "/_headerTitle/_innerOwn/_trainedCharaId", Dialog + "/_headerTitle/_headerBase/_characterButton/Info/Id" };
                    foreach (string suffix in new[] { "/@get_Id", "/@get_CardId", "/@get_ScenarioId", "/@get_IsSuccessionOnly", "/@get_RankScore", "/@get_AcquiredSkillArray/@get_Length", "/@get_SingleWinNum", "/@get_SingleTotalRaceNum" }) queries.Add(Record + suffix);
                    string root = Record + (group == 0 ? "" : "/@get_SuccessionChara" + group);
                    if (group > 0) queries.Add(root + "/@get_CardId");
                    queries.Add(root + "/FactorDataArray/@get_Length");
                    for (int i = start; i < Math.Min(64, start + 12); i++) queries.Add(root + "/FactorDataArray/[" + i + "]/@get_FactorId");
                    token = "SceneManager/__bare_" + gamePid + "_" + Guid.NewGuid().ToString("N"); queries.Add(token);
                    string request = string.Join("\n", queries) + "\n";
                    if (Encoding.ASCII.GetByteCount(request) > 8190) { Reset(); return null; }
                    File.WriteAllText(Path.Combine(directory, "bridge-inspect.request"), request, Encoding.ASCII); sent = now; next = now.AddSeconds(1);
                }
                return cached != null && now - received < TimeSpan.FromSeconds(3) ? cached : null;
            } catch (IOException) { Reset(); return null; } catch (UnauthorizedAccessException) { Reset(); return null; }
        }
    }
}
