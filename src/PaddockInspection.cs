using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace UmapyonPresence
{
    // Compatibility for the diagnostic DLL already loaded in this session.
    // The normal bridge fixes the underlying nested-record traversal directly.
    internal sealed class PaddockInspectionReader
    {
        internal const string Controller = "SceneManager/_currentViewController";
        private string token;
        private int pid;
        private DateTime sent, received, next;
        private RaceSnapshot cached;
        private static int Value(string text, string query)
        {
            var m = Regex.Match(text, @"(?:^|\n)QUERY " + Regex.Escape(query) + @"\r?\nVALUE (-?\d+)(?:\r?\n|$)");
            int n; return m.Success && int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) ? n : -1;
        }
        internal static RaceSnapshot Parse(string text, RaceSnapshot seed)
        {
            text = (text ?? "").Replace("\r\n", "\n");
            if (!Regex.IsMatch(text, @"QUERY SceneManager/_currentViewController\nCLASS Gallop\.\w*PaddockViewController\n") || !text.Contains("FIELD _isRaceStart : Boolean = false\n")) return null;
            int team = Value(text, Controller + "/_myTeamId"), count = Value(text, Controller + "/_horseDataArray/@get_Length");
            if (team < 0 || count < 1 || count > 18) return null;
            var entries = new List<RaceEntry>();
            for (int i = 0; i < count; i++) {
                string horse = Controller + "/_horseDataArray/[" + i + "]";
                if (Value(text, horse + "/@get_TeamId") != team) continue;
                int id = Value(text, horse + "/charaId"), gate = Value(text, horse + "/@get_GateNo");
                if (id < 1000 || id >= 2000 || gate < 1 || gate > 64) return null;
                entries.Add(new RaceEntry { CharacterId = id, Gate = gate });
            }
            if (entries.Count < 1 || entries.Count > 3 || entries.Select(e => e.Gate).Distinct().Count() != entries.Count) return null;
            var anchor = seed == null ? null : seed.Team.FirstOrDefault(e => e.Gate > 0);
            int at = anchor == null ? -1 : entries.FindIndex(e => e.Gate == anchor.Gate && e.CharacterId == anchor.CharacterId);
            if (at > 0) entries = entries.Skip(at).Concat(entries.Take(at)).ToList();
            int program = Value(text, Controller + "/_info/raceInfo/@get_RaceInstanceId");
            return new RaceSnapshot { Preparing = true, Team = entries.ToArray(), Program = seed == null ? Math.Max(0, program) : seed.Program, ProgramType = seed == null ? 2 : seed.ProgramType, Course = seed == null ? 0 : seed.Course, Distance = seed == null ? 0 : seed.Distance, Kind = seed == null ? (text.Contains("CLASS Gallop.ChampionsPaddockViewController\n") ? 1 : 0) : seed.Kind, Runners = count };
        }
        internal RaceSnapshot Read(int gamePid, NativeMenuSnapshot menu, RaceSnapshot seed)
        {
            string directory = AppDomain.CurrentDomain.BaseDirectory;
            try {
                string marker = Path.Combine(directory, "bridge-inspect-paddock.enabled");
                if (menu == null || !(menu.Root ?? "").Contains("Paddock") || (seed != null && !seed.Preparing) || !File.Exists(marker) || File.ReadAllText(marker).Trim() != gamePid.ToString()) { cached = null; token = null; return null; }
                if (pid != gamePid) { pid = gamePid; cached = null; token = null; next = DateTime.MinValue; }
                DateTime now = DateTime.UtcNow; string path = Path.Combine(directory, "bridge-inspect.response");
                if (token != null && File.Exists(path) && new FileInfo(path).Length <= 262144 && File.GetLastWriteTimeUtc(path) >= sent.AddSeconds(-1)) {
                    string text = File.ReadAllText(path, Encoding.UTF8).Replace("\r\n", "\n");
                    if (text.Contains("QUERY " + token + "\n")) { cached = Parse(text, seed); received = now; token = null; }
                }
                if (token != null && now - sent > TimeSpan.FromSeconds(4)) { cached = null; token = null; }
                if (token == null && now >= next) {
                    token = "SceneManager/__paddock_" + gamePid + "_" + Guid.NewGuid().ToString("N");
                    var queries = new List<string> { Controller, Controller + "/_myTeamId", Controller + "/_horseDataArray/@get_Length", Controller + "/_info/raceInfo/@get_RaceInstanceId" };
                    queries.Add(Controller + "/_info/raceInfo/@get_Weather"); queries.Add(Controller + "/_info/raceInfo/@get_GroundCondition");
                    queries.Add("#Weather"); queries.Add("#GroundCondition");
                    for (int i = 0; i < 18; i++) { string horse = Controller + "/_horseDataArray/[" + i + "]"; queries.Add(horse + "/charaId"); queries.Add(horse + "/@get_TeamId"); queries.Add(horse + "/@get_GateNo"); }
                    queries.Add(token); sent = now; next = now.AddSeconds(2);
                    File.WriteAllLines(Path.Combine(directory, "bridge-inspect.request"), queries, Encoding.ASCII);
                }
                return cached != null && now - received < TimeSpan.FromSeconds(4) && (seed == null || seed.Program == cached.Program) ? cached : null;
            } catch (IOException) { return null; } catch (UnauthorizedAccessException) { return null; }
        }
    }
}
