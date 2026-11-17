using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace UmapyonPresence
{
    internal sealed class StandaloneUpdateCheck
    {
        internal StandaloneUpdateCheck(string message, StandaloneUpdateOffer update = null) { Message = message; Update = update; }
        internal string Message { get; }
        internal StandaloneUpdateOffer Update { get; }
    }

    internal sealed class StandaloneUpdateOffer
    {
        internal StandaloneUpdateOffer(StandaloneFeedEntry entry, Uri download, string gameDirectory, DateTimeOffset expires)
        { Entry = entry; Download = download; GameDirectory = gameDirectory; Expires = expires; }
        internal StandaloneFeedEntry Entry { get; }
        internal Uri Download { get; }
        internal string GameDirectory { get; }
        internal DateTimeOffset Expires { get; }
        internal string Version => Entry.Version;
        internal string Features => string.Join(", ", Entry.Features);
    }

    internal sealed class StandaloneFeedPolicy
    {
        public string ManifestUrl { get; set; }
        public string Modulus { get; set; }
        public string Exponent { get; set; }
    }

    internal sealed class SignedStandaloneManifest
    {
        public string Manifest { get; set; }
        public string Signature { get; set; }
    }

    internal sealed class StandaloneFeedManifest
    {
        public int Format { get; set; }
        public string Product { get; set; }
        public string ExpiresUtc { get; set; }
        public StandaloneFeedEntry[] Releases { get; set; }
    }

    internal sealed class StandaloneFeedEntry
    {
        public long Sequence { get; set; }
        public string Version { get; set; }
        public string CompanionVersion { get; set; }
        public string UnitySha256 { get; set; }
        public string AssemblySha256 { get; set; }
        public string ArchiveSha256 { get; set; }
        public Dictionary<string, string> Files { get; set; }
        public string[] Features { get; set; }
    }

    internal static class StandaloneUpdates
    {
        private const string Product = "UmaPresence Standalone compatibility";
        private const int ManifestLimit = 256 * 1024, BundleLimit = 20 * 1024 * 1024, ProfileLimit = 128 * 1024;
        internal static readonly string[] BundleFiles = { "UnityPlayer.dll", "umapyon_bridge.dll", "game-profile.json" };
        private static readonly string[] AllowedFeatures = { "Detailed presence", "Song bar", "Duplicate casting" };
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        private static StandaloneFeedPolicy PackagedPolicy()
        {
            using (var resource = typeof(StandaloneUpdates).Assembly.GetManifestResourceStream("compatibility-feed.json"))
                return resource == null ? null : Json.Decode<StandaloneFeedPolicy>(Utf8.GetString(ReadBounded(resource, ManifestLimit)));
        }

        internal static Task<StandaloneUpdateCheck> CheckAsync(Settings settings, int pid = 0)
        {
            string directory = settings.GameDirectory;
            return Task.Run(() => Check(directory, PackagedPolicy(), Download, pid));
        }

        internal static StandaloneUpdateCheck Check(string directory, StandaloneFeedPolicy policy, Func<Uri, int, byte[]> download, int pid = 0)
        {
            if (!GameInstallationDiscovery.Valid(directory)) return new StandaloneUpdateCheck("Choose your Umamusume game folder first.");
            string game = Path.GetFullPath(directory);
            var runtime = StandaloneRuntime.Read(pid);
            bool failed = runtime != null && (runtime.State == 2 || runtime.State == 5 || runtime.OwnershipConflict)
                && string.Equals(GameInstallationDiscovery.ProcessPath(pid), Path.Combine(game, "umamusume.exe"), StringComparison.OrdinalIgnoreCase);
            if (!failed && IntegrationProfiles.CanUseCurrentStandalone(game))
                return new StandaloneUpdateCheck("Standalone can use the current game build.");
            if (policy == null)
                return new StandaloneUpdateCheck("No verified Standalone update is ready. Basic Discord presence and uninstall remain available.");
            var manifestUrl = FeedUri(policy);
            DateTimeOffset expires;
            var manifest = Authenticate(download(manifestUrl, ManifestLimit), policy, out expires);
            string unity = IntegrationProfiles.Hash(Path.Combine(game, "UnityPlayer.dll"));
            string assembly = IntegrationProfiles.Hash(Path.Combine(game, "GameAssembly.dll"));
            long installed = IntegrationProfiles.StandaloneUpdateSequence(game);
            var entry = manifest.Releases.Where(row => row.UnitySha256 == unity && row.AssemblySha256 == assembly
                && row.CompanionVersion == ReleasePolicy.StandaloneUpdateChannel && row.Sequence > installed)
                .OrderByDescending(row => row.Sequence).FirstOrDefault();
            if (entry == null) return new StandaloneUpdateCheck("No verified Standalone update is ready for this game version. Basic Discord presence and uninstall remain available.");
            string archive = "standalone-" + entry.Sequence.ToString(CultureInfo.InvariantCulture) + ".zip";
            return new StandaloneUpdateCheck("A verified Standalone update is ready. Close the game to install it.",
                new StandaloneUpdateOffer(entry, new Uri(manifestUrl, archive), game, expires));
        }

        internal static Task InstallAsync(Settings settings, StandaloneUpdateOffer offer)
        {
            string directory = settings.GameDirectory;
            return Task.Run(() => Install(directory, offer, Download));
        }

        internal static void Install(string directory, StandaloneUpdateOffer offer, Func<Uri, int, byte[]> download)
        {
            if (offer == null || !GameInstallationDiscovery.Valid(directory) ||
                !string.Equals(Path.GetFullPath(directory), offer.GameDirectory, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Select the same game folder and check compatibility again.");
            if (GameInstallationDiscovery.IsGameRunning(offer.GameDirectory))
                throw new IOException("Close Umamusume before installing the Standalone update.");
            if (offer.Expires <= DateTimeOffset.UtcNow) throw new IOException("The update offer expired. Check compatibility again.");
            var archive = download(offer.Download, BundleLimit);
            var files = VerifyBundle(offer.Entry, archive);
            if (offer.Expires <= DateTimeOffset.UtcNow) throw new IOException("The update offer expired. Check compatibility again.");
            IntegrationProfiles.InstallStandaloneUpdate(offer.GameDirectory, offer.Entry, files);
        }

        internal static string Status(Settings settings, int pid) => IntegrationProfiles.StandaloneUpdateStatus(settings.GameDirectory, pid);

        internal static bool CanRestore(Settings settings) => IntegrationProfiles.CanRestoreStandaloneUpdate(settings.GameDirectory);

        internal static void Restore(Settings settings) => IntegrationProfiles.RestoreStandaloneUpdate(settings.GameDirectory);

        internal static void Observe(Settings settings, int pid, bool telemetryReceived)
        {
            if (telemetryReceived && pid > 0 && !string.IsNullOrWhiteSpace(settings.GameDirectory))
                IntegrationProfiles.ObserveStandaloneUpdate(settings.GameDirectory, pid);
        }

        private static Uri FeedUri(StandaloneFeedPolicy policy)
        {
            if (!Uri.TryCreate(policy.ManifestUrl, UriKind.Absolute, out Uri url)
                || url.Scheme != Uri.UriSchemeHttps || !url.IsDefaultPort || url.UserInfo.Length != 0
                || url.Query.Length != 0 || url.Fragment.Length != 0
                || !url.AbsolutePath.EndsWith("/manifest.json", StringComparison.Ordinal))
                throw new InvalidDataException("The packaged Standalone update address is invalid.");
            return url;
        }

        internal static StandaloneFeedManifest Authenticate(byte[] envelope, StandaloneFeedPolicy policy, out DateTimeOffset expires)
        {
            if (envelope == null || envelope.Length > ManifestLimit) throw new InvalidDataException("The Standalone update manifest is too large.");
            try {
                var signed = Json.Decode<SignedStandaloneManifest>(Utf8.GetString(envelope));
                if (signed == null || string.IsNullOrEmpty(signed.Manifest) || string.IsNullOrEmpty(signed.Signature))
                    throw new InvalidDataException("The Standalone update manifest is incomplete.");
                byte[] payload = Convert.FromBase64String(signed.Manifest), signature = Convert.FromBase64String(signed.Signature);
                byte[] modulus = Convert.FromBase64String(policy.Modulus ?? "");
                if (modulus.Length < 256 || modulus.Length > 512 || modulus[0] < 0x80
                    || policy.Exponent != "AQAB" || signature.Length != modulus.Length)
                    throw new InvalidDataException("The Standalone update signing key is invalid.");
                using (var key = new RSACryptoServiceProvider()) {
                    key.PersistKeyInCsp = false;
                    key.ImportParameters(new RSAParameters { Modulus = modulus, Exponent = new byte[] { 1, 0, 1 } });
                    if (!key.VerifyData(payload, "SHA256", signature))
                        throw new InvalidDataException("The Standalone update signature did not match.");
                }
                var manifest = Json.Decode<StandaloneFeedManifest>(Utf8.GetString(payload));
                if (manifest == null || manifest.Product != Product || manifest.Format != 1
                    || !DateTimeOffset.TryParseExact(manifest.ExpiresUtc, "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out expires)
                    || expires.Offset != TimeSpan.Zero || expires <= DateTimeOffset.UtcNow
                    || manifest.Releases == null || manifest.Releases.Length > 256)
                    throw new InvalidDataException("The Standalone update manifest is invalid or expired.");
                var sequences = new HashSet<long>();
                foreach (var entry in manifest.Releases) {
                    if (entry == null || entry.Sequence <= 0 || !sequences.Add(entry.Sequence)
                        || !ValidVersion(entry.Version) || !ValidVersion(entry.CompanionVersion)
                        || !ValidHash(entry.UnitySha256) || !ValidHash(entry.AssemblySha256) || !ValidHash(entry.ArchiveSha256)
                        || !ValidBundleFiles(entry.Files)
                        || entry.Features == null || !entry.Features.Contains("Detailed presence") || entry.Features.Distinct().Count() != entry.Features.Length
                        || entry.Features.Any(feature => !AllowedFeatures.Contains(feature)))
                        throw new InvalidDataException("The Standalone update manifest contains an invalid release.");
                }
                return manifest;
            } catch (Exception ex) when (ex is ArgumentException || ex is CryptographicException || ex is FormatException || ex is InvalidOperationException) {
                throw new InvalidDataException("The Standalone update manifest could not be verified.", ex);
            }
        }

        private static bool ValidBundleFiles(Dictionary<string, string> files)
        {
            return files != null && files.Count == BundleFiles.Length
                && BundleFiles.All(name => files.ContainsKey(name) && ValidHash(files[name]));
        }

        internal static bool ValidHash(string value) => value != null && Regex.IsMatch(value, @"\A[0-9a-f]{64}\z");
        internal static bool ValidVersion(string value) => value != null && Regex.IsMatch(value, @"\A[0-9A-Za-z][0-9A-Za-z.+-]{0,39}\z");

        internal static Dictionary<string, byte[]> VerifyBundle(StandaloneFeedEntry entry, byte[] archiveBytes)
        {
            if (archiveBytes == null || archiveBytes.Length > BundleLimit || Digest(archiveBytes) != entry.ArchiveSha256 || !ValidBundleFiles(entry.Files))
                throw new InvalidDataException("The Standalone download did not match the signed manifest.");
            var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            using (var archive = new ZipArchive(new MemoryStream(archiveBytes, false), ZipArchiveMode.Read)) {
                if (archive.Entries.Count != BundleFiles.Length) throw new InvalidDataException("The Standalone download has an unexpected file list.");
                foreach (var item in archive.Entries) {
                    if (!entry.Files.TryGetValue(item.FullName, out string expected) || files.ContainsKey(item.FullName))
                        throw new InvalidDataException("The Standalone download has an unexpected file.");
                    int limit = item.FullName == "game-profile.json" ? ProfileLimit : BundleLimit;
                    if (item.Length == 0 || item.Length > limit) throw new InvalidDataException("A Standalone update file has an invalid size.");
                    using (var stream = item.Open()) {
                        var data = ReadBounded(stream, limit);
                        if (Digest(data) != expected) throw new InvalidDataException("A Standalone update file did not match the signed manifest.");
                        files.Add(item.FullName, data);
                    }
                }
            }
            var profile = Json.Decode<Dictionary<string, object>>(Utf8.GetString(files["game-profile.json"]).TrimStart('\uFEFF'));
            if (ProfileHash(profile, "unity") != entry.UnitySha256 || ProfileHash(profile, "assembly") != entry.AssemblySha256)
                throw new InvalidDataException("The downloaded Standalone profile targets a different game build.");
            return files;
        }

        private static string ProfileHash(Dictionary<string, object> profile, string name)
        {
            return profile != null && profile.TryGetValue(name, out object raw) && raw is Dictionary<string, object> module
                && module.TryGetValue("sha256", out object hash) ? hash as string : null;
        }

        private static string Digest(byte[] bytes)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        private static byte[] Download(Uri url, int limit)
        {
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.AllowAutoRedirect = false;
            request.Timeout = request.ReadWriteTimeout = 8000;
            request.UserAgent = "UmaPresence/" + ReleasePolicy.Version;
            var clock = Stopwatch.StartNew();
            using (var response = (HttpWebResponse)request.GetResponse()) {
                if (response.StatusCode != HttpStatusCode.OK || response.ContentLength > limit)
                    throw new IOException("The Standalone update could not be downloaded.");
                using (var stream = response.GetResponseStream())
                    return ReadBounded(stream, limit, clock);
            }
        }

        private static byte[] ReadBounded(Stream input, int limit, Stopwatch clock = null)
        {
            using (var result = new MemoryStream()) {
                var buffer = new byte[16384];
                int count;
                while ((count = input.Read(buffer, 0, buffer.Length)) != 0) {
                    if (clock != null && clock.Elapsed.TotalSeconds > 30) throw new IOException("The Standalone update download took too long.");
                    if (result.Length + count > limit) throw new InvalidDataException("The Standalone update file is too large.");
                    result.Write(buffer, 0, count);
                }
                return result.ToArray();
            }
        }
    }
}
