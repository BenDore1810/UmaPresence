using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace UmapyonPresence
{
    // Opt-in, PID-scoped compatibility for an already-loaded diagnostic bridge.
    // Native official margins always take precedence; no time/distance estimates.
    internal sealed class OfficialMarginInspection
    {
        internal const string Panel = "SceneManager/currentViewController/_resultList";
        internal const string Info = "RaceManager/@get_RaceInfo";
        private string token, pendingIdentity, cachedIdentity;
        private DateTime sent, next, received;
        private string cached;
        private static string Identity(RaceSnapshot race) { return race.ProgramType + ":" + race.Program + ":" + string.Join(",", race.Team.Select(e => e.CharacterId + ":" + e.Gate + ":" + e.Position + ":" + (e.Metrics == null ? "" : e.Metrics.Seconds.ToString("R", CultureInfo.InvariantCulture)))); }
        private static string Read(string text, string path, string type)
        {
            var match = Regex.Match(text, @"(?:^|\n)QUERY " + Regex.Escape(path) + @"\r?\n" + type + @" ([^\r\n]*)");
            return match.Success ? match.Groups[1].Value : null;
        }
        private static int Number(string text, string path) { int n; return int.TryParse(Read(text, path, "VALUE"), out n) ? n : -1; }
        private static string ProgramPath(RaceSnapshot race) { return Info + (race.ProgramType == 1 ? "/@get_ProgramId" : race.ProgramType == 2 ? "/@get_RaceInstanceId" : "/@get_RaceId"); }
        internal static bool Label(string text, out int kind, out float lengths)
        {
            kind = 0; lengths = 0;
            string value = Regex.Replace((text ?? "").Replace("$(nb)", " "), "<[^>]*>", "").Trim().ToLowerInvariant();
            switch (value) {
                case "nose": case "a nose": case "ns": case "ハナ": kind = 1; return true;
                case "head": case "a head": case "hd": case "アタマ": kind = 2; return true;
                case "neck": case "a neck": case "nk": case "クビ": kind = 3; return true;
                case "distance": case "dst": case "large margin": case "大差": kind = 4; return true;
            }
            value = value.Replace("½", " 1/2").Replace("¼", " 1/4").Replace("¾", " 3/4").Replace('\u2044', '/').Replace('\u00a0', ' ').Replace('\u202f', ' ');
            var match = Regex.Match(value, @"^(?:(\d+(?:\.\d+)?)\s+)?(\d+)\s*/\s*(\d+)\s*(?:lengths?|l|バ身)?$");
            if (match.Success) {
                float whole = match.Groups[1].Success ? float.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
                float denominator = float.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
                if (denominator <= 0 || denominator > 64) return false;
                lengths = whole + float.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) / denominator;
            } else {
                match = Regex.Match(value, @"^(\d+(?:\.\d+)?)\s*(?:lengths?|l|バ身)?$");
                if (!match.Success || !float.TryParse(match.Groups[1].Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out lengths)) return false;
            }
            return lengths > 0 && lengths <= 1000;
        }
        internal static bool Apply(string text, RaceSnapshot race)
        {
            if (race == null || !race.Results || race.Runners < 2 || race.Runners > 64 || Number(text, Panel + "/@get_gameObject/@get_activeInHierarchy") != 1 || Number(text, ProgramPath(race)) != race.Program || Number(text, Panel + "/_containerList/_size") != race.Runners) return false;
            int rows = Math.Min(3, race.Runners); var charas = new int[rows]; var gates = new int[rows]; var times = new float[rows]; var labels = new string[rows];
            for (int i = 0; i < rows; i++) {
                string root = Panel + "/_containerList/_items/[" + i + "]";
                charas[i] = Number(text, root + "/_horseData/charaId"); gates[i] = Number(text, root + "/_horseData/@get_GateNo");
                if (Number(text, root + "/_horseData/FinishOrder") != i || charas[i] < 1000 || charas[i] >= 2000 || gates[i] < 1 || gates[i] > race.Runners || gates.Take(i).Contains(gates[i]) || !float.TryParse(Read(text, root + "/_horseData/FinishTimeScaled", "DECIMAL"), NumberStyles.Float, CultureInfo.InvariantCulture, out times[i]) || float.IsNaN(times[i]) || float.IsInfinity(times[i]) || times[i] <= 0) return false;
                labels[i] = Read(text, root + "/_recordText/@get_text", "TEXT");
            }
            foreach (var entry in race.Team.Where(e => e.Position <= rows)) {
                int r = entry.Position - 1;
                if (r < 0 || entry.CharacterId != charas[r] || entry.Gate != gates[r] || entry.Metrics == null || Math.Abs(entry.Metrics.Seconds - times[r]) > .001) return false;
            }
            int winningKind; float winningLengths;
            if (!Label(labels[1], out winningKind, out winningLengths)) return false;
            bool applied = false;
            foreach (var entry in race.Team.Where(e => e.Position <= rows && e.Metrics != null && (e.Metrics.Flags & 64) == 0)) {
                int r = entry.Position - 1, kind = winningKind; float lengths = winningLengths;
                if (r > 1) {
                    if (kind != 0) continue;
                    int previous; float gap; if (!Label(labels[2], out previous, out gap) || previous != 0) continue;
                    lengths += gap;
                }
                entry.Metrics.Flags = (entry.Metrics.Flags & ~48) | 64 | (r == 0 ? 32 : 16);
                entry.Metrics.Margin = kind; entry.Metrics.GapLengths = lengths; applied = true;
            }
            return applied;
        }
        internal void Read(int pid, RaceSnapshot race)
        {
            string directory = AppDomain.CurrentDomain.BaseDirectory;
            try {
                string marker = Path.Combine(directory, "bridge-inspect-margins.enabled");
                if (race == null || !race.Results || race.Team.All(e => e.Metrics == null || (e.Metrics.Flags & 64) != 0) || !File.Exists(marker) || File.ReadAllText(marker).Trim() != pid.ToString()) { token = cached = null; return; }
                string identity = Identity(race); DateTime now = DateTime.UtcNow;
                string response = Path.Combine(directory, "bridge-inspect.response");
                if (token != null && File.Exists(response) && new FileInfo(response).Length < 262144 && File.GetLastWriteTimeUtc(response) >= sent.AddSeconds(-1)) {
                    string text = File.ReadAllText(response);
                    if (text.Contains("QUERY " + token + "\n") || text.Contains("QUERY " + token + "\r\n")) { cached = pendingIdentity == identity ? text : null; cachedIdentity = pendingIdentity; received = now; token = null; }
                }
                if (token != null && now - sent > TimeSpan.FromSeconds(4)) token = null;
                if (cached != null && cachedIdentity == identity && now - received < TimeSpan.FromSeconds(3)) Apply(cached, race);
                if (token == null && now >= next) {
                    var queries = new List<string> { ProgramPath(race), Panel + "/@get_gameObject/@get_activeInHierarchy", Panel + "/_containerList/_size" };
                    for (int i = 0; i < Math.Min(3, race.Runners); i++) {
                        string root = Panel + "/_containerList/_items/[" + i + "]";
                        foreach (string suffix in new[] { "/_horseData/charaId", "/_horseData/@get_GateNo", "/_horseData/FinishOrder", "/_horseData/FinishTimeScaled", "/_recordText/@get_text" }) queries.Add(root + suffix);
                    }
                    token = "SceneManager/__official_margin_" + Guid.NewGuid().ToString("N"); queries.Add(token); sent = now; next = now.AddSeconds(2); pendingIdentity = identity;
                    File.WriteAllLines(Path.Combine(directory, "bridge-inspect.request"), queries, Encoding.ASCII);
                }
            } catch (IOException) { cached = null; } catch (UnauthorizedAccessException) { cached = null; }
        }
    }
}
