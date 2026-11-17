using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace UmapyonPresence
{
    internal sealed class CareerExtras
    {
        public int Flags, Energy, MaxEnergy, Motivation, Fans, Remaining;
        public string Goal;
        internal static CareerExtras Parse(byte[] bytes, CareerSnapshot career)
        {
            if (career == null || career.Results || bytes == null || bytes.Length < 232 || BitConverter.ToUInt32(bytes, 0) != 0x43455831 || BitConverter.ToInt32(bytes, 4) != 1 || BitConverter.ToInt32(bytes, 8) != career.Card || BitConverter.ToInt32(bytes, 12) != career.Turn) return null;
            int[] n = Enumerable.Range(0, 6).Select(i => BitConverter.ToInt32(bytes, 16 + i * 4)).ToArray();
            if (n[0] < 0 || n[0] > 31 || ((n[0] & 1) != 0 && (n[1] < 0 || n[2] < 1 || n[2] > 1000 || n[1] > n[2])) || ((n[0] & 2) != 0 && (n[3] < 1 || n[3] > 5)) || ((n[0] & 4) != 0 && (n[4] < 0 || n[4] > 1000000000)) || ((n[0] & 8) != 0 && (n[5] < 0 || n[5] > 200))) return null;
            int end = Array.IndexOf(bytes, (byte)0, 40, 192); if (end < 0) return null;
            string goal;
            try { goal = new UTF8Encoding(false, true).GetString(bytes, 40, end - 40); } catch (DecoderFallbackException) { return null; }
            return new CareerExtras { Flags = n[0], Energy = n[1], MaxEnergy = n[2], Motivation = n[3], Fans = n[4], Remaining = n[5], Goal = Clean(goal) };
        }
        internal static string Clean(string text) { return Regex.Replace(Regex.Replace((text ?? "").Replace("\\n", " "), "<[^>]*>", ""), @"\s+", " ").Trim(); }
        internal static string Rows(IEnumerable<string> rows)
        {
            var groups = rows.Where(row => !string.IsNullOrWhiteSpace(row)).Select(row => Clean(row).Replace(' ', '\u00a0')).ToList();
            // Shorten whole rows at Unicode text-element boundaries, never cut a
            // UTF-8 sequence or a closing bracket. Discord collapses literal newlines.
            while (Encoding.UTF8.GetByteCount(string.Join(" ", groups)) > 128 && groups.Count > 1) groups.RemoveAt(0);
            return ActivityState.Limit(string.Join(" ", groups));
        }
        internal string Tooltip(string scenario)
        {
            var rows = new List<string> { Clean(scenario) };
            var energy = new List<string>();
            if ((Flags & 1) != 0) energy.Add("Energy: " + Energy + "/" + MaxEnergy);
            if ((Flags & 2) != 0) energy.Add(new[] { "", "Awful", "Bad", "Normal", "Good", "Great" }[Motivation]);
            if (energy.Count > 0) rows.Add(string.Join(" | ", energy));
            int goalRow = -1;
            if ((Flags & 16) != 0 && !string.IsNullOrEmpty(Goal)) { goalRow = rows.Count; rows.Add("Next: " + Goal); }
            if ((Flags & 8) != 0) rows.Add(Remaining == 0 ? "Objective turn" : Remaining + (Remaining == 1 ? " turn left" : " turns left"));
            Func<int> size = () => Encoding.UTF8.GetByteCount(string.Join(" ", rows.Select(row => row.Replace(' ', '\u00a0'))));
            if (size() > 128 && rows.Count > 1) { rows.RemoveAt(0); goalRow--; }
            if (goalRow >= 0) {
                string text = rows[goalRow];
                while (size() > 128 && text.Length > 6) {
                    var elements = StringInfo.ParseCombiningCharacters(text);
                    if (elements.Length < 2) break;
                    text = text.Substring(0, elements[elements.Length - 1]); rows[goalRow] = text.TrimEnd() + "…";
                }
                if (size() > 128) rows.RemoveAt(goalRow);
            }
            return Rows(rows);
        }
    }
}
