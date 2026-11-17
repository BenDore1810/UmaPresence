using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace UmapyonPresence
{
    // Compatibility for the already-loaded v11 development bridge. Normal
    // releases use the native result packet; this requires an explicit marker.
    internal sealed class LegacyInspectionReader
    {
        private const string Dialog = "DialogManager/_dialogList/[0]/_data/CenterButtonCallBack/m_target";
        private const string Record = Dialog + "/_onCloseDialogWithCharaSwitchButton/m_target/getIndexedSetupParam/m_target/buttonInfo/TrainedChara";
        private readonly string directory = AppDomain.CurrentDomain.BaseDirectory;
        private int pid, disabledPid;
        private string token;
        private DateTime sent, next;
        private CareerSnapshot cached;
        internal NativeMenuSnapshot Menu { get; private set; }
        private bool retired;
        internal void Retire()
        {
            if (retired) return; retired = true; cached = null; Menu = null; token = null;
            try { File.Delete(Path.Combine(directory, "bridge-inspect-legacy.enabled")); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        private static int Value(string response, string query)
        {
            var match = Regex.Match(response, @"(?:^|\n)QUERY " + Regex.Escape(query) + @"\r?\nVALUE (-?\d+)(?:\r?\n|$)");
            int value; return match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : -1;
        }
        internal static CareerSnapshot Parse(string response)
        {
            response = (response ?? "").Replace("\r\n", "\n");
            if (!response.Contains("QUERY " + Dialog + "\nCLASS Gallop.DialogTrainedCharacterDetail\n") ||
                !response.Contains("FIELD _isDispClosing : Boolean = false\n") ||
                !response.Contains("FIELD _isDispClosed : Boolean = false\n")) return null;
            int selected = Value(response, Dialog + "/_headerTitle/_innerOwn/_trainedCharaId");
            int id = Value(response, Record + "/@get_Id"), card = Value(response, Record + "/@get_CardId");
            int icon = Value(response, Dialog + "/_headerTitle/_headerBase/_characterButton/Info/Id");
            int score = Value(response, Record + "/@get_RankScore"), scenario = Value(response, Record + "/@get_ScenarioId");
            int count = Value(response, Record + "/AcquiredSkillArray/@get_Length");
            if (id <= 0 || id != selected || card != icon || card < 100000 || card >= 200000 || score <= 0 || score > 999999 || scenario < 0 || scenario > 1000 || count < 0 || count > 512) return null;
            return new CareerSnapshot { Card = card, Scenario = scenario, Results = true, Legacy = true, EvaluationScore = score, SkillCount = count };
        }
        internal CareerSnapshot Read(int gamePid)
        {
            if (retired || disabledPid == gamePid || !File.Exists(Path.Combine(directory, "bridge-inspect-legacy.enabled"))) { cached = null; Menu = null; return null; }
            try
            {
                if (pid != gamePid) { pid = gamePid; token = null; cached = null; Menu = null; next = DateTime.MinValue; }
                DateTime now = DateTime.UtcNow;
                string path = Path.Combine(directory, "bridge-inspect.response");
                if (token != null && File.Exists(path) && new FileInfo(path).Length <= 262144)
                {
                    string response = File.ReadAllText(path, Encoding.UTF8).Replace("\r\n", "\n");
                    if (response.Contains("QUERY " + token + "\n") && File.GetLastWriteTimeUtc(path) >= sent.AddSeconds(-1))
                    { cached = Parse(response); Menu = NativeMenuSnapshot.ParseInspection(response); token = null; }
                }
                if (token != null && now - sent > TimeSpan.FromSeconds(5)) { disabledPid = gamePid; cached = null; Menu = null; return null; }
                if (token == null && now >= next)
                {
                    token = "SceneManager/__presence_" + pid + "_" + Guid.NewGuid().ToString("N");
                    string queries = "score=SceneManager/_currentViewController/_sequence/_rankScoreModel/@get_RankScore\nskills=career/_acquiredSkillList/@get_Count\n" +
                        "DialogManager/_dialogList/[0]\n" + Dialog + "\n" +
                        Dialog + "/_headerTitle/_innerOwn/_trainedCharaId\n" + Dialog + "/_headerTitle/_headerBase/_characterButton/Info/Id\n";
                    foreach (string suffix in new[] { "/@get_Id", "/@get_CardId", "/@get_RankScore", "/@get_ScenarioId", "/AcquiredSkillArray/@get_Length" }) queries += Record + suffix + "\n";
                    string controller = "SceneManager/_currentViewController";
                    for (int depth = 0; depth < 4; depth++) { queries += controller + "\n"; controller += "/ChildCurrentController"; }
                    sent = now; next = now.AddSeconds(2);
                    File.WriteAllText(Path.Combine(directory, "bridge-inspect.request"), queries + token + "\n", Encoding.ASCII);
                }
                return cached;
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }
    }
}
