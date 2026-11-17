using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace UmapyonPresence
{
    // Same-session compatibility for the already-loaded inspection bridge.
    // Only the live-verified CM result panel is supported here.
    internal sealed class RaceResultInspectionReader
    {
        internal const string Info = "RaceManager/@get_RaceInfo";
        internal const string Active = "SceneManager/_currentViewController/_raceResultChampionsView/@get_gameObject/@get_activeInHierarchy";
        private string token;
        private DateTime sent, next, received;
        private RaceSnapshot cached;
        private static int Value(string text, string query)
        {
            var m = Regex.Match(text, @"(?:^|\n)QUERY " + Regex.Escape(query) + @"\r?\nVALUE (-?\d+)(?:\r?\n|$)");
            int n; return m.Success && int.TryParse(m.Groups[1].Value, out n) ? n : -1;
        }
        internal static RaceSnapshot Parse(string text, RaceSnapshot seed)
        {
            if (seed == null || seed.Kind != 1 || seed.Preparing || seed.Team.Length < 1 || seed.Team.Length > 3 || Value(text, Active) != 1 || Value(text, Info + "/@get_RaceInstanceId") != seed.Program) return null;
            int count = Value(text, Info + "/@get_RaceHorse/@get_Length");
            if (count < 1 || count > 18) return null;
            var all = Enumerable.Range(0, count).Select(i => {
                string path = Info + "/@get_RaceHorse/[" + i + "]";
                return new RaceEntry { CharacterId = Value(text, path + "/charaId"), Gate = Value(text, path + "/@get_GateNo"), Position = Value(text, path + "/FinishOrder") };
            }).ToArray();
            // Infer the base from a complete permutation, never from one entrant.
            var ranks = all.Select(e => e.Position).OrderBy(n => n).ToArray();
            int offset = ranks.SequenceEqual(Enumerable.Range(0, count)) ? 1 : ranks.SequenceEqual(Enumerable.Range(1, count)) ? 0 : -1;
            if (offset < 0) return null;
            var team = seed.Team.Select(e => all.Where(a => a.Gate == e.Gate && a.CharacterId == e.CharacterId).ToArray()).ToArray();
            if (team.Any(matches => matches.Length != 1)) return null;
            return new RaceSnapshot { Results = true, Program = seed.Program, ProgramType = seed.ProgramType, Course = seed.Course, Distance = seed.Distance, Meters = seed.Distance, Kind = seed.Kind, Runners = count,
                Team = team.Select(matches => new RaceEntry { CharacterId = matches[0].CharacterId, Gate = matches[0].Gate, Position = matches[0].Position + offset }).ToArray() };
        }
        internal RaceSnapshot Read(int gamePid, NativeMenuSnapshot menu, RaceSnapshot seed)
        {
            string directory = AppDomain.CurrentDomain.BaseDirectory;
            try {
                string marker = Path.Combine(directory, "bridge-inspect-paddock.enabled");
                if (seed == null || seed.Kind != 1 || seed.Preparing || seed.Runners < 1 || seed.Runners > 18 || menu == null || menu.Root != "RaceMainViewController" || !File.Exists(marker) || File.ReadAllText(marker).Trim() != gamePid.ToString()) { token = null; cached = null; return null; }
                DateTime now = DateTime.UtcNow; string response = Path.Combine(directory, "bridge-inspect.response");
                if (token != null && File.Exists(response) && new FileInfo(response).Length <= 262144 && File.GetLastWriteTimeUtc(response) >= sent.AddSeconds(-1)) {
                    string text = File.ReadAllText(response, Encoding.UTF8).Replace("\r\n", "\n");
                    if (text.Contains("QUERY " + token + "\n")) { cached = Parse(text, seed); received = now; token = null; }
                }
                if (token != null && now - sent > TimeSpan.FromSeconds(4)) { cached = null; token = null; }
                if (token == null && now >= next) {
                    token = "SceneManager/__result_" + gamePid + "_" + Guid.NewGuid().ToString("N");
                    var queries = new System.Collections.Generic.List<string> { Active, Info + "/@get_RaceInstanceId", Info + "/@get_RaceHorse/@get_Length" };
                    for (int i = 0; i < seed.Runners; i++) { string path = Info + "/@get_RaceHorse/[" + i + "]"; queries.Add(path + "/charaId"); queries.Add(path + "/@get_GateNo"); queries.Add(path + "/FinishOrder"); }
                    queries.Add(token); sent = now; next = now.AddSeconds(2);
                    File.WriteAllLines(Path.Combine(directory, "bridge-inspect.request"), queries, Encoding.ASCII);
                }
                return cached != null && cached.Program == seed.Program && now - received < TimeSpan.FromSeconds(4) ? cached : null;
            } catch (IOException) { return null; } catch (UnauthorizedAccessException) { return null; }
        }
    }
}
