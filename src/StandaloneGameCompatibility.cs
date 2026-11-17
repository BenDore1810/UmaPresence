using System;
using System.IO;
using System.Security.Cryptography;

namespace UmapyonPresence
{
    internal enum StandaloneGameSupport { Unsupported, Exact, Equivalent }

    internal static class StandaloneGameCompatibility
    {
        private const int MaxPeLength = 0x40000000;
        private sealed class ModuleProfile
        {
            public string sha256 { get; set; }
            public string compatibilitySha256 { get; set; }
        }

        private sealed class GameProfile
        {
            public ModuleProfile unity { get; set; }
            public ModuleProfile assembly { get; set; }
        }

        internal static StandaloneGameSupport Check(string gameDirectory, string profilePath)
        {
            if (string.IsNullOrWhiteSpace(gameDirectory) || string.IsNullOrWhiteSpace(profilePath))
                return StandaloneGameSupport.Unsupported;
            try {
                var profile = Json.Decode<GameProfile>(File.ReadAllText(profilePath));
                if (profile?.unity == null || profile.assembly == null) return StandaloneGameSupport.Unsupported;
                var unity = CheckModule(Path.Combine(gameDirectory, "UnityPlayer.dll"), profile.unity);
                if (unity == StandaloneGameSupport.Unsupported) return unity;
                var assembly = CheckModule(Path.Combine(gameDirectory, "GameAssembly.dll"), profile.assembly);
                if (assembly == StandaloneGameSupport.Unsupported) return assembly;
                return unity == StandaloneGameSupport.Exact && assembly == StandaloneGameSupport.Exact
                    ? StandaloneGameSupport.Exact : StandaloneGameSupport.Equivalent;
            } catch (Exception error) when (error is IOException || error is UnauthorizedAccessException ||
                error is ArgumentException || error is InvalidOperationException) {
                return StandaloneGameSupport.Unsupported;
            }
        }

        internal static void Require(string gameDirectory, string profilePath)
        {
            if (Check(gameDirectory, profilePath) == StandaloneGameSupport.Unsupported)
                throw new IOException("The Standalone profile does not match this game build. Check compatibility before activating Standalone.");
        }

        private static StandaloneGameSupport CheckModule(string path, ModuleProfile expected)
        {
            if (expected.sha256 == null || (File.GetAttributes(path) & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
                return StandaloneGameSupport.Unsupported;
            using (var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var hash = SHA256.Create()) {
                if (stream.Length > MaxPeLength) return StandaloneGameSupport.Unsupported;
                if (Hex(hash.ComputeHash(stream)) == expected.sha256) return StandaloneGameSupport.Exact;
                if (expected.compatibilitySha256 != null && CompatibilityHash(stream) == expected.compatibilitySha256)
                    return StandaloneGameSupport.Equivalent;
                return StandaloneGameSupport.Unsupported;
            }
        }

        private static string Hex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();

        // Same identity as standalone/pe-identity.h and the profile generator.
        internal static string CompatibilityHash(Stream stream)
        {
            long length = stream.Length;
            if (length < 64 || length > MaxPeLength) return null;
            using (var read = new BinaryReader(stream, System.Text.Encoding.UTF8, true)) {
                ushort U16(long offset) { stream.Position = offset; return read.ReadUInt16(); }
                uint U32(long offset) { stream.Position = offset; return read.ReadUInt32(); }
                if (U16(0) != 0x5a4d) return null;
                long pe = U32(60);
                if (pe < 64 || pe > length - 24 || U32(pe) != 0x4550 || U16(pe + 4) != 0x8664) return null;
                long optional = pe + 24;
                int optionalSize = U16(pe + 20), count = U16(pe + 6);
                if (optionalSize < 152 || optionalSize > length - optional || count < 1 || count > 96 ||
                    U16(optional) != 0x20b || U32(optional + 108) < 5) return null;
                long table = optional + optionalSize, headers = U32(optional + 60), mappedEnd = headers;
                if (headers < table || headers > length || count > (headers - table) / 40) return null;
                for (int i = 0; i < count; i++) {
                    long size = U32(table + i * 40 + 16), offset = U32(table + i * 40 + 20);
                    if (size == 0) continue;
                    if (offset < headers || offset > length || size > length - offset) return null;
                    mappedEnd = Math.Max(mappedEnd, offset + size);
                }

                long security = optional + 144, certificate = U32(security), certificateSize = U32(security + 4);
                if (certificate != 0 || certificateSize != 0) {
                    if (certificate < mappedEnd || certificate > length || (certificate & 7) != 0 ||
                        certificateSize < 8 || certificateSize != length - certificate) return null;
                    for (long cursor = certificate; cursor < length;) {
                        long remaining = length - cursor;
                        if (remaining < 8) return null;
                        long record = U32(cursor);
                        if (record <= 8 || record > remaining || U16(cursor + 4) != 0x200 || U16(cursor + 6) != 2) return null;
                        long aligned = (record + 7) & ~7L;
                        if (aligned > remaining) return null;
                        stream.Position = cursor + record;
                        for (long i = record; i < aligned; i++) if (read.ReadByte() != 0) return null;
                        cursor += aligned;
                    }
                }

                using (var hash = SHA256.Create())
                using (var output = new CryptoStream(Stream.Null, hash, CryptoStreamMode.Write)) {
                    var buffer = new byte[65536];
                    void WriteRange(long start, long end)
                    {
                        stream.Position = start;
                        while (start < end) {
                            int countRead = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, end - start));
                            if (countRead == 0) throw new EndOfStreamException();
                            output.Write(buffer, 0, countRead);
                            start += countRead;
                        }
                    }
                    long checksum = optional + 64;
                    WriteRange(0, checksum);
                    WriteRange(checksum + 4, security);
                    WriteRange(security + 8, certificate == 0 ? length : certificate);
                    output.FlushFinalBlock();
                    return Hex(hash.Hash);
                }
            }
        }
    }
}
