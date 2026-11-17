using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace UmapyonPresence
{
    internal sealed class RaceMetrics
    {
        internal int Flags;
        // Canonical labels from the game's official margin formatter, never loss-reason enums.
        internal int Margin;
        internal float Current, Peak, Finish, Seconds, GapSeconds, GapLengths;
        private static string Number(float n, string format = "0.0") { return n.ToString(format, CultureInfo.InvariantCulture); }
        private static string Lengths(float n) { return n > 0 && n < .1f ? "<0.1" : Number(n); }
        internal string Tooltip(RaceEntry entry, bool results, bool podiumSweep = false)
        {
            bool winner = entry.Position == 1, overSecond = (Flags & 32) != 0;
            bool official = (Flags & 64) != 0, showLength = !results || official;
            bool distantWin = results && official && winner && overSecond && Margin == 4;
            var lines = new List<string> { (results ? entry.Name : entry.ShortName) + " — " + RaceSnapshot.Ordinal(entry.Position) };
            if (distantWin) lines.Add("The Rest Nowhere");
            if (results && podiumSweep) lines.Insert(0, RaceSnapshot.SweepHeading);
            var speeds = new List<string>();
            if ((Flags & 4) != 0 && (results || (Flags & 1) == 0)) speeds.Add((distantWin ? "C: " : "Finish: ") + Number(Finish));
            if (!results && (Flags & 1) != 0) speeds.Add("Current: " + Number(Current));
            if ((Flags & 2) != 0) speeds.Add((distantWin ? "P: " : "Peak: ") + Number(Peak));
            int speedRow = lines.Count;
            if (speeds.Count > 0) {
                string speedLine = string.Join(" | ", speeds) + " m/s";
                // The live label is wider than Finish; narrow spaces retain the full words.
                // Protect punctuation too: a non-breaking space alone doesn't protect a pipe.
                if (!results) speedLine = speedLine.Replace(' ', '\u202f').Replace("|", "\u2060|\u2060").Replace("/", "\u2060/\u2060");
                lines.Add(speedLine);
            }
            var extra = new List<string>();
            if (results && (Flags & 8) != 0) { int tenths = (int)Math.Round(Seconds * 10, MidpointRounding.AwayFromZero); extra.Add((tenths / 600) + ":" + ((tenths % 600) / 10.0).ToString("00.0", CultureInfo.InvariantCulture)); }
            string close = !official ? null : Margin == 1 ? "nose" : Margin == 2 ? "head" : Margin == 3 ? "neck" : null;
            if (winner && showLength && overSecond && !distantWin) extra.Add(results ? (close == null ? "Won by " + Number(GapLengths, "0.##") + (GapLengths == 1 ? " length" : " lengths") : "Won by a " + close + "!") : Lengths(GapLengths) + " L over 2nd");
            if (results && entry.Position == 2 && close != null) extra.Add("Lost by a " + close);
            else if (results && official && entry.Position == 2 && Margin == 4) extra.Add("Large margin behind 1st");
            else if (entry.Position > 1 && showLength && (Flags & 16) != 0) extra.Add((results ? Number(GapLengths, "0.##") : Lengths(GapLengths)) + " L behind first");
            else if (results && entry.Position > 1 && (Flags & 8) != 0) extra.Add("+" + Number(GapSeconds, "0.00") + "s to first");
            Func<string> pack = () => string.Join(" ", lines.Concat(extra.Count == 0 ? new string[0] : new[] { string.Join(" • ", extra) }).Select((line, index) => {
                string row = line.Replace(' ', '\u00a0');
                if (distantWin && index == speedRow) {
                    row = row.Replace("|", "\u2060|\u2060").Replace("/", "\u2060/\u2060");
                    // The short C/P row can otherwise share a line with the time.
                    // Balanced figure spaces widen this row without shifting its centre.
                    if (!podiumSweep) row = "\u2007\u2007" + row + "\u2007\u2007";
                }
                return row;
            }));
            // Preserve the complete margin and sweep note under Discord's byte limit.
            // With a long name/large-margin phrase, omit optional time then peak rather than
            // clipping the ending of the result claim.
            if (Encoding.UTF8.GetByteCount(pack()) > 128 && results && (Flags & 8) != 0 && extra.Count > 1) extra.RemoveAt(0);
            if (Encoding.UTF8.GetByteCount(pack()) > 128 && results && speeds.Count > 1) lines[speedRow] = speeds[0] + " m/s";
            // Discord collapses newlines. Keep each intended row as one wrapping group.
            return pack();
        }
        internal static bool Apply(byte[] bytes, RaceSnapshot race)
        {
            if (bytes == null || bytes.Length < 152 || race == null || race.Preparing || BitConverter.ToUInt32(bytes, 0) != 0x524d5431 || BitConverter.ToInt32(bytes, 4) != 1 || BitConverter.ToInt32(bytes, 8) != race.Program || BitConverter.ToInt32(bytes, 12) != race.ProgramType || BitConverter.ToInt32(bytes, 16) != race.Team.Length || BitConverter.ToInt32(bytes, 20) != (race.Results ? 2 : 1)) return false;
            var metrics = new Dictionary<string, RaceMetrics>();
            for (int i = 0; i < race.Team.Length; i++) {
                int offset = 32 + i * 40, chara = BitConverter.ToInt32(bytes, offset), gate = BitConverter.ToInt32(bytes, offset + 4), flags = BitConverter.ToInt32(bytes, offset + 8), margin = BitConverter.ToInt32(bytes, offset + 12);
                var values = Enumerable.Range(0, 6).Select(j => BitConverter.ToSingle(bytes, offset + 16 + j * 4)).ToArray();
                if (chara < 1000 || chara >= 2000 || gate < 1 || gate > 64 || flags < 0 || flags > 127 || (flags & 48) == 48 || ((flags & 64) != 0 && (!race.Results || (flags & 48) == 0)) || margin < 0 || margin > 4 || (!race.Results && margin != 0) || values.Any(v => float.IsNaN(v) || float.IsInfinity(v) || v < 0) || values.Take(3).Any(v => v > 100) || values.Skip(3).Any(v => v > 10000)) return false;
                string key = chara + ":" + gate; if (metrics.ContainsKey(key)) return false;
                // RMT1 reserved labels from older bridges came from DefeatType (loss reasons).
                // Only the v23 official-source flag validates a result margin.
                metrics[key] = new RaceMetrics { Flags = flags, Margin = (flags & 64) != 0 ? margin : 0, Current = values[0], Peak = values[1], Finish = values[2], Seconds = values[3], GapSeconds = values[4], GapLengths = values[5] };
            }
            if (race.Team.Any(e => !metrics.ContainsKey(e.CharacterId + ":" + e.Gate))) return false;
            foreach (var entry in race.Team) { var metric = metrics[entry.CharacterId + ":" + entry.Gate]; if (metric.Flags != 0) entry.Metrics = metric; }
            return true;
        }
    }
}
