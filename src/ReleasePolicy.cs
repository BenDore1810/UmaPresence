using System.Reflection;

[assembly: AssemblyVersion("0.1.0.0")]
[assembly: AssemblyFileVersion("0.1.0.2")]
[assembly: AssemblyInformationalVersion("0.1.0-beta.2")]

namespace UmapyonPresence
{
    internal static class ReleasePolicy
    {
        internal const string Version = "0.1.0-beta.2";
#if PUBLIC_RELEASE
        internal static readonly bool PublicRelease = true;
#else
        internal static readonly bool PublicRelease = false;
#endif
#if PISA_DEV
        internal static readonly bool PisaDev = true;
#else
        internal static readonly bool PisaDev = false;
#endif
        internal static bool ExperimentalAudio { get { return PisaDev || !PublicRelease; } }
        internal static string StandaloneUpdateChannel => PisaDev ? Version + "-pisa" : Version;
    }
}
