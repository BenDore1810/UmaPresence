using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace UmapyonPresence
{
    // Japanese-only layout. English formatters and their output stay unchanged.
    internal static class JapaneseTooltips
    {
        internal static string ScenarioTitle(string title)
        {
            return (title ?? "").Contains("駿大祭") ? "駿大祭" : title;
        }
        internal static bool ContainsJapanese(string text)
        {
            return (text ?? "").Any(c => (c >= '\u3040' && c <= '\u30ff') || (c >= '\u3400' && c <= '\u9fff') || (c >= '\uff66' && c <= '\uff9f'));
        }
        private static List<string> Elements(string text)
        {
            var result = new List<string>(); var iterator = StringInfo.GetTextElementEnumerator(text);
            while (iterator.MoveNext()) result.Add(iterator.GetTextElement()); return result;
        }
        internal static string Protect(string text)
        {
            text = (text ?? "").Trim().Replace(' ', '\u00a0');
            var parts = Elements(text); var result = new StringBuilder();
            for (int i = 0; i < parts.Count; i++) {
                // NBSP protects Latin words; Japanese has implicit break points
                // between glyphs, so protect those boundaries with zero-width WJ.
                if (i > 0 && parts[i - 1] != "\u2060" && parts[i] != "\u2060" && (ContainsJapanese(parts[i - 1]) || ContainsJapanese(parts[i]))) result.Append('\u2060');
                result.Append(parts[i]);
            }
            return result.ToString();
        }
        private static int Cells(string text) { return Elements(text).Sum(element => ContainsJapanese(element) ? 2 : 1); }
        internal static string Bounded(string raw, int bytes)
        {
            bool bracketed = raw.Length >= 2 && raw[0] == '[' && raw[raw.Length - 1] == ']';
            string body = bracketed ? raw.Substring(1, raw.Length - 2) : raw;
            string first = bracketed ? "[" : "", last = bracketed ? "]" : "";
            Func<string, string> pack = value => Protect(first + value + last);
            string full = pack(body);
            if (Encoding.UTF8.GetByteCount(full) <= bytes && Cells(raw) <= 28) return full;
            var parts = Elements(body);
            while (parts.Count > 0) {
                parts.RemoveAt(parts.Count - 1);
                string candidate = first + string.Concat(parts) + "…" + last;
                string protectedText = Protect(candidate);
                if (Encoding.UTF8.GetByteCount(protectedText) <= bytes && Cells(candidate) <= 28) return protectedText;
            }
            return bytes >= (bracketed ? 5 : 3) ? first + "…" + last : "";
        }
        internal static string Character(string name, string outfit, string cv)
        {
            string first = Protect(name), last = string.IsNullOrEmpty(cv) ? "" : Protect("CV: " + cv);
            if (Encoding.UTF8.GetByteCount(first + " " + Protect(outfit) + " " + last) > 128) last = "";
            if (Encoding.UTF8.GetByteCount(first) > 64) first = Bounded(name, 64);
            int budget = 128 - Encoding.UTF8.GetByteCount(first) - (last.Length == 0 ? 1 : Encoding.UTF8.GetByteCount(last) + 2);
            string middle = Bounded(outfit, budget);
            return string.Join(" ", new[] { first, middle, last }.Where(row => row.Length > 0));
        }
        internal static string Other(string text)
        {
            if (!ContainsJapanese(text)) return text;
            var groups = text.Replace("\r\n", " ").Replace('\n', ' ').Split(new[] { ' ', '\u2028' }, StringSplitOptions.RemoveEmptyEntries);
            // One existing joiner does not prove that every Japanese boundary is
            // protected. Already-protected labels must also pass the byte budget.
            if (Encoding.UTF8.GetByteCount(text) <= 128 && groups.Where(ContainsJapanese).All(group => Protect(group) == group)) return text;
            // Do not charge existing protection bytes to the numerical/Latin
            // data budget; the Japanese rows are protected again below.
            for (int i = 0; i < groups.Length; i++) if (ContainsJapanese(groups[i])) groups[i] = groups[i].Replace("\u2060", "");
            Func<string, bool> labelOnly = group => !group.Any(c => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'));
            int[] fixedParts = groups.Select(group => ContainsJapanese(group) && labelOnly(group) ? 0 : Encoding.UTF8.GetByteCount(string.Concat(Elements(group).Where(element => !ContainsJapanese(element))))).ToArray();
            int fixedBytes = groups.Length - 1 + fixedParts.Sum();
            int remaining = Math.Max(0, 128 - fixedBytes), count = groups.Count(ContainsJapanese);
            for (int i = 0; i < groups.Length; i++) if (ContainsJapanese(groups[i])) {
                string source = groups[i].Replace("\u2060", "");
                int share = count == 1 ? remaining : remaining / count;
                int budget = fixedParts[i] + share;
                if (labelOnly(source)) { groups[i] = Bounded(source, budget); remaining -= Encoding.UTF8.GetByteCount(groups[i]); count--; continue; }
                var parts = Elements(source); string candidate = Protect(source); bool shortened = false;
                while ((Encoding.UTF8.GetByteCount(candidate) > budget || Cells(string.Concat(parts)) > 28) && parts.Any(ContainsJapanese)) {
                    int index = parts.FindLastIndex(ContainsJapanese);
                    parts.RemoveAt(index);
                    if (!shortened) { parts.Insert(index, "…"); shortened = true; }
                    candidate = Protect(string.Concat(parts));
                }
                // Preserve every non-Japanese component: rank, speeds, time,
                // win record and factor counts must not be shortened with a name.
                if (Encoding.UTF8.GetByteCount(candidate) > budget) candidate = string.Concat(parts.Where(part => part != "…"));
                groups[i] = candidate; remaining -= Math.Max(0, Encoding.UTF8.GetByteCount(candidate) - fixedParts[i]); count--;
            }
            return string.Join(" ", groups.Where(group => group.Length > 0));
        }
        internal static object Apply(object activity)
        {
            var payload = activity as Dictionary<string, object>; object value;
            if (payload == null || !payload.TryGetValue("assets", out value)) return activity;
            var assets = value as Dictionary<string, object>; if (assets == null) return activity;
            foreach (string field in new[] { "large_text", "small_text" }) {
                object text; if (assets.TryGetValue(field, out text) && text is string && ContainsJapanese((string)text)) assets[field] = Other((string)text);
            }
            return activity;
        }
    }
}
