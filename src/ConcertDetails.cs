using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace UmapyonPresence
{
    internal static class ConcertDetails
    {
        private static readonly Dictionary<string, Dictionary<string, string>> labels =
            SongCatalog.Resource<Dictionary<string, Dictionary<string, string>>>("concert-details.json");
        private static string Find(string category, int id, Settings settings)
        {
            if (category == "7" || category == "14" || category == "17") {
                string translated = DataMaintenance.FindTranslation(category, id, settings);
                if (translated != null) return translated;
            }
            Dictionary<string, string> values; string value;
            return labels.TryGetValue(category, out values) && values.TryGetValue(id.ToString(), out value) && !string.IsNullOrWhiteSpace(value) && value != "None" ? value : GameText.Find(category, id, settings);
        }
        internal static string CharacterName(int id, Settings settings)
        {
            var character = CharacterCatalog.Entries.FirstOrDefault(c => c.Id == id.ToString());
            return character == null ? Find("6", id, settings) ?? "Uma " + id : character.Name;
        }
        private static string Group(string row) { return row.Trim().Replace(' ', '\u00a0').Replace("|", "\u2060|\u2060"); }
        private static string TrimBytes(string text, int budget)
        {
            if (budget < 4) return "";
            if (Encoding.UTF8.GetByteCount(text) <= budget) return text;
            while (text.Length > 0 && Encoding.UTF8.GetByteCount(text) > budget - 3) text = text.Substring(0, text.Length - (char.IsLowSurrogate(text[text.Length - 1]) && text.Length > 1 ? 2 : 1));
            return text + "…";
        }
        internal static string UnwrapCostume(string text)
        {
            text = text.Trim();
            if (text.Length < 2 || text[0] != '[') return text;
            int depth = 0;
            for (int i = 0; i < text.Length; i++) {
                if (text[i] == '[') depth++;
                else if (text[i] == ']' && --depth == 0) return i == text.Length - 1 ? text.Substring(1, text.Length - 2).Trim() : text;
            }
            return text;
        }
        private static string Costume(int character, int dress, Settings settings)
        {
            string outfit;
            if (OutfitCatalog.IsRacing(character, dress)) { int card = OutfitCatalog.CardForDress(character, dress); outfit = card > 0 ? CareerLabels.Outfit(card, settings).Trim() : ""; if (string.IsNullOrEmpty(outfit)) outfit = Find("14", dress, settings) ?? "Racing outfit"; }
            else outfit = Find("14", dress, settings) ?? "Stage outfit";
            outfit = UnwrapCostume(outfit);
            if (!OutfitCatalog.IsRacing(character, dress)) {
                outfit = Regex.Replace(outfit.Trim(), @"\s+", " ");
                if (outfit.IndexOf("uniform", StringComparison.OrdinalIgnoreCase) >= 0 && outfit.IndexOf("winter", StringComparison.OrdinalIgnoreCase) >= 0) outfit = "Winter Uniform";
                else if (outfit.IndexOf("uniform", StringComparison.OrdinalIgnoreCase) >= 0 && outfit.IndexOf("summer", StringComparison.OrdinalIgnoreCase) >= 0) outfit = "Summer Uniform";
                else if (Regex.IsMatch(outfit, @"^Casual\s+Wear(?:\s*\(|$)", RegexOptions.IgnoreCase)) outfit = "Casual Wear";
                else outfit = CultureInfo.GetCultureInfo("en-US").TextInfo.ToTitleCase(outfit);
            }
            outfit = Regex.Replace(outfit, @"\s*\[([A-Z0-9]{1,3})\]$", " ($1)");
            return "[" + outfit + "]";
        }
        internal static string CharacterTooltip(int character, int dress, string name, Settings settings)
        {
            string outfit = Costume(character, dress, settings);
            string japaneseCv = Find("7", character, settings);
            if (JapaneseTooltips.ContainsJapanese(name) || JapaneseTooltips.ContainsJapanese(outfit) || JapaneseTooltips.ContainsJapanese(japaneseCv)) return JapaneseTooltips.Character(name, outfit, japaneseCv);
            string cv = Find("7", character, settings), first = Group(name), last = string.IsNullOrEmpty(cv) ? "" : Group("CV: " + cv);
            // Compact protected groups; no artificial padding that widens or shifts the tooltip.
            // Preserve the costume title before the optional CV.
            if (Encoding.UTF8.GetByteCount(first + " " + Group(outfit) + " " + last) > 128) last = "";
            int budget = 128 - Encoding.UTF8.GetByteCount(first) - (last.Length == 0 ? 1 : Encoding.UTF8.GetByteCount(last) + 2);
            // Truncate inside brackets so every costume retains its closing bracket.
            string middle = "[" + TrimBytes(Group(outfit.Substring(1, outfit.Length - 2)), budget - 2) + "]";
            return ActivityState.Limit(string.Join(" ", new[] { first, middle, last }.Where(row => row.Length > 0)));
        }
    }
}
