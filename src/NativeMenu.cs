using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace UmapyonPresence
{
    internal sealed class NativeMenuSnapshot
    {
        internal const string ActivitySource = "Native menu bridge";
        public string Root, View;
        // Controller names were enumerated from the installed game's metadata.
        // Unrecognized controllers retain a neutral native menu presence.
        private static readonly Dictionary<string, string[]> Labels = new Dictionary<string, string[]> {
            { "HomeViewController", new[] { "Main Menu", "Home" } },
            { "TrainedCharaListViewController", new[] { "Veterans", "Browsing trained Umamusume" } },
            { "CharacterCardHaveListViewController", new[] { "Umamusume", "Managing characters" } },
            { "CharacterCardCatalogViewController", new[] { "Umamusume", "Character catalog" } },
            { "SupportCardListViewController", new[] { "Support Cards", "Managing support cards" } },
            { "SupportCardDeckEditViewController", new[] { "Support Cards", "Editing a support deck" } },
            { "SupportCardWaitingRoomViewController", new[] { "Support Cards", "Waiting room" } },
            { "SingleModeStartViewController", new[] { "Career mode", "Preparing a career" } },
            { "SingleModeMainViewController", new[] { "Career mode", "Training" } },
            { "SingleModeMonthStartViewController", new[] { "Career mode", "Training" } },
            { "SingleModeSkillLearningViewController", new[] { "Career mode", "Learning skills" } },
            { "SingleModeResultViewController", new[] { "Career results", "Reviewing a completed career" } },
            { "ChampionsViewController", new[] { "Champions Meeting", "Event menu" } },
            { "ChampionsLobbyViewController", new[] { "Champions Meeting", "Lobby" } },
            { "HeroesTopViewController", new[] { "League of Heroes", "Event menu" } },
            { "HeroesHubViewController", new[] { "League of Heroes", "Event menu" } },
            { "UltimateRaceViewController", new[] { "Master Challenge", "Choosing a challenge" } },
            { "PracticeRaceHubViewController", new[] { "Practice Races", "Race menu" } },
            { "PracticeRaceTopViewController", new[] { "Practice Races", "Race menu" } },
            { "TeamStadiumViewController", new[] { "Team Stadium", "Race menu" } },
            { "TeamStadiumDeckHubViewController", new[] { "Team Stadium", "Managing teams" } },
            { "LiveTheaterViewController", new[] { "Live Theater", "Choosing a concert" } },
            { "StoryViewController", new[] { "Story", "Story mode" } },
            { "StoryEventHubViewController", new[] { "Story Event", "Event menu" } },
            { "StoryEventTopViewController", new[] { "Story Event", "Event menu" } },
            { "GachaMainViewController", new[] { "Scout", "Scout menu" } },
            { "GachaResultViewController", new[] { "Scout", "Viewing scout results" } },
            { "MissionViewController", new[] { "Missions", "Checking missions" } },
            { "DirectoryViewController", new[] { "Directory", "Browsing records" } }
        };
        internal ActivityState ToState()
        {
            string[] label;
            return new ActivityState { Kind = "Menu", Details = Labels.TryGetValue(View ?? "", out label) ? label[0] : "Playing Umamusume", State = label == null ? "Browsing menus" : label[1], Source = ActivitySource, StrongEvidence = true };
        }
        internal static NativeMenuSnapshot Parse(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 200 || BitConverter.ToUInt32(bytes, 0) != 0x4d454e31 || BitConverter.ToInt32(bytes, 4) != 1) return null;
            string root = Encoding.ASCII.GetString(bytes, 8, 96).Split('\0')[0], view = Encoding.ASCII.GetString(bytes, 104, 96).Split('\0')[0];
            return Valid(root) && Valid(view) ? new NativeMenuSnapshot { Root = root, View = view } : null;
        }
        private static bool Valid(string name) { return name.Length > 0 && name.Length < 96 && Regex.IsMatch(name, @"^[A-Za-z0-9_`]+$"); }
        internal static NativeMenuSnapshot ParseInspection(string response)
        {
            response = (response ?? "").Replace("\r\n", "\n");
            const string root = "SceneManager/_currentViewController";
            string first = null, last = null;
            for (int depth = 0; depth < 4; depth++)
            {
                string path = root; for (int j = 0; j < depth; j++) path += "/ChildCurrentController";
                var match = Regex.Match(response, @"(?:^|\n)QUERY " + Regex.Escape(path) + @"\nCLASS Gallop\.([A-Za-z0-9_`]+)\n");
                if (!match.Success) break;
                if (first == null) first = match.Groups[1].Value;
                last = match.Groups[1].Value;
            }
            return Valid(first ?? "") && Valid(last ?? "") && last != "HomeHubViewController" ? new NativeMenuSnapshot { Root = first, View = last } : null;
        }
    }
}
