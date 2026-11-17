using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace UmapyonPresence
{
    internal static class ReleaseSelfCheck
    {
        // Offline package check: no game discovery, loader writes or Discord connection.
        internal static int Run()
        {
            string root = AppDomain.CurrentDomain.BaseDirectory;
            try {
                if (!ReleasePolicy.PublicRelease) throw new InvalidDataException("Expected a public build.");
                var settings = Settings.Load();
                if (settings.WelcomeDismissed) throw new InvalidDataException("Fresh package welcome is already dismissed.");
                bool local = File.Exists(Path.Combine(root, WelcomeDialog.LocalArtworkMarker));
                if (local != ReleasePolicy.PisaDev || local != ReleasePolicy.ExperimentalAudio)
                    throw new InvalidDataException("Package audio policy does not match its distribution.");
                if (settings.ApplicationId != "1563519446503522344" || settings.GameDirectory != "" || settings.ManualCharacter != "" || settings.ManualCharacterId != "" || settings.ConcertOutfitPortraits.Count != 0 || settings.CareerPortraitAssets.Count != 0 || settings.GameIntegrationMode != "Standalone" || settings.ConcertAudioMode != "Original" || settings.ConcertAllowDuplicates || settings.StandaloneSeekBarMode != "Paused") throw new InvalidDataException("Fresh public settings check failed.");
                if (local ? settings.ConcertPreferOriginalCover || settings.ConcertArtwork.Any(pair => !int.TryParse(pair.Key, out int id) || id <= 0 || !DataMaintenance.ValidAsset(pair.Value))
                    : !settings.ConcertPreferOriginalCover || settings.ConcertArtwork.Count != 0)
                    throw new InvalidDataException("Fresh package cover settings check failed.");
                var hashes = Json.Decode<Dictionary<string, string>>(File.ReadAllText(Path.Combine(root, "checksums.json")));
                foreach (var entry in hashes) {
                    string path = Path.GetFullPath(Path.Combine(root, entry.Key));
                    if (!path.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || entry.Key.Contains(':') || entry.Key.Split('/', '\\').Any(part => part == "..") || IntegrationProfiles.Hash(path) != entry.Value) throw new InvalidDataException("Package checksum check failed.");
                }
                if (CharacterCatalog.Entries.Count == 0 || SongCatalog.Find(1001) == null) throw new InvalidDataException("Embedded catalogs unavailable.");
                File.WriteAllText(Path.Combine(root, "release-smoke.json"), Json.Encode(new { version = ReleasePolicy.Version, status = "passed", scope = "Offline extracted-package checks only; no game launch or installation", filesVerified = hashes.Count }), new UTF8Encoding(false));
                return 0;
            } catch (Exception) {
                File.WriteAllText(Path.Combine(root, "release-smoke.json"), Json.Encode(new { version = ReleasePolicy.Version, status = "failed", scope = "Offline extracted-package validation" }), new UTF8Encoding(false)); return 1;
            }
        }
    }
}
