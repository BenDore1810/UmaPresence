using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace UmapyonPresence
{
    internal static class LoaderHelper
    {
        internal const string Url = "https://github.com/Hachimi-Hachimi/Cellar/releases/download/v2.0.0/cellar.dll";
        internal const string Sha256 = "f97609628ba41f32c7ffc8674894f2eea100a6bcf10f560da4fbd57b7a2a82cb";
        internal static string FilePath { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dependencies", "cellar-v2.0.0.dll"); } }

        internal static void Verify(byte[] data)
        {
            if (data == null || data.Length != 198656 || data[0] != 'M' || data[1] != 'Z')
                throw new InvalidDataException("The selected file is not the supported Cellar release.");
            int pe = BitConverter.ToInt32(data, 60);
            if (pe < 64 || pe > data.Length - 26 || BitConverter.ToUInt32(data, pe) != 0x4550 ||
                BitConverter.ToUInt16(data, pe + 4) != 0x8664 || BitConverter.ToUInt16(data, pe + 24) != 0x20b)
                throw new InvalidDataException("Cellar must be a Windows x64 DLL.");
            using (var hash = SHA256.Create()) {
                string actual = BitConverter.ToString(hash.ComputeHash(data)).Replace("-", "").ToLowerInvariant();
                if (actual != Sha256) throw new InvalidDataException("Cellar checksum does not match the pinned official release.");
            }
        }

        internal static string Require()
        {
            if (!File.Exists(FilePath)) throw new FileNotFoundException("Download the official loader helper or select its verified file under Game integration first.");
            Verify(File.ReadAllBytes(FilePath)); return FilePath;
        }

        internal static void Import(string source)
        {
            var info = new FileInfo(source);
            if (info.Length != 198656) throw new InvalidDataException("Unexpected loader helper size.");
            Store(File.ReadAllBytes(source));
        }

        private static void Store(byte[] bytes)
        {
            Verify(bytes);
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            if (File.Exists(FilePath)) { Require(); return; }
            string temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try {
                File.WriteAllBytes(temporary, bytes);
                try { File.Move(temporary, FilePath); }
                catch (IOException) { if (!File.Exists(FilePath)) throw; Require(); }
            } finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        internal static async Task Download()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            using (var web = new WebClient()) {
                web.Headers[HttpRequestHeader.UserAgent] = "UmaPresence/" + ReleasePolicy.Version;
                using (var stream = await web.OpenReadTaskAsync(Url))
                using (var output = new MemoryStream()) {
                    var buffer = new byte[16384]; int count;
                    while ((count = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0) {
                        if (output.Length + count > 198656) throw new InvalidDataException("Official download exceeds the pinned file size.");
                        output.Write(buffer, 0, count);
                    }
                    Store(output.ToArray());
                }
            }
        }
    }
}
