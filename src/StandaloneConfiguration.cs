using System;
using System.Threading.Tasks;
using System.IO;
using System.Collections.Generic;

namespace UmapyonPresence
{
    internal sealed class StandaloneConfiguration
    {
        internal static bool ImportHachimiPreferences(Settings settings)
        {
            if (settings.StandaloneConcertPreferencesImported) return false;
            string path = Path.Combine(settings.GameDirectory ?? "", "hachimi", "config.json");
            if (File.Exists(path)) {
                var config = Json.Decode<Dictionary<string, object>>(File.ReadAllText(path)); object value;
                if (config == null) throw new InvalidDataException("Hachimi preference file is malformed; it was left untouched.");
                if (config.TryGetValue("live_theater_allow_same_chara", out value) && value is bool) settings.ConcertAllowDuplicates = (bool)value;
            }
            settings.StandaloneConcertPreferencesImported = true; return true;
        }
        private string acknowledged, attempted;
        private bool busy;
        private DateTime retry;
        internal string Status { get; private set; } = "No standalone runtime";
        internal static uint Flags(Settings settings, RuntimeCapability capabilities)
        {
            uint flags = settings.ConcertAllowDuplicates && (capabilities & RuntimeCapability.Formation) != 0 ? 1u : 0u;
            if (!ReleasePolicy.ExperimentalAudio) return flags;
            if ((capabilities & RuntimeCapability.Mix) != 0) {
                if (settings.ConcertAudioMode == "Balanced") flags |= 2;
                else if (settings.ConcertAudioMode == "Lead") flags |= 6;
            }
            if ((capabilities & RuntimeCapability.Routing) != 0) { if (settings.ConcertAudioMode == "Solo") flags |= 8; else if (settings.ConcertAudioMode == "Duet") flags |= 16; }
            return flags;
        }
        internal static string Missing(Settings settings, RuntimeCapability capabilities)
        {
            var missing=new List<string>();
            if(settings.ConcertAllowDuplicates&&(capabilities&RuntimeCapability.Formation)==0)missing.Add("duplicate casting");
            if (!ReleasePolicy.ExperimentalAudio) {
                if (settings.ConcertAudioMode != "Original") missing.Add("experimental audio (disabled in this prerelease)");
                return string.Join(", ", missing);
            }
            if((settings.ConcertAudioMode=="Balanced"||settings.ConcertAudioMode=="Lead")&&(capabilities&RuntimeCapability.Mix)==0)missing.Add("vocal balancing");
            if((settings.ConcertAudioMode=="Solo"||settings.ConcertAudioMode=="Duet")&&(capabilities&RuntimeCapability.Routing)==0)missing.Add("singer arrangements");
            return string.Join(", ",missing);
        }
        internal async Task Observe(Settings settings, int pid)
        {
            var runtime = StandaloneRuntime.Read(pid);
            if (runtime == null || !runtime.Ready) { acknowledged = null; Status = "No exclusive standalone configuration owner"; return; }
            uint flags = Flags(settings, runtime.Capabilities);
            string identity = pid + ":" + runtime.Nonce + ":" + flags + ":" + settings.ConcertVoice1 + ":" + settings.ConcertVoice2 + ":" + settings.ConcertAudioMode + ":" + settings.ConcertAllowDuplicates;
            if (busy || identity == acknowledged || (identity == attempted && DateTime.UtcNow < retry)) return;
            busy = true; attempted = identity; retry = DateTime.UtcNow.AddSeconds(5);
            try {
                int result = await StandaloneRuntime.Send(pid, 6, 0, 0, flags,
                    ReleasePolicy.ExperimentalAudio ? (uint)settings.ConcertVoice1 : 0u,
                    ReleasePolicy.ExperimentalAudio ? (uint)settings.ConcertVoice2 : 0u);
                if (result == 0) acknowledged = identity;
                string missing=Missing(settings,runtime.Capabilities);
                Status = result == 0 ? (missing.Length==0?"Runtime accepted the preferences; live verification is pending.":"Saved choices unavailable in this runtime: "+missing+". Other supported preferences were accepted.") : "Configuration rejected: " + result;
            } catch (Exception ex) { Status = ex.Message; } finally { busy = false; }
        }
    }
}
