using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace UmapyonPresence
{
    internal static class HardeningTests
    {
        internal static void Run(Action<bool, string> check)
        {
            string temp = Path.Combine(Path.GetTempPath(), "umapresence-hardening-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            try
            {
                string path = Path.Combine(temp, "settings.json");
                var settings = Settings.Load(path);
                Directory.CreateDirectory(Path.Combine(temp, "umamusume_Data"));
                foreach (string gameFile in new[] { "umamusume.exe", "UnityPlayer.dll", "GameAssembly.dll" })
                    File.WriteAllText(Path.Combine(temp, gameFile), "");
                settings.GameDirectory = temp;
                settings.Save(path);
                settings.ManualCharacter = "New setting";
                settings.Save(path);
                File.WriteAllText(path, "{ broken");
                var recovered = Settings.Load(path);
                check(recovered.RecoveredSettings && recovered.GameDirectory == temp &&
                    string.IsNullOrEmpty(recovered.ManualCharacter) && File.ReadAllText(path + ".corrupt") == "{ broken",
                    "Corrupt settings recover from the last known good copy");
                check(Settings.Load(path).GameDirectory == temp, "Recovered settings survive a second load");
                File.Delete(path + ".previous");
                File.WriteAllText(path, "{ broken again");
                var unconfirmed = Settings.Load(path);
                check(unconfirmed.RequiresFolderConfirmation, "Unrecoverable settings require folder confirmation");
                bool saveBlocked = false;
                try { unconfirmed.Save(path); } catch (InvalidOperationException) { saveBlocked = true; }
                check(saveBlocked && File.ReadAllText(path) == "{ broken again", "Unconfirmed settings leave the original file intact");

                using (var response = new MemoryStream(new byte[] { (byte)'u', (byte)'m', (byte)'a' }))
                    check(ArtworkDownload.ReadText(response, 3, CancellationToken.None).GetAwaiter().GetResult() == "uma", "Artwork response at the byte limit is accepted");
                using (var oversized = new MemoryStream(new byte[4]))
                {
                    bool rejected = false;
                    try { ArtworkDownload.ReadText(oversized, 3, CancellationToken.None).GetAwaiter().GetResult(); }
                    catch (IOException) { rejected = true; }
                    check(rejected, "Oversized artwork response is rejected");
                }
                using (var response = new MemoryStream(new byte[] { (byte)'s', (byte)'h', (byte)'o', (byte)'w', 10 }))
                    check(PresenceApp.ReadControlCommand(response).GetAwaiter().GetResult() == "show", "Complete local command parses");
                using (var response = new MemoryStream(new byte[] { (byte)'s', (byte)'t', (byte)'o', (byte)'p' }))
                    check(PresenceApp.ReadControlCommand(response).GetAwaiter().GetResult() == null, "Incomplete local command is rejected");
                using (var response = new PendingRead())
                    check(PresenceApp.ReadControlCommand(response, 50).GetAwaiter().GetResult() == null, "Incomplete client cannot hold the command read indefinitely");

                string playtimeFile = Path.Combine(temp, "playtime.json");
                File.WriteAllText(playtimeFile, "7260");
                var playtime = new Playtime(playtimeFile);
                check(playtime.Label == "2 h 01 min played", "Stored playtime displays total hours and minutes");
                string activity = Json.Encode(playtime.AddToActivity(new { details = "Training" }));
                check(activity.Contains("2 h 01 min played") && activity.Contains("Training"), "Playtime tooltip preserves activity details");
            }
            finally { Directory.Delete(temp, true); }
        }

        private sealed class PendingRead : MemoryStream
        {
            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                return new TaskCompletionSource<int>().Task;
            }
        }
    }
}
