using System;
using System.IO;
using System.Security.Cryptography;

namespace UmapyonPresence
{
    internal static class StandaloneGameCompatibilityTests
    {
        internal static void Run(Action<bool, string> check)
        {
            var image = new byte[1024];
            void WriteField(byte[] bytes, int offset, int value) =>
                Buffer.BlockCopy(BitConverter.GetBytes(value), 0, bytes, offset, 4);
            WriteField(image, 0, 0x5a4d);
            WriteField(image, 60, 64);
            WriteField(image, 64, 0x4550);
            WriteField(image, 68, 0x18664);
            WriteField(image, 84, 0xf0);
            WriteField(image, 88, 0x20b);
            WriteField(image, 88 + 60, 0x200);
            WriteField(image, 88 + 108, 16);
            WriteField(image, 88 + 0xf0 + 16, 0x200);
            WriteField(image, 88 + 0xf0 + 20, 0x200);
            image[512] = 0x90;
            string identity;
            using (var stream = new MemoryStream(image)) identity = StandaloneGameCompatibility.CompatibilityHash(stream);
            check(identity != null, "PE game fixture has a valid byte identity");
            string exact;
            using (var hash = SHA256.Create())
                exact = BitConverter.ToString(hash.ComputeHash(image)).Replace("-", "").ToLowerInvariant();
            var profile = new { sha256 = exact, compatibilitySha256 = identity };
            string fixture = Path.Combine(Path.GetTempPath(), "umapresence-compatibility-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(fixture);
            string expected = Path.Combine(fixture, "game-profile.json");
            string unity = Path.Combine(fixture, "UnityPlayer.dll");
            string assembly = Path.Combine(fixture, "GameAssembly.dll");
            try {
                File.WriteAllText(expected, Json.Encode(new { unity = profile, assembly = profile }));
                File.WriteAllBytes(unity, image);
                File.WriteAllBytes(assembly, image);
                check(StandaloneGameCompatibility.Check(fixture, expected) == StandaloneGameSupport.Exact,
                    "Exact game profile takes the fast path");
                var signed = new byte[1040];
                Buffer.BlockCopy(image, 0, signed, 0, image.Length);
                WriteField(signed, 152, 42);
                WriteField(signed, 232, 1024);
                WriteField(signed, 236, 16);
                WriteField(signed, 1024, 16);
                WriteField(signed, 1028, 0x20200);
                signed[1032] = 3;
                File.WriteAllBytes(assembly, signed);
                check(StandaloneGameCompatibility.Check(fixture, expected) == StandaloneGameSupport.Equivalent,
                    "PE checksum and a detached certificate change can reuse the known game profile");
                signed[512] ^= 1;
                File.WriteAllBytes(assembly, signed);
                check(StandaloneGameCompatibility.Check(fixture, expected) == StandaloneGameSupport.Unsupported,
                    "Changed executable bytes require a different verified Standalone bundle");
                signed[512] ^= 1;
                WriteField(signed, 232, 992);
                WriteField(signed, 236, 48);
                File.WriteAllBytes(assembly, signed);
                check(StandaloneGameCompatibility.Check(fixture, expected) == StandaloneGameSupport.Unsupported,
                    "A certificate overlapping mapped data cannot bypass the game profile");
                check(new StandaloneRuntimeStatus { State = 2, Backend = 1 }.RequiresBasicPresence &&
                    !new StandaloneRuntimeStatus { State = 4, Backend = 1 }.RequiresBasicPresence &&
                    !new StandaloneRuntimeStatus { State = 7, Backend = 2 }.RequiresBasicPresence,
                    "Only a failed active Standalone session uses basic presence, preserving Hachimi ownership");
            } finally {
                foreach (string path in new[] { expected, unity, assembly }) File.Delete(path);
                Directory.Delete(fixture);
            }
        }
    }
}
