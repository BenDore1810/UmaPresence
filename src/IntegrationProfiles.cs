using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace UmapyonPresence
{
    internal sealed class IntegrationJournal
    {
        public int Version { get; set; } = 1;
        public string GameDirectory { get; set; }
        public string Configured { get; set; } = "Hachimi";
        public string Target { get; set; }
        public string Step { get; set; } = "Idle";
        public string Staging { get; set; }
        public string OriginalForwardingMode { get; set; }
        public long OriginalForwardingDiskBytes { get; set; }
        public string StartingProfile { get; set; } = "Unknown";
        public Dictionary<string, string> HachimiFiles { get; set; } = new Dictionary<string, string>();
        public Dictionary<string, string> StandaloneFiles { get; set; } = new Dictionary<string, string>();
        public bool KeepStockDmm { get; set; }
        public bool? RemoveBridgeRegistrations { get; set; }
        public bool FullRemoval { get; set; }
        public StandaloneUpdateJournal StandaloneUpdate { get; set; }
        public StandaloneInstalledRelease StandaloneRelease { get; set; }
        public StandaloneUpdateBackup StandaloneBackup { get; set; }
        public long StandaloneUpdateSequence { get; set; }
    }
    internal sealed class IntegrationState
    {
        internal string Configured { get; set; }
        internal string Step { get; set; }
    }
    internal static partial class IntegrationProfiles
    {
        // Tests interrupt durable transaction boundaries; never enabled by settings.
        internal static Action<string> Fault;
        private static void Boundary(string name) { if (Fault != null) Fault(name); }
        internal static string Hash(string path) { using (var input = File.OpenRead(path)) using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant(); }
        private static string Root(string game) { if (!GameInstallationDiscovery.Valid(game)) throw new IOException("Choose a verified JP DMM installation first."); return Contained(game, "umapresence"); }
        private static string Contained(string game, string relative)
        {
            string root = Path.GetFullPath(game).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, path = Path.GetFullPath(Path.Combine(root, relative));
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new IOException("Integration path escaped the selected game directory.");
            for (string check = path; check != null && check.StartsWith(root, StringComparison.OrdinalIgnoreCase); check = Path.GetDirectoryName(check))
                if ((Directory.Exists(check) || File.Exists(check)) && (File.GetAttributes(check) & FileAttributes.ReparsePoint) != 0) throw new IOException("Refusing to move an integration path through a reparse point.");
            return path;
        }
        private static string JournalPath(string game) { Root(game); return Contained(game, "umapresence\\integration-journal.json"); }
        private static IntegrationJournal Read(string game)
        {
            string path = JournalPath(game); if (!File.Exists(path)) return null;
            var journal = Json.Decode<IntegrationJournal>(File.ReadAllText(path, Encoding.UTF8));
            if (journal == null || journal.Version != 1 || !string.Equals(journal.GameDirectory, Path.GetFullPath(game), StringComparison.OrdinalIgnoreCase) || (journal.Configured != "Hachimi" && journal.Configured != "Standalone" && journal.Configured != "Original")) throw new IOException("Integration journal is invalid; no loader changes made.");
            if (!new[] { "Idle", "Preparing", "Prepared", "SourceStored", "TargetActivated", "Removing", "UpdatingStandalone" }.Contains(journal.Step) || journal.HachimiFiles == null || journal.StandaloneFiles == null || journal.StandaloneFiles.Count == 0) throw new IOException("Incomplete integration journal; no loader changes made.");
            if (journal.Step == "Preparing") { if (journal.Staging == null || !System.Text.RegularExpressions.Regex.IsMatch(journal.Staging, @"^\.preparing-[a-f0-9]{32}$")) throw new IOException("Invalid staging identity."); }
            else if (journal.Step != "Idle" && journal.Step != "UpdatingStandalone" && !new[] { "Hachimi", "Standalone", "Original" }.Contains(journal.Target)) throw new IOException("Invalid transaction target.");
            foreach (var entry in journal.HachimiFiles.Concat(journal.StandaloneFiles)) {
                if (string.IsNullOrEmpty(entry.Key) || Path.IsPathRooted(entry.Key) || entry.Key.Split('\\', '/').Any(part => part == ".." || part == "." || part.Contains(':')) || entry.Value == null || !System.Text.RegularExpressions.Regex.IsMatch(entry.Value, "^[a-f0-9]{64}$")) throw new IOException("Invalid profile hash manifest.");
            }
            ValidateStandaloneUpdate(journal);
            return journal;
        }
        private static void Write(string game, IntegrationJournal journal)
        {
            string directory = Root(game); Directory.CreateDirectory(directory); string path = JournalPath(game), temporary = Contained(game, "umapresence\\integration-journal.json.new");
            Contained(game, "umapresence\\integration-journal.json.previous");
            byte[] bytes = new UTF8Encoding(false).GetBytes(Json.Encode(journal));
            using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough)) { output.Write(bytes, 0, bytes.Length); output.Flush(true); }
            if (File.Exists(path)) File.Replace(temporary, path, path + ".previous"); else File.Move(temporary, path);
        }
        private static Dictionary<string, string> Files(string directory)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!Directory.Exists(directory)) return result;
            var pending = new Stack<string>(); pending.Push(directory);
            var files = new List<string>();
            while (pending.Count > 0) {
                string current = pending.Pop(); if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("Unexpected linked directory in loader profile.");
                files.AddRange(Directory.GetFiles(current)); foreach (string child in Directory.GetDirectories(current)) pending.Push(child);
            }
            foreach (string file in files) {
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new IOException("Unexpected link in loader profile.");
                string relative = file.Substring(directory.Length).TrimStart(Path.DirectorySeparatorChar);
                result[relative] = Hash(file);
            }
            return result;
        }
        private static void Verify(string directory, Dictionary<string, string> expected)
        {
            if (!MatchFiles(Files(directory), expected)) throw new IOException("Loader profile changed outside UmaPresence. Recheck it before switching; nothing was overwritten.");
        }
        private static bool MatchFiles(Dictionary<string, string> actual, Dictionary<string, string> expected) =>
            actual.Count == expected.Count && expected.All(pair => actual.TryGetValue(pair.Key, out string hash) && hash == pair.Value);
        private static void RequireClosed(string gameDirectory) { if (GameInstallationDiscovery.IsGameRunning(gameDirectory)) throw new InvalidOperationException("Close the game normally before applying the integration mode. Its executable is still running; no game process will be stopped."); }
        private static IDisposable Acquire(string game) { RequireClosed(game); Directory.CreateDirectory(Root(game)); return new FileStream(Contained(game, "umapresence\\integration.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        internal static string DetectStartingProfile(Settings settings, string binaryDirectory = null)
        {
            try {
                if (settings == null || !GameInstallationDiscovery.Valid(settings.GameDirectory)) return "Unknown";
                string game = Path.GetFullPath(settings.GameDirectory);
                var journal = Read(game);
                if (journal != null) {
                    if (journal.StartingProfile == "Original DMM" || journal.StartingProfile == "Hachimi Edge") return journal.StartingProfile;
                    // Version 1 journals created before StartingProfile was recorded.
                    return journal.HachimiFiles.Count > 0 ? "Hachimi Edge" : "Original DMM";
                }
                string binaries = binaryDirectory ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "standalone");
                string provenancePath = Path.Combine(binaries, "loader-provenance.json");
                string active = Contained(game, "umamusume.exe.local");
                if (Directory.Exists(active)) {
                    if (!File.Exists(provenancePath)) return "Unknown";
                    var provenance = Json.Decode<Dictionary<string, string>>(File.ReadAllText(provenancePath));
                    string unity = Path.Combine(active, "UnityPlayer.dll"), bridge = Path.Combine(active, "umapyon_bridge.dll");
                    if (File.Exists(unity) && File.Exists(bridge) && provenance.ContainsKey("standaloneSha256") && provenance.ContainsKey("bridgeSha256") && Hash(unity) == provenance["standaloneSha256"] && Hash(bridge) == provenance["bridgeSha256"])
                        return "Unknown";
                    string config = Path.Combine(game, "hachimi", "config.json");
                    if (File.Exists(config) && CanSnapshotHachimi(game, HashPair(active), provenance))
                        return "Hachimi Edge";
                    return "Unknown";
                }
                string profile = Path.Combine(binaries, "game-profile.json");
                if (!File.Exists(profile)) return "Unknown";
                RequireMatchingBuild(game, profile);
                return "Original DMM";
            } catch { return "Unknown"; }
        }
        internal static bool CanUninstall(Settings settings)
        {
            try { var journal = Read(settings.GameDirectory); return journal != null && (journal.Step == "Idle" || journal.Step == "Removing"); }
            catch { return false; }
        }
        internal static IntegrationState State(Settings settings)
        {
            if (!GameInstallationDiscovery.Valid(settings.GameDirectory)) return new IntegrationState { Configured = "Unknown", Step = "Idle" };
            var journal = Read(settings.GameDirectory);
            if (journal != null) return new IntegrationState { Configured = journal.Configured, Step = journal.Step };
            string configured = Directory.Exists(Contained(settings.GameDirectory, "umamusume.exe.local")) ? "Unmanaged" : "Original";
            return new IntegrationState { Configured = configured, Step = "Idle" };
        }
        internal static void InstallStandalone(Settings settings, string binaryDirectory = null, Action<Settings> save = null)
        {
            using (Acquire(settings.GameDirectory)) {
                if (Read(settings.GameDirectory) == null) PrepareLocked(settings, binaryDirectory);
                ApplyLocked(settings, "Standalone", binaryDirectory);
                settings.GameIntegrationMode = "Standalone";
                AppliedMessage(settings);
                (save ?? (value => value.Save()))(settings);
            }
        }
        private const string ManagedBridgePrefix = "umapyon_bridge_managed_";
        private static readonly object hachimiObservationLock = new object();
        private sealed class HachimiObservation
        {
            internal string Game;
            internal int Pid;
            internal long Started;
            internal ulong Heartbeat;
            internal Dictionary<string, string> Pair;
        }
        private static HachimiObservation hachimiObservation;
        private static readonly string[] hachimiPair = { "UnityPlayer.dll", "apphelp.dll" };

        internal static bool HasHachimiSession(Settings settings, int gamePid)
        {
            if (gamePid <= 0 || string.IsNullOrWhiteSpace(settings.GameDirectory)) return false;
            lock (hachimiObservationLock) {
                var observation = hachimiObservation;
                ulong now = ConcertReader.GetTickCount64();
                return observation?.Pair != null && observation.Pid == gamePid
                    && observation.Heartbeat <= now && now - observation.Heartbeat <= 10000
                    && string.Equals(observation.Game, Path.GetFullPath(settings.GameDirectory), StringComparison.OrdinalIgnoreCase);
            }
        }

        private static Dictionary<string, string> HashPair(string directory)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string name in hachimiPair)
                if (File.Exists(Path.Combine(directory, name))) result[name] = Hash(Path.Combine(directory, name));
            return result;
        }

        // Called only after a current bridge heartbeat carries native activity.
        internal static void ObserveHachimiSession(string gameDirectory, string executable, int gamePid, long gameStarted, ulong heartbeat)
        {
            if (gamePid <= 0 || string.IsNullOrWhiteSpace(gameDirectory)) return;
            string game = Path.GetFullPath(gameDirectory);
            if (!string.Equals(Path.Combine(game, "umamusume.exe"), executable, StringComparison.OrdinalIgnoreCase)) return;
            HachimiObservation observation;
            lock (hachimiObservationLock) {
                if (hachimiObservation?.Pid == gamePid && hachimiObservation.Started == gameStarted
                    && string.Equals(hachimiObservation.Game, game, StringComparison.OrdinalIgnoreCase)) {
                    hachimiObservation.Heartbeat = heartbeat;
                    return;
                }
                observation = new HachimiObservation { Game = game, Pid = gamePid, Started = gameStarted, Heartbeat = heartbeat };
                hachimiObservation = observation;
            }
            Task.Run(() => {
                try {
                    using (var process = Process.GetProcessById(gamePid)) {
                        if (new DateTimeOffset(process.StartTime.ToUniversalTime()).ToUnixTimeSeconds() != gameStarted) return;
                        string active = Contained(game, "umamusume.exe.local");
                        string plugins = Contained(game, "hachimi\\plugins");
                        var modules = process.Modules.Cast<ProcessModule>().Select(module => module.FileName).ToArray();
                        if (!hachimiPair.All(name => modules.Contains(Path.Combine(active, name), StringComparer.OrdinalIgnoreCase))) return;
                        if (!modules.Any(path => string.Equals(Path.GetDirectoryName(path), plugins, StringComparison.OrdinalIgnoreCase)
                            && Path.GetFileName(path).StartsWith("umapyon_bridge", StringComparison.OrdinalIgnoreCase))) return;
                        if (File.Exists(Path.Combine(active, "game-profile.json")) || process.HasExited) return;
                        foreach (string name in hachimiPair) Contained(game, "umamusume.exe.local\\" + name);
                        var pair = HashPair(active);
                        lock (hachimiObservationLock)
                            if (!process.HasExited && pair.Count == hachimiPair.Length) observation.Pair = pair;
                    }
                } catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is InvalidOperationException
                    || error is ArgumentException || error is System.ComponentModel.Win32Exception) { }
            });
        }

        private static bool CanSnapshotHachimi(string game, Dictionary<string, string> files, Dictionary<string, string> provenance)
        {
            if (!files.TryGetValue("UnityPlayer.dll", out string loader) || !files.TryGetValue("apphelp.dll", out string helper)) return false;
            if (provenance.TryGetValue("verifiedHachimiSha256", out string expectedLoader)
                && (provenance.TryGetValue("verifiedHachimiHelperSha256", out string expectedHelper) || provenance.TryGetValue("helperSha256", out expectedHelper))
                && loader == expectedLoader && helper == expectedHelper) return true;
            lock (hachimiObservationLock) {
                var observation = hachimiObservation;
                return observation?.Pair != null && string.Equals(observation.Game, game, StringComparison.OrdinalIgnoreCase)
                    && loader == observation.Pair["UnityPlayer.dll"] && helper == observation.Pair["apphelp.dll"];
            }
        }

        private static Dictionary<string, string> ReadProvenance(string binaryDirectory) =>
            Json.Decode<Dictionary<string, string>>(File.ReadAllText(Path.Combine(
                binaryDirectory ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "standalone"), "loader-provenance.json")));

        internal static void InstallHachimi(Settings settings, string binaryDirectory = null, Action<Settings> save = null)
        {
            string game = Path.GetFullPath(settings.GameDirectory);
            using (Acquire(game)) {
                string binaries = binaryDirectory ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "standalone");
                var provenance = Json.Decode<Dictionary<string, string>>(File.ReadAllText(Path.Combine(binaries, "loader-provenance.json")));
                string source = Path.Combine(binaries, "umapyon_bridge.dll"), hash = Hash(source);
                if (hash != provenance["bridgeSha256"]) throw new IOException("The packaged Hachimi bridge failed verification.");
                string active = Contained(game, "umamusume.exe.local");
                var journal = Read(game);
                if (journal != null && journal.Step != "Idle") throw new IOException("Recover the interrupted loader switch first.");
                bool switching = journal != null && journal.Configured != "Hachimi";
                string loader = switching ? Contained(game, "umapresence\\profiles\\hachimi\\UnityPlayer.dll") : Path.Combine(active, "UnityPlayer.dll");
                if (!File.Exists(loader)) throw new IOException("Install Hachimi Edge before selecting Hachimi mode.");
                if (!switching && (File.Exists(Path.Combine(active, "game-profile.json")) || Hash(loader) == provenance["standaloneSha256"])) throw new IOException("Standalone appears to be active. Recover the loader setup before configuring Hachimi.");
                string configPath = Contained(game, "hachimi\\config.json");
                var config = Json.Decode<Dictionary<string, object>>(File.ReadAllText(configPath, Encoding.UTF8));
                var libraries = HachimiLibraries(config);
                bool hasOldBridge = RemovePreviousBridgeRegistration(game, false);
                var old = ManagedBridges(game, libraries);
                string destination = Contained(game, "hachimi\\plugins\\" + ManagedBridgePrefix + hash + ".dll");
                if (File.Exists(destination) && Hash(destination) != hash) throw new IOException("The managed Hachimi bridge path is occupied by a different file.");
                if (switching) ApplyLocked(settings, "Hachimi", binaryDirectory);
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                if (!File.Exists(destination)) File.Copy(source, destination);
                if (Hash(destination) != hash) throw new IOException("Installed Hachimi bridge failed verification.");
                config["load_libraries"] = libraries.Cast<object>().Where(entry => !old.Contains(entry as string) && !(hasOldBridge && IsPreviousBridge(game, entry as string))).Concat(new object[] { destination }).ToArray();
                ReplaceHachimiConfig(configPath, config, false);
                foreach (string path in old.Where(path => !string.Equals(path, destination, StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase)) File.Delete(path);
                settings.GameIntegrationMode = "Hachimi";
                (save ?? (value => value.Save()))(settings);
            }
        }

        private static System.Collections.IList HachimiLibraries(Dictionary<string, object> config)
        {
            object value;
            if (config == null) throw new IOException("Hachimi configuration is invalid.");
            if (!config.TryGetValue("load_libraries", out value) || value == null) return new object[0];
            return value as System.Collections.IList ?? throw new IOException("Hachimi load_libraries is invalid.");
        }

        private static HashSet<string> ManagedBridges(string game, System.Collections.IList libraries)
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string entry in libraries.OfType<string>()) {
                string name = Path.GetFileName(entry);
                if (!System.Text.RegularExpressions.Regex.IsMatch(name, "^" + ManagedBridgePrefix + "[0-9a-f]{64}\\.dll$")) continue;
                string path = Contained(game, "hachimi\\plugins\\" + name);
                if (!string.Equals(entry, path, StringComparison.OrdinalIgnoreCase)) continue;
                if (File.Exists(path) && Hash(path) != name.Substring(ManagedBridgePrefix.Length, 64)) throw new IOException("The registered UmaPresence bridge has changed.");
                paths.Add(path);
            }
            return paths;
        }

        private static bool RemoveManagedBridges(string game, bool remove)
        {
            string path = Contained(game, "hachimi\\config.json");
            if (!File.Exists(path)) return false;
            var config = Json.Decode<Dictionary<string, object>>(File.ReadAllText(path, Encoding.UTF8));
            var libraries = HachimiLibraries(config);
            var bridges = ManagedBridges(game, libraries);
            if (remove && bridges.Count != 0) {
                config["load_libraries"] = libraries.Cast<object>().Where(entry => !bridges.Contains(entry as string)).ToArray();
                ReplaceHachimiConfig(path, config, false);
                foreach (string bridge in bridges) File.Delete(bridge);
            }
            return bridges.Count != 0;
        }
        private const string PreviousBridge = "hachimi\\plugins\\umapyon_bridge_career_v30.dll";
        private const string PreviousBridgeHash = "c2004f8e1f9dc1425250df1111404cc724670134f7c5f09236c525a34cfc15f5";
        private static bool IsPreviousBridge(string game, string path)
        {
            return string.Equals(path, Contained(game, PreviousBridge), StringComparison.OrdinalIgnoreCase) || string.Equals(path, PreviousBridge, StringComparison.OrdinalIgnoreCase);
        }

        private static bool RemovePreviousBridgeRegistration(string game, bool remove, bool keepBackup = true)
        {
            string configPath = Contained(game, "hachimi\\config.json");
            if (!File.Exists(configPath)) return false;
            var config = Json.Decode<Dictionary<string, object>>(File.ReadAllText(configPath, Encoding.UTF8));
            object raw;
            if (config == null || !config.TryGetValue("load_libraries", out raw) || raw == null) return false;
            var entries = raw as System.Collections.IList;
            if (entries == null) throw new IOException("Hachimi load_libraries is invalid.");
            string bridge = Contained(game, PreviousBridge);
            var retained = new List<object>();
            foreach (object entry in entries) {
                string path = entry as string;
                if (IsPreviousBridge(game, path)) continue;
                retained.Add(entry);
            }
            if (retained.Count == entries.Count) return false;
            if (!File.Exists(bridge) || Hash(bridge) != PreviousBridgeHash) throw new IOException("The registered UmaPresence bridge does not match the recorded copy.");
            if (!remove) return true;

            config["load_libraries"] = retained;
            ReplaceHachimiConfig(configPath, config, keepBackup);
            return true;
        }

        private static void ReplaceHachimiConfig(string configPath, Dictionary<string, object> config, bool keepBackup)
        {
            string backup = configPath + ".umapresence-" + Guid.NewGuid().ToString("N") + ".bak";
            string staging = backup + ".new";
            try {
                File.WriteAllText(staging, Json.Encode(config), new UTF8Encoding(false));
                File.Replace(staging, configPath, keepBackup ? backup : null);
            } finally { if (File.Exists(staging)) File.Delete(staging); }
        }

        internal static bool CanUninstallCurrent(Settings settings)
        {
            if (CanUninstall(settings)) return true;
            try { string game = Path.GetFullPath(settings.GameDirectory); return RemoveManagedBridges(game, false) || RemovePreviousBridgeRegistration(game, false); }
            catch { return false; }
        }

        internal static void UninstallCurrent(Settings settings, bool fullRemoval = false)
        {
            string game = Path.GetFullPath(settings.GameDirectory);
            using (Acquire(game)) UninstallLocked(settings, true, fullRemoval);
            CleanupRuntimeFiles(game);
        }
        private static void RemoveRegistrations(string game, bool fullRemoval)
        {
            RequireClosed(game);
            RemovePreviousBridgeRegistration(game, true, !fullRemoval);
            RemoveManagedBridges(game, true);
            if (fullRemoval) {
                string bridge = Contained(game, PreviousBridge);
                if (File.Exists(bridge) && Hash(bridge) == PreviousBridgeHash) File.Delete(bridge);
            }
        }
        private static void CleanupRuntimeFiles(string game)
        {
            string root = Root(game);
            string profiles = Contained(game, "umapresence\\profiles");
            if (Directory.Exists(profiles) && !Directory.EnumerateFileSystemEntries(profiles).Any()) Directory.Delete(profiles);
            foreach (string name in new[] { "integration.lock", "runtime-startup.log", "UnityPlayer_current.dll" }) {
                string path = Contained(game, "umapresence\\" + name);
                if (File.Exists(path)) File.Delete(path);
            }
            if (Directory.Exists(root) && !Directory.EnumerateFileSystemEntries(root).Any()) Directory.Delete(root);
        }
        internal static string Status(Settings settings, int pid)
        {
            string configured = "Unmanaged existing installation";
            try { var journal = Read(settings.GameDirectory); if (journal != null) configured = journal.Configured + (journal.Step == "Idle" ? "" : " — recovery required: " + journal.Step) + (journal.OriginalForwardingMode == null ? "" : "\r\nOriginal DLL forwarding: " + journal.OriginalForwardingMode + ", additional disk " + journal.OriginalForwardingDiskBytes.ToString("N0") + " bytes"); } catch (Exception ex) { configured = ex.Message; }
            var runtime = StandaloneRuntime.Read(pid);
            return "Starting profile: " + DetectStartingProfile(settings) + "\r\nRequested: " + settings.GameIntegrationMode + "\r\nConfigured: " + configured + "\r\nLoaded: " + (runtime == null ? (pid == 0 ? "Game not running" : "Independent runtime not detected; see bridge module diagnostics") : runtime.OwnershipConflict ? "Competing integration; standalone suspended — " + runtime.Detail : "Standalone, state " + runtime.State + " — " + runtime.Detail) + "\r\n" + (runtime == null ? "" : "Capabilities: " + runtime.Capabilities) + (pid > 0 && !configured.StartsWith(settings.GameIntegrationMode, StringComparison.Ordinal) ? "\r\nRequested mode is pending; close the game normally, then Apply." : "");
        }
        internal static string AppliedMessage(Settings settings)
        {
            string game = Path.GetFullPath(settings.GameDirectory); var journal = Read(game);
            if (journal == null || journal.Step != "Idle" || journal.Configured != settings.GameIntegrationMode) throw new IOException("The requested loader mode could not be verified. Check the integration status before launching.");
            Verify(Contained(game, "umamusume.exe.local"), journal.Configured == "Standalone" ? journal.StandaloneFiles : journal.HachimiFiles);
            return journal.Configured + " loader applied successfully.\r\n\r\nThe active loader files have been verified. Launch the game normally.\r\n\r\nThis confirms installation; runtime startup is checked after the game opens.";
        }
        internal static void Prepare(Settings settings, string binaryDirectory = null)
        { using (Acquire(settings.GameDirectory)) PrepareLocked(settings, binaryDirectory); }
        private static void RequireMatchingBuild(string game, string profilePath) => StandaloneGameCompatibility.Require(game, profilePath);
        private static void PrepareLocked(Settings settings, string binaryDirectory, StandaloneInstalledRelease release = null)
        {
            RequireClosed(settings.GameDirectory); string game = Path.GetFullPath(settings.GameDirectory); string management = Root(game);
            if (Read(game) != null) throw new InvalidOperationException("Profiles are already prepared. Use Apply requested mode.");
            string binaries = binaryDirectory ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "standalone");
            foreach (string name in new[] { "UnityPlayer.dll", "umapyon_bridge.dll", "game-profile.json", "loader-provenance.json" }) if (!File.Exists(Path.Combine(binaries, name))) throw new FileNotFoundException("Standalone build is incomplete: " + name);
            RequireMatchingBuild(game, Path.Combine(binaries, "game-profile.json"));
            string active = Contained(game, "umamusume.exe.local"), standalone = Contained(game, "umapresence\\profiles\\standalone");
            if (Directory.Exists(standalone)) throw new IOException("An unregistered standalone profile already exists. Nothing was overwritten.");
            var provenance = ReadProvenance(binaries);
            if (Hash(Path.Combine(binaries, "UnityPlayer.dll")) != provenance["standaloneSha256"] || Hash(Path.Combine(binaries, "umapyon_bridge.dll")) != provenance["bridgeSha256"]) throw new IOException("Standalone binaries do not match their build manifest.");
            bool hachimi = Directory.Exists(active);
            var hachimiFiles = Files(active);
            if (hachimi) {
                string config = Path.Combine(game, "hachimi", "config.json");
                var existing = File.Exists(config) ? Json.Decode<Dictionary<string, object>>(File.ReadAllText(config)) : null;
                if (existing == null || !CanSnapshotHachimi(game, hachimiFiles, provenance)) throw new IOException("This Hachimi loader has not been verified. Start the game with Hachimi and confirm UmaPresence receives activity before switching.");
            }
            string helper = ReleasePolicy.PublicRelease ? LoaderHelper.Require() : Path.Combine(binaries, "apphelp.dll");
            if (!ReleasePolicy.PublicRelease && !File.Exists(helper) && hachimi) helper = Path.Combine(active, "apphelp.dll");
            if (!File.Exists(helper) || Hash(helper) != provenance["helperSha256"]) throw new IOException("Verified DotLocal helper is unavailable.");
            string stagingName = ".preparing-" + Guid.NewGuid().ToString("N"), staging = Contained(game, "umapresence\\profiles\\" + stagingName); Directory.CreateDirectory(staging); bool recorded = false;
            try {
                foreach (string name in new[] { "UnityPlayer.dll", "umapyon_bridge.dll", "game-profile.json" }) File.Copy(Path.Combine(binaries, name), Path.Combine(staging, name), false);
                File.Copy(helper, Path.Combine(staging, "apphelp.dll"));
                var journal = new IntegrationJournal {
                    GameDirectory = game, Configured = hachimi ? "Hachimi" : "Original", StartingProfile = hachimi ? "Hachimi Edge" : "Original DMM",
                    HachimiFiles = hachimiFiles, StandaloneFiles = Files(staging), Step = "Preparing", Staging = stagingName,
                    StandaloneRelease = release, StandaloneUpdateSequence = release?.Sequence ?? 0
                };
                Write(game, journal); recorded = true; Boundary("PrepareRecorded");
                Directory.Move(staging, standalone); Boundary("PrepareMoved");
                journal.Step = "Idle"; journal.Staging = null; Write(game, journal);
            } finally {
                if (!recorded && Directory.Exists(staging)) { foreach (string file in Directory.GetFiles(staging)) File.Delete(file); Directory.Delete(staging); }
            }
        }
        internal static void Apply(Settings settings)
        { using (Acquire(settings.GameDirectory)) ApplyLocked(settings, settings.GameIntegrationMode); }
        private static void ApplyLocked(Settings settings, string target, string binaryDirectory = null, bool keepStockDmm = false)
        {
            RequireClosed(settings.GameDirectory); string game = Path.GetFullPath(settings.GameDirectory); var journal = Read(game);
            if (journal == null) throw new IOException("Prepare verified profiles first.");
            if (journal.Step != "Idle") throw new IOException("An interrupted switch needs recovery before another mode change.");
            if (target != "Hachimi" && target != "Standalone" && target != "Original") throw new ArgumentException("Unknown integration mode.");
            if (target == "Standalone") {
                string directory = target == journal.Configured ? "umamusume.exe.local" : "umapresence\\profiles\\standalone";
                RequireMatchingBuild(game, Path.Combine(Contained(game, directory), "game-profile.json"));
            }
            if (target == journal.Configured) return;
            if (target == "Hachimi" && journal.HachimiFiles.Count == 0) throw new IOException("This clean installation has no saved Hachimi profile to enable. Standalone preparation does not install the full Hachimi mod.");
            string active = Contained(game, "umamusume.exe.local"), targetPath = Contained(game, "umapresence\\profiles\\" + target.ToLowerInvariant());
            string sourcePath = Contained(game, "umapresence\\profiles\\" + journal.Configured.ToLowerInvariant());
            if (target != "Original") Verify(targetPath, target == "Standalone" ? journal.StandaloneFiles : journal.HachimiFiles);
            else if (Directory.Exists(targetPath)) throw new IOException("Unexpected original profile directory.");
            if (journal.Configured == "Original" && Directory.Exists(active)) throw new IOException("Unknown active loader; nothing was overwritten.");
            if (journal.Configured == "Standalone") Verify(active, journal.StandaloneFiles);
            if (journal.Configured == "Hachimi") {
                var files = Files(active);
                if (!MatchFiles(files, journal.HachimiFiles)) {
                    bool changedPair = hachimiPair.Any(name => !files.TryGetValue(name, out string hash)
                        || !journal.HachimiFiles.TryGetValue(name, out string previous) || hash != previous);
                    if (!changedPair || !CanSnapshotHachimi(game, files, ReadProvenance(binaryDirectory)))
                        throw new IOException("Hachimi files changed outside UmaPresence. Start the game with Hachimi and confirm UmaPresence receives activity before switching.");
                    journal.HachimiFiles = files;
                }
            }
            if (Directory.Exists(sourcePath)) throw new IOException("Inactive source profile already exists; refusing to overwrite it.");
            journal.Target = target; journal.Step = "Prepared"; Write(game, journal); Boundary("SwitchPrepared");
            if (Directory.Exists(active)) Directory.Move(active, sourcePath);
            Boundary("SourceMoved"); journal.Step = "SourceStored"; Write(game, journal); Boundary("SourceRecorded");
            if (target != "Original") Directory.Move(targetPath, active);
            Boundary("TargetMoved"); journal.Step = "TargetActivated"; Write(game, journal); Boundary("TargetRecorded");
            journal.Configured = target; journal.Target = null; journal.Step = "Idle"; journal.KeepStockDmm = keepStockDmm && target == "Original"; Write(game, journal);
        }
        internal static void Recover(Settings settings)
        {
            string game = Path.GetFullPath(settings.GameDirectory);
            bool removal;
            using (Acquire(game)) {
                removal = Read(game)?.Step == "Removing";
                RecoverLocked(settings);
            }
            if (removal) CleanupRuntimeFiles(game);
        }
        private static void RecoverLocked(Settings settings)
        {
            RequireClosed(settings.GameDirectory); string game = Path.GetFullPath(settings.GameDirectory); var journal = Read(game); if (journal == null || journal.Step == "Idle") return;
            if (journal.Step == "UpdatingStandalone") { RecoverStandaloneUpdate(game, journal); return; }
            if (journal.Step == "Removing") { FinishRemoval(game, journal); return; }
            if (journal.Step == "Preparing") {
                string staging = Contained(game, "umapresence\\profiles\\" + journal.Staging), ready = Contained(game, "umapresence\\profiles\\standalone");
                if (Directory.Exists(staging) && Directory.Exists(ready)) throw new IOException("Both staging and completed profiles exist; recovery refused.");
                Verify(Directory.Exists(staging) ? staging : ready, journal.StandaloneFiles);
                if (Directory.Exists(staging)) Directory.Move(staging, ready);
                journal.Staging = null; journal.Step = "Idle"; Write(game, journal); return;
            }
            string active = Contained(game, "umamusume.exe.local"), targetPath = Contained(game, "umapresence\\profiles\\" + journal.Target.ToLowerInvariant()), sourcePath = Contained(game, "umapresence\\profiles\\" + journal.Configured.ToLowerInvariant());
            // File identity, rather than the last persisted step, makes rollback
            // repeatable even if interruption occurred immediately after a move.
            bool sourceStored = journal.Configured == "Original" || Directory.Exists(sourcePath);
            if (sourceStored && Directory.Exists(active)) {
                if (journal.Target == "Original") throw new IOException("Unexpected active loader while restoring original.");
                Verify(active, journal.Target == "Standalone" ? journal.StandaloneFiles : journal.HachimiFiles);
                if (Directory.Exists(targetPath)) throw new IOException("Recovery target is occupied."); Directory.Move(active, targetPath);
            }
            if (journal.Configured != "Original") {
                Verify(Directory.Exists(active) ? active : sourcePath, journal.Configured == "Standalone" ? journal.StandaloneFiles : journal.HachimiFiles);
                if (!Directory.Exists(active)) Directory.Move(sourcePath, active);
            }
            journal.Step = "Idle"; journal.Target = null; Write(game, journal);
        }
        internal static void RestoreOriginal(Settings settings)
        { using (Acquire(settings.GameDirectory)) ApplyLocked(settings, "Original"); }
        internal static void ReturnToStockDmm(Settings settings)
        {
            using (Acquire(settings.GameDirectory)) {
                var journal = Read(settings.GameDirectory);
                if (journal?.Configured != "Standalone" || journal.Step != "Idle")
                    throw new IOException("Return to stock DMM is available when Standalone is active and there is no interrupted change.");
                ApplyLocked(settings, "Original", keepStockDmm: true);
            }
        }
        internal static void Uninstall(Settings settings)
        {
            using (Acquire(settings.GameDirectory)) UninstallLocked(settings, false, false);
        }
        private static void UninstallLocked(Settings settings, bool removeRegistrations, bool fullRemoval)
        {
            RequireClosed(settings.GameDirectory);
            string game = Path.GetFullPath(settings.GameDirectory);
            var journal = Read(game);
            if (journal != null && journal.Step != "Idle" && journal.Step != "Removing")
                throw new IOException("Recover the interrupted operation before uninstalling.");
            if (removeRegistrations || (journal?.Step == "Removing" && journal.RemoveBridgeRegistrations != false)) {
                RemovePreviousBridgeRegistration(game, false);
                RemoveManagedBridges(game, false);
            }
            if (journal == null) {
                if (removeRegistrations) RemoveRegistrations(game, fullRemoval);
                return;
            }
            bool removing = journal.Step == "Removing";
            if (!removing) {
                if (journal.Configured != "Hachimi" && !journal.KeepStockDmm) {
                    ApplyLocked(settings, journal.HachimiFiles.Count > 0 ? "Hachimi" : "Original");
                    journal = Read(game);
                }
                Verify(Contained(game, "umapresence\\profiles\\standalone"), journal.StandaloneFiles);
                journal.Step = "Removing";
                journal.Target = "Original";
                journal.RemoveBridgeRegistrations = removeRegistrations;
            } else if (removeRegistrations) journal.RemoveBridgeRegistrations = true;
            journal.FullRemoval |= fullRemoval;
            Write(game, journal);
            if (!removing) Boundary("RemoveRecorded");
            FinishRemoval(game, journal);
        }
        private static void FinishRemoval(string game, IntegrationJournal journal)
        {
            string standalone = Contained(game, "umapresence\\profiles\\standalone"), quarantine = Contained(game, "umapresence\\profiles\\.removing");
            if (journal.Configured == "Standalone") throw new IOException("Refusing to remove an active standalone profile.");
            // A Hachimi update can replace the active loader. Removal only
            // touches the isolated Standalone profile, so preserve it verbatim.
            string activeLoader = Contained(game, "umamusume.exe.local\\UnityPlayer.dll");
            if (journal.StandaloneFiles.TryGetValue("UnityPlayer.dll", out string standaloneHash)
                && File.Exists(activeLoader) && Hash(activeLoader) == standaloneHash)
                throw new IOException("Refusing to remove a profile while its Standalone loader is active.");
            if (journal.Configured != "Hachimi" && Directory.Exists(Contained(game, "umamusume.exe.local")))
                throw new IOException("Unexpected active loader blocks removal.");
            if (Directory.Exists(standalone)) { if (Directory.Exists(quarantine)) throw new IOException("Removal quarantine is occupied."); Verify(standalone, journal.StandaloneFiles); Directory.Move(standalone, quarantine); Boundary("RemoveMoved"); }
            var present = Files(quarantine);
            if (present.Any(pair => !journal.StandaloneFiles.ContainsKey(pair.Key) || journal.StandaloneFiles[pair.Key] != pair.Value)) throw new IOException("Changed or unknown files block removal; nothing further was deleted.");
            foreach (string relative in journal.StandaloneFiles.Keys) { string file = Contained(game, "umapresence\\profiles\\.removing\\" + relative); if (File.Exists(file)) { File.Delete(file); Boundary("RemoveFile"); } }
            if (Directory.Exists(quarantine)) { foreach (string directory in Directory.GetDirectories(quarantine, "*", SearchOption.AllDirectories).OrderByDescending(path => path.Length)) Directory.Delete(directory); Directory.Delete(quarantine); }
            RemoveStandaloneUpdateBackup(game, journal);
            if (journal.RemoveBridgeRegistrations != false) RemoveRegistrations(game, journal.FullRemoval);
            foreach (string path in new[] { JournalPath(game), JournalPath(game) + ".previous", JournalPath(game) + ".new" }) if (File.Exists(path)) File.Delete(path);
        }
    }
}
