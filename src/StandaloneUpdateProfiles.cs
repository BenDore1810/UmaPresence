using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace UmapyonPresence
{
    internal sealed class StandaloneInstalledRelease
    {
        public long Sequence { get; set; }
        public string Version { get; set; }
        public string UnitySha256 { get; set; }
        public string AssemblySha256 { get; set; }
    }

    internal sealed class StandaloneUpdateBackup
    {
        public string DirectoryName { get; set; }
        public Dictionary<string, string> Files { get; set; }
        public StandaloneInstalledRelease Release { get; set; }
    }

    internal sealed class StandaloneUpdateJournal
    {
        public bool Restoring { get; set; }
        public Dictionary<string, string> Files { get; set; }
        public StandaloneInstalledRelease Release { get; set; }
    }

    internal static partial class IntegrationProfiles
    {
        private const string UpdateStaging = ".compatibility-stage";
        private const string UpdateBackup = ".compatibility-backup";
        private const string UpdateSwap = ".compatibility-swap";
        private static readonly string[] InstalledBundleFiles = StandaloneUpdates.BundleFiles.Concat(new[] { "apphelp.dll" }).ToArray();
        private static readonly object ObservationLock = new object();
        private static string observingGame;
        private static int observingPid;
        private static long observedUpdate;
        private static bool verifiedUpdate;
        private static readonly object RestoreCheckLock = new object();
        private static string restoreCheckKey;
        private static Task<bool> restoreCheck;

        private static bool ValidRelease(StandaloneInstalledRelease release, long limit)
        {
            return release == null || (release.Sequence > 0 && release.Sequence <= limit
                && StandaloneUpdates.ValidVersion(release.Version) && StandaloneUpdates.ValidHash(release.UnitySha256)
                && StandaloneUpdates.ValidHash(release.AssemblySha256));
        }

        private static bool ValidUpdateFiles(Dictionary<string, string> files)
        {
            return files != null && files.Count == InstalledBundleFiles.Length
                && InstalledBundleFiles.All(name => files.ContainsKey(name) && StandaloneUpdates.ValidHash(files[name]));
        }

        private static void ValidateStandaloneUpdate(IntegrationJournal journal)
        {
            var update = journal.StandaloneUpdate;
            var backup = journal.StandaloneBackup;
            long sequence = journal.StandaloneUpdateSequence;
            if (sequence < 0 || !ValidRelease(journal.StandaloneRelease, sequence)
                || (journal.Step == "UpdatingStandalone") != (update != null)
                || (backup != null && ((backup.DirectoryName != UpdateBackup && backup.DirectoryName != UpdateSwap)
                    || !ValidUpdateFiles(backup.Files) || !ValidRelease(backup.Release, sequence))))
                throw new IOException("The Standalone update record is invalid. No loader files were changed.");
            if (update == null) return;
            if (journal.Target != null || !ValidUpdateFiles(journal.StandaloneFiles) || !ValidUpdateFiles(update.Files)
                || (update.Restoring && (backup == null || !EqualFiles(update.Files, backup.Files)
                    || !SameRelease(update.Release, backup.Release)))
                || (!update.Restoring && (backup != null || update.Release == null || update.Release.Sequence <= sequence
                    || !ValidRelease(update.Release, long.MaxValue))))
                throw new IOException("The pending Standalone update record is invalid. No loader files were changed.");
        }

        private static bool SameRelease(StandaloneInstalledRelease first, StandaloneInstalledRelease second)
        {
            return first == null || second == null ? first == second : first.Sequence == second.Sequence
                && first.Version == second.Version && first.UnitySha256 == second.UnitySha256 && first.AssemblySha256 == second.AssemblySha256;
        }

        private static bool EqualFiles(Dictionary<string, string> first, Dictionary<string, string> second)
            => first.Count == second.Count && first.All(pair => second.TryGetValue(pair.Key, out string hash) && hash == pair.Value);

        private static string UpdateDirectory(string game, string name) => Contained(game, "umapresence\\profiles\\" + name);

        private static string InstalledStandalone(string game, IntegrationJournal journal)
            => journal.Configured == "Standalone" ? Contained(game, "umamusume.exe.local") : UpdateDirectory(game, "standalone");

        internal static bool CanUseCurrentStandalone(string game)
        {
            var journal = Read(game);
            if (journal != null && journal.Step != "Idle") throw new IOException("Finish the interrupted game integration operation first.");
            string directory = journal == null ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "standalone") : InstalledStandalone(game, journal);
            if (journal != null) Verify(directory, journal.StandaloneFiles);
            string profile = Path.Combine(directory, "game-profile.json");
            return File.Exists(profile) && StandaloneGameCompatibility.Check(game, profile) != StandaloneGameSupport.Unsupported;
        }

        internal static long StandaloneUpdateSequence(string game) => Read(game)?.StandaloneUpdateSequence ?? 0;

        private static void VerifyUpdateGame(string game, StandaloneInstalledRelease release)
        {
            if (Hash(Contained(game, "UnityPlayer.dll")) != release.UnitySha256 || Hash(Contained(game, "GameAssembly.dll")) != release.AssemblySha256)
                throw new IOException("The game changed after this update was checked. Check compatibility again.");
        }

        internal static void InstallStandaloneUpdate(string game, StandaloneFeedEntry entry, Dictionary<string, byte[]> files)
        {
            using (Acquire(game)) {
                var journal = Read(game);
                if (journal != null && journal.Step != "Idle") throw new IOException("Finish the interrupted game integration operation first.");
                if (entry.Sequence <= (journal?.StandaloneUpdateSequence ?? 0)) throw new IOException("This Standalone update was already installed.");
                var release = new StandaloneInstalledRelease { Sequence = entry.Sequence, Version = entry.Version, UnitySha256 = entry.UnitySha256, AssemblySha256 = entry.AssemblySha256 };
                VerifyUpdateGame(game, release);
                if (journal == null) {
                    PrepareFirstStandaloneUpdate(game, entry, files, release);
                    return;
                }
                if (!ValidUpdateFiles(journal.StandaloneFiles)) throw new IOException("The installed Standalone profile has an unexpected file list.");
                string target = InstalledStandalone(game, journal);
                Verify(target, journal.StandaloneFiles);
                string incoming = UpdateDirectory(game, UpdateStaging);
                if (Directory.Exists(incoming)) throw new IOException("There is an unregistered Standalone update folder. No loader files were overwritten.");
                RemoveStandaloneUpdateBackup(game, journal);
                if (Directory.Exists(UpdateDirectory(game, UpdateBackup)) || Directory.Exists(UpdateDirectory(game, UpdateSwap)))
                    throw new IOException("There is an unregistered Standalone backup. No loader files were overwritten.");
                var updated = new Dictionary<string, string>(entry.Files) { { "apphelp.dll", journal.StandaloneFiles["apphelp.dll"] } };
                var update = new StandaloneUpdateJournal { Files = updated, Release = release };
                BeginStandaloneUpdate(game, journal, update);
                Directory.CreateDirectory(incoming);
                foreach (var file in files) File.WriteAllBytes(Path.Combine(incoming, file.Key), file.Value);
                File.Copy(Path.Combine(target, "apphelp.dll"), Path.Combine(incoming, "apphelp.dll"));
                Verify(incoming, updated);
                VerifyUpdateGame(game, release);
                CompleteStandaloneUpdate(game, journal, incoming, UpdateBackup);
            }
        }

        private static void PrepareFirstStandaloneUpdate(string game, StandaloneFeedEntry entry, Dictionary<string, byte[]> files, StandaloneInstalledRelease release)
        {
            var bundled = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "standalone", "loader-provenance.json");
            var provenance = Json.Decode<Dictionary<string, string>>(File.ReadAllText(bundled));
            provenance["standaloneSha256"] = entry.Files["UnityPlayer.dll"];
            provenance["bridgeSha256"] = entry.Files["umapyon_bridge.dll"];
            string temporary = Path.Combine(Path.GetTempPath(), "UmaPresence-update-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporary);
            try {
                foreach (var file in files) File.WriteAllBytes(Path.Combine(temporary, file.Key), file.Value);
                File.WriteAllText(Path.Combine(temporary, "loader-provenance.json"), Json.Encode(provenance));
                VerifyUpdateGame(game, release);
                PrepareLocked(new Settings { GameDirectory = game }, temporary, release);
                // Hachimi or stock is still the selected loader. The update is now ready for the normal switch action.
            }
            finally {
                foreach (string name in StandaloneUpdates.BundleFiles.Concat(new[] { "loader-provenance.json" })) File.Delete(Path.Combine(temporary, name));
                Directory.Delete(temporary);
            }
        }

        private static void BeginStandaloneUpdate(string game, IntegrationJournal journal, StandaloneUpdateJournal update)
        {
            journal.Step = "UpdatingStandalone";
            journal.StandaloneUpdate = update;
            Write(game, journal);
            Boundary("UpdateRecorded");
        }

        private static void CompleteStandaloneUpdate(string game, IntegrationJournal journal, string incoming, string outgoingName)
        {
            var update = journal.StandaloneUpdate;
            string target = InstalledStandalone(game, journal), outgoing = UpdateDirectory(game, outgoingName);
            RequireClosed(game);
            Verify(target, journal.StandaloneFiles);
            Verify(incoming, update.Files);
            Directory.Move(target, outgoing);
            Boundary("UpdateOldMoved");
            Directory.Move(incoming, target);
            Boundary("UpdateNewMoved");
            journal.StandaloneBackup = new StandaloneUpdateBackup { DirectoryName = outgoingName, Files = journal.StandaloneFiles, Release = journal.StandaloneRelease };
            journal.StandaloneFiles = update.Files;
            journal.StandaloneRelease = update.Release;
            journal.StandaloneUpdateSequence = Math.Max(journal.StandaloneUpdateSequence, update.Release?.Sequence ?? 0);
            journal.StandaloneUpdate = null;
            journal.Step = "Idle";
            Write(game, journal);
        }

        private static void RecoverStandaloneUpdate(string game, IntegrationJournal journal)
        {
            RequireClosed(game);
            var update = journal.StandaloneUpdate;
            string target = InstalledStandalone(game, journal);
            string incoming = UpdateDirectory(game, update.Restoring ? journal.StandaloneBackup.DirectoryName : UpdateStaging);
            string outgoing = UpdateDirectory(game, update.Restoring ? OtherBackup(journal.StandaloneBackup.DirectoryName) : UpdateBackup);
            if (Directory.Exists(outgoing)) {
                Verify(outgoing, journal.StandaloneFiles);
                if (Directory.Exists(target)) {
                    if (Directory.Exists(incoming)) throw new IOException("Unexpected files blocked recovery of the Standalone update.");
                    Verify(target, update.Files);
                    Directory.Move(target, incoming);
                }
                Directory.Move(outgoing, target);
            }
            Verify(target, journal.StandaloneFiles);
            if (update.Restoring) Verify(incoming, update.Files);
            else DeleteUpdateDirectory(incoming, update.Files, false);
            journal.StandaloneUpdate = null;
            journal.Step = "Idle";
            Write(game, journal);
        }

        private static string OtherBackup(string directoryName) => directoryName == UpdateBackup ? UpdateSwap : UpdateBackup;

        private static bool CanRestoreUpdate(IntegrationJournal journal)
            => journal?.Step == "Idle" && journal.StandaloneRelease != null && journal.StandaloneBackup != null
                && (journal.StandaloneBackup.Release?.Sequence ?? 0) < journal.StandaloneRelease.Sequence;

        internal static bool CanRestoreStandaloneUpdate(string game)
        {
            try {
                if (!GameInstallationDiscovery.Valid(game)) return false;
                game = Path.GetFullPath(game);
                var journal = Read(game);
                if (!CanRestoreUpdate(journal)) return false;
                string profile = Path.Combine(UpdateDirectory(game, journal.StandaloneBackup.DirectoryName), "game-profile.json");
                string key = string.Join("|", new[] { game, profile, journal.StandaloneBackup.Files["game-profile.json"],
                    FileStamp(profile), FileStamp(Path.Combine(game, "UnityPlayer.dll")), FileStamp(Path.Combine(game, "GameAssembly.dll")) });
                lock (RestoreCheckLock) {
                    if (restoreCheckKey == key && restoreCheck != null)
                        return restoreCheck.Status == TaskStatus.RanToCompletion && restoreCheck.Result;
                    if (restoreCheck != null && !restoreCheck.IsCompleted) return false;
                    string directory = game;
                    restoreCheckKey = key;
                    restoreCheck = Task.Run(() => StandaloneGameCompatibility.Check(directory, profile) != StandaloneGameSupport.Unsupported);
                    return false;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is InvalidOperationException) { return false; }
        }

        private static string FileStamp(string path)
        {
            var info = new FileInfo(path);
            return info.Length + ":" + info.LastWriteTimeUtc.Ticks;
        }

        internal static void RestoreStandaloneUpdate(string directory)
        {
            string game = Path.GetFullPath(directory);
            using (Acquire(game)) {
                var journal = Read(game);
                if (!CanRestoreUpdate(journal)) throw new IOException("A previous Standalone build is unavailable.");
                var backup = journal.StandaloneBackup;
                string incoming = UpdateDirectory(game, backup.DirectoryName);
                Verify(incoming, backup.Files);
                RequireMatchingBuild(game, Path.Combine(incoming, "game-profile.json"));
                if (Directory.Exists(UpdateDirectory(game, OtherBackup(backup.DirectoryName))))
                    throw new IOException("An unexpected Standalone backup exists. Nothing was changed.");
                var update = new StandaloneUpdateJournal { Restoring = true, Files = backup.Files, Release = backup.Release };
                BeginStandaloneUpdate(game, journal, update);
                CompleteStandaloneUpdate(game, journal, incoming, OtherBackup(backup.DirectoryName));
            }
        }

        private static void RemoveStandaloneUpdateBackup(string game, IntegrationJournal journal)
        {
            var backup = journal.StandaloneBackup;
            if (backup == null) return;
            DeleteUpdateDirectory(UpdateDirectory(game, backup.DirectoryName), backup.Files, true);
            journal.StandaloneBackup = null;
        }

        private static void DeleteUpdateDirectory(string directory, Dictionary<string, string> expected, bool checkHashes)
        {
            if (!Directory.Exists(directory)) return;
            var files = Files(directory);
            if (Directory.GetDirectories(directory).Length != 0 || files.Any(pair => !expected.TryGetValue(pair.Key, out string hash) || (checkHashes && hash != pair.Value)))
                throw new IOException("Unexpected files blocked Standalone cleanup. The remaining files were left in place.");
            foreach (var name in files.Keys) File.Delete(Path.Combine(directory, name));
            Directory.Delete(directory);
        }

        internal static string StandaloneUpdateStatus(string directory, int pid)
        {
            if (!GameInstallationDiscovery.Valid(directory)) return "";
            try {
                string game = Path.GetFullPath(directory);
                var journal = Read(game);
                if (journal?.Step == "UpdatingStandalone") return "A Standalone update was interrupted. Close the game and use Recover interrupted switch.";
                if (journal?.StandaloneRelease == null) return "";
                string version = journal.StandaloneRelease.Version;
                if (journal.Configured != "Standalone") return "Standalone update " + version + " is ready. Select Standalone and apply the switch when you want to use it.";
                var runtime = StandaloneRuntime.Read(pid);
                if (runtime != null && (runtime.State == 2 || runtime.State == 5 || runtime.OwnershipConflict))
                    return "Standalone update " + version + " did not start. Check compatibility or return to stock DMM.";
                lock (ObservationLock)
                    if (verifiedUpdate && pid == observingPid && observedUpdate == journal.StandaloneRelease.Sequence
                        && string.Equals(game, observingGame, StringComparison.OrdinalIgnoreCase))
                        return "Standalone update " + version + " is working.";
                return "Standalone update " + version + " is installed. Launch through DMM and wait for the bridge to send activity.";
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is InvalidOperationException) { return ""; }
        }

        internal static void ObserveStandaloneUpdate(string directory, int pid)
        {
            string game = Path.GetFullPath(directory);
            try {
                var journal = Read(game);
                if (journal?.StandaloneRelease == null || journal.Configured != "Standalone" || journal.Step != "Idle") return;
                lock (ObservationLock) {
                    if (observingPid == pid && observedUpdate == journal.StandaloneRelease.Sequence && string.Equals(game, observingGame, StringComparison.OrdinalIgnoreCase)) return;
                    var runtime = StandaloneRuntime.Read(pid);
                    if (runtime == null || !runtime.Ready || (runtime.Capabilities & RuntimeCapability.Telemetry) == 0
                        || !string.Equals(GameInstallationDiscovery.ProcessPath(pid), Path.Combine(game, "umamusume.exe"), StringComparison.OrdinalIgnoreCase)) return;
                    observingGame = game; observingPid = pid; observedUpdate = journal.StandaloneRelease.Sequence; verifiedUpdate = false;
                }
                var release = journal.StandaloneRelease;
                Task.Run(() => {
                    try {
                        VerifyUpdateGame(game, release);
                        Verify(InstalledStandalone(game, journal), journal.StandaloneFiles);
                        lock (ObservationLock)
                            if (observingPid == pid && observedUpdate == release.Sequence && string.Equals(game, observingGame, StringComparison.OrdinalIgnoreCase)) verifiedUpdate = true;
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is InvalidOperationException) { }
                });
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is InvalidOperationException) { }
        }
    }
}
