using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace UmapyonPresence
{
    internal static class StandaloneUpdateTests
    {
        private static void Check(bool passed, string label)
        {
            if (!passed) throw new Exception("FAIL: " + label);
            Console.WriteLine("PASS: " + label);
        }

        private static byte[] Bytes(string value) => Encoding.UTF8.GetBytes(value);

        private static string Hash(byte[] bytes)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        private static byte[] Zip(Dictionary<string, byte[]> files)
        {
            using (var stream = new MemoryStream()) {
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
                    foreach (var file in files) using (var body = zip.CreateEntry(file.Key).Open()) body.Write(file.Value, 0, file.Value.Length);
                return stream.ToArray();
            }
        }

        internal static void Run()
        {
            using (var fixture = new Fixture(false))
            using (var signing = new RSACryptoServiceProvider(2048)) {
                signing.PersistKeyInCsp = false;
                var publicKey = signing.ExportParameters(false);
                var policy = new StandaloneFeedPolicy {
                    ManifestUrl = "https://updates.example.invalid/manifest.json",
                    Modulus = Convert.ToBase64String(publicKey.Modulus), Exponent = Convert.ToBase64String(publicKey.Exponent)
                };
                var manifest = new StandaloneFeedManifest {
                    Format = 1, Product = "UmaPresence Standalone compatibility",
                    ExpiresUtc = DateTimeOffset.UtcNow.AddDays(1).ToString("O", CultureInfo.InvariantCulture),
                    Releases = new[] { fixture.Entry }
                };
                var payload = Bytes(Json.Encode(manifest));
                var signature = signing.SignData(payload, "SHA256");
                byte[] Signed(byte[] bytes) => Bytes(Json.Encode(new SignedStandaloneManifest {
                    Manifest = Convert.ToBase64String(bytes), Signature = Convert.ToBase64String(signature)
                }));
                var result = StandaloneUpdates.Check(fixture.Game, policy, (uri, limit) => Signed(payload));
                Check(result.Update != null && result.Update.Download.AbsolutePath == "/standalone-1.zip", "A signed update matches the exact build and companion");
                var invalid = (byte[])payload.Clone();
                invalid[0] ^= 1;
                Reject(() => StandaloneUpdates.Check(fixture.Game, policy, (uri, limit) => Signed(invalid)), "Changed manifest bytes fail signature verification");
                var corrupt = (byte[])fixture.Archive.Clone();
                corrupt[0] ^= 1;
                Reject(() => StandaloneUpdates.Install(fixture.Game, result.Update, (uri, limit) => corrupt), "A changed archive fails before loader files are written");
                var traversing = new Dictionary<string, byte[]>(fixture.NewFiles);
                traversing.Add("../foreign.dll", Bytes("unexpected"));
                byte[] enlarged = Zip(traversing);
                string expectedArchive = fixture.Entry.ArchiveSha256;
                fixture.Entry.ArchiveSha256 = Hash(enlarged);
                Reject(() => StandaloneUpdates.VerifyBundle(fixture.Entry, enlarged), "The bundle accepts only its three allowed files");
                fixture.Entry.ArchiveSha256 = expectedArchive;
                string module = Path.Combine(fixture.Game, "GameAssembly.dll");
                string before = File.ReadAllText(module);
                File.AppendAllText(module, "changed game");
                Check(StandaloneUpdates.Check(fixture.Game, policy, (uri, limit) => Signed(payload)).Update == null, "A different game build cannot select the signed bundle");
                Reject(() => StandaloneUpdates.Install(fixture.Game, result.Update, (uri, limit) => fixture.Archive), "A game change between offer and install leaves the loader alone");
                File.WriteAllText(module, before);
                StandaloneUpdates.Install(fixture.Game, result.Update, (uri, limit) => fixture.Archive);
                Check(fixture.Journal().Configured == "Hachimi" && File.ReadAllText(fixture.HachimiLoader) == "external Hachimi", "Installing while Hachimi is active leaves Hachimi in place");
                Check(File.ReadAllText(Path.Combine(fixture.Target, "UnityPlayer.dll")) == "updated loader" && fixture.Journal().StandaloneRelease.Sequence == 1, "The signed bundle becomes the saved Standalone profile");
                Check(StandaloneUpdates.Check(fixture.Game, null, (uri, limit) => throw new Exception("unexpected network request")).Update == null, "The installed matching profile does not require a network request");
                Reject(() => StandaloneUpdates.Install(fixture.Game, result.Update, (uri, limit) => fixture.Archive), "A previously accepted sequence cannot be replayed");
            }

            foreach (string stage in new[] { "UpdateRecorded", "UpdateOldMoved", "UpdateNewMoved" }) using (var fixture = new Fixture(true)) {
                IntegrationProfiles.Fault = name => { if (name == stage) throw new IOException("Simulated interruption"); };
                try { Reject(() => fixture.Install(), "Installation can be interrupted at " + stage); }
                finally { IntegrationProfiles.Fault = null; }
                IntegrationProfiles.Recover(new Settings { GameDirectory = fixture.Game });
                Check(fixture.Journal().Step == "Idle" && fixture.Journal().StandaloneRelease == null
                    && File.ReadAllText(Path.Combine(fixture.Target, "UnityPlayer.dll")) == "original loader",
                    "Recovery restores the previous active loader after " + stage);
                fixture.Install();
                Check(System.Threading.SpinWait.SpinUntil(() => IntegrationProfiles.CanRestoreStandaloneUpdate(fixture.Game), 2000),
                    "The previous compatible Standalone profile can be restored");
                IntegrationProfiles.RestoreStandaloneUpdate(fixture.Game);
                Check(fixture.Journal().StandaloneUpdateSequence == 1 && fixture.Journal().StandaloneRelease == null
                    && File.ReadAllText(Path.Combine(fixture.Target, "UnityPlayer.dll")) == "original loader",
                    "Restore preserves the update history and returns to the compatible prior loader");
            }
        }

        private static void Reject(Action action, string label)
        {
            try { action(); }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException) { Check(true, label); return; }
            throw new Exception("FAIL: " + label);
        }

        private sealed class Fixture : IDisposable
        {
            internal string Game { get; }
            internal string Target { get; }
            internal string HachimiLoader { get; }
            internal StandaloneFeedEntry Entry { get; }
            internal Dictionary<string, byte[]> NewFiles { get; }
            internal byte[] Archive { get; }
            private string JournalPath => Path.Combine(Game, "umapresence", "integration-journal.json");

            internal Fixture(bool active)
            {
                Game = Path.Combine(Path.GetTempPath(), "UmaPresence-signed-test-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path.Combine(Game, "umamusume_Data"));
                foreach (string file in new[] { "umamusume.exe", "UnityPlayer.dll", "GameAssembly.dll" }) File.WriteAllText(Path.Combine(Game, file), file);
                string unity = IntegrationProfiles.Hash(Path.Combine(Game, "UnityPlayer.dll")), assembly = IntegrationProfiles.Hash(Path.Combine(Game, "GameAssembly.dll"));
                var reference = Bytes(Json.Encode(new { unity = new { sha256 = unity }, assembly = new { sha256 = assembly } }));
                var activeDirectory = Path.Combine(Game, "umamusume.exe.local");
                Target = active ? activeDirectory : Path.Combine(Game, "umapresence", "profiles", "standalone");
                HachimiLoader = Path.Combine(activeDirectory, "UnityPlayer.dll");
                Directory.CreateDirectory(Target);
                if (!active) { Directory.CreateDirectory(activeDirectory); File.WriteAllText(HachimiLoader, "external Hachimi"); }
                var originals = new Dictionary<string, byte[]> {
                    { "UnityPlayer.dll", Bytes("original loader") }, { "umapyon_bridge.dll", Bytes("original bridge") },
                    { "game-profile.json", active ? reference : Bytes("{}") }, { "apphelp.dll", Bytes("original helper") }
                };
                foreach (var file in originals) File.WriteAllBytes(Path.Combine(Target, file.Key), file.Value);
                NewFiles = new Dictionary<string, byte[]> {
                    { "UnityPlayer.dll", Bytes("updated loader") }, { "umapyon_bridge.dll", Bytes("updated bridge") }, { "game-profile.json", reference }
                };
                Archive = Zip(NewFiles);
                Entry = new StandaloneFeedEntry {
                    Sequence = 1, Version = "test-1", CompanionVersion = ReleasePolicy.Version, UnitySha256 = unity, AssemblySha256 = assembly,
                    ArchiveSha256 = Hash(Archive), Files = NewFiles.ToDictionary(pair => pair.Key, pair => Hash(pair.Value)), Features = new[] { "Detailed presence", "Song bar" }
                };
                Directory.CreateDirectory(Path.GetDirectoryName(JournalPath));
                File.WriteAllText(JournalPath, Json.Encode(new IntegrationJournal {
                    GameDirectory = Game, Configured = active ? "Standalone" : "Hachimi", Step = "Idle",
                    StandaloneFiles = originals.ToDictionary(pair => pair.Key, pair => Hash(pair.Value))
                }));
            }

            internal IntegrationJournal Journal() => Json.Decode<IntegrationJournal>(File.ReadAllText(JournalPath));

            internal void Install() => StandaloneUpdates.Install(Game,
                new StandaloneUpdateOffer(Entry, new Uri("https://updates.example.invalid/standalone-1.zip"), Game, DateTimeOffset.UtcNow.AddHours(1)),
                (uri, limit) => Archive);

            public void Dispose()
            {
                if (Directory.Exists(Game)) Directory.Delete(Game, true);
            }
        }
    }
}
