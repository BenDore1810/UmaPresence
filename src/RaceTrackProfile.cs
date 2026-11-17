using System;
using System.Collections.Generic;
using System.Linq;

namespace UmapyonPresence
{
    internal sealed class RaceCourseProfile
    {
        public int Distance { get; set; }
        public int Turn { get; set; }
    }
    internal sealed class RaceProfileCatalog
    {
        public Dictionary<string, RaceCourseProfile> Courses { get; set; }
        public Dictionary<string, int> Programs { get; set; }
    }
    // Preparation-only layout. Result/live and other tooltip formatters stay separate.
    internal static class RaceTrackProfile
    {
        private static RaceProfileCatalog catalog;
        private static string Label(int value, params string[] labels) { return value > 0 && value <= labels.Length ? labels[value - 1] : null; }
        private static string Category(int distance)
        {
            if (distance < 1000 || distance > 4000 || distance % 100 != 0) return null;
            return distance <= 1400 ? "Sprint" : distance <= 1800 ? "Mile" : distance <= 2400 ? "Medium" : "Long";
        }
        internal static string Tooltip(RaceSnapshot race)
        {
            if (catalog == null) catalog = SongCatalog.Resource<RaceProfileCatalog>("race-profiles.json");
            int course = race.Course;
            if (!catalog.Courses.ContainsKey(course.ToString())) {
                string prefix = race.ProgramType == 1 ? "p:" : race.ProgramType == 2 ? "i:" : race.ProgramType == 3 ? "r:" : null;
                int resolved;
                if (prefix != null && catalog.Programs.TryGetValue(prefix + race.Program, out resolved)) course = resolved;
            }
            RaceCourseProfile profile;
            catalog.Courses.TryGetValue(course.ToString(), out profile);
            // A conflicting live distance must not acquire another course's turn label.
            bool conflict = profile != null && race.Distance > 0 && profile.Distance != race.Distance;
            if (conflict) profile = null;
            int distance = race.Distance > 0 ? race.Distance : profile == null ? 0 : profile.Distance;
            string courseLabel = conflict ? distance + " m" : profile != null ? RaceCatalog.CourseLabel(course, race.Program, distance, race.ProgramType) : RaceCatalog.CourseLabel(race.Course, race.Program, distance, race.ProgramType);
            int qualifier = courseLabel.IndexOf(" (");
            if (qualifier >= 0) courseLabel = courseLabel.Substring(0, qualifier);
            var parts = courseLabel.Replace(" m", "m").Split(new[] { " • " }, StringSplitOptions.RemoveEmptyEntries).ToList();
            string category = Category(distance);
            if (category != null && parts.Count > 0 && parts[parts.Count - 1] == distance + "m") parts[parts.Count - 1] += " (" + category + ")";
            if (profile != null) {
                string turn = profile.Turn == 1 ? "Right turn" : profile.Turn == 2 ? "Left turn" : profile.Turn == 4 ? "Straight" : null;
                if (turn != null) parts.Add(turn);
            }
            string season = Label(race.Season, "Spring", "Summer", "Autumn", "Winter");
            if (season != null) parts.Add(season);
            // Keep the compact weather/going pair together without field labels.
            var conditions = new List<string>();
            string weather = Label(race.Weather, "Sunny", "Cloudy", "Rainy", "Snowy");
            string ground = Label(race.GroundCondition, "Firm", "Good", "Soft", "Heavy");
            if (weather != null) conditions.Add(weather);
            if (ground != null) conditions.Add(ground);
            string track = string.Join(" - ", parts);
            return string.Join(" - ", new[] { track, string.Join(" - ", conditions).Replace(' ', '\u00a0') }.Where(row => row.Length > 0));
        }
    }
}
