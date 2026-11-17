using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace UmapyonPresence
{
    internal static class GameInstallationDiscovery
    {
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder path, ref uint size);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
        internal static bool Valid(string directory)
        {
            try { return !string.IsNullOrWhiteSpace(directory) && File.Exists(Path.Combine(directory, "umamusume.exe")) && File.Exists(Path.Combine(directory, "UnityPlayer.dll")) && File.Exists(Path.Combine(directory, "GameAssembly.dll")) && Directory.Exists(Path.Combine(directory, "umamusume_Data")); } catch { return false; }
        }
        internal static string ProcessPath(int pid)
        {
            IntPtr handle = OpenProcess(0x1000, false, pid); if (handle == IntPtr.Zero) return null;
            try { var path = new StringBuilder(32768); uint size = (uint)path.Capacity; return QueryFullProcessImageName(handle, 0, path, ref size) ? path.ToString() : null; } finally { CloseHandle(handle); }
        }
        internal static bool IsGameRunning(string directory)
        {
            if (!Valid(directory)) return false;
            string expected = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var knownNames = new HashSet<string>(new[] { "umamusume", "cygames.gallop" }, StringComparer.OrdinalIgnoreCase);
            Process[] processes;
            try { processes = Process.GetProcesses(); }
            catch (Exception ex) { throw new IOException("Windows could not verify whether the game is closed. No loader files were changed.", ex); }
            foreach (var process in processes) using (process) {
                string name;
                try { name = process.ProcessName; } catch { continue; }
                string path = ProcessPath(process.Id);
                if (!string.IsNullOrEmpty(path)) {
                    string executableDirectory = Path.GetDirectoryName(Path.GetFullPath(path));
                    if (string.Equals(executableDirectory == null ? "" : executableDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), expected, StringComparison.OrdinalIgnoreCase) && string.Equals(Path.GetFileName(path), "umamusume.exe", StringComparison.OrdinalIgnoreCase)) return true;
                } else if (knownNames.Contains(name)) {
                    // If Windows cannot reveal the path of a known game process,
                    // conservatively block loader changes rather than guess.
                    return true;
                }
            }
            return false;
        }
        internal static string[] Find(Settings settings)
        {
            var candidates = new List<string>();
            foreach (var process in Process.GetProcessesByName("umamusume")) using (process) { string path = ProcessPath(process.Id); if (!string.IsNullOrEmpty(path)) candidates.Add(Path.GetDirectoryName(path)); }
            candidates.Add(settings.GameDirectory);
            foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine }) {
                try { using (var key = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths\umamusume.exe")) { string path = key == null ? null : key.GetValue("") as string; if (!string.IsNullOrWhiteSpace(path)) candidates.Add(Path.GetDirectoryName(path.Trim('"'))); } } catch { }
                try { using (var uninstall = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall")) if (uninstall != null) foreach (string name in uninstall.GetSubKeyNames()) using (var entry = uninstall.OpenSubKey(name)) {
                    string display = entry == null ? null : entry.GetValue("DisplayName") as string;
                    if (display != null && (display.IndexOf("umamusume", StringComparison.OrdinalIgnoreCase) >= 0 || display.Contains("ウマ娘"))) candidates.Add(entry.GetValue("InstallLocation") as string);
                } } catch { }
            }
            var known = ValidPaths(candidates);
            if (known.Length != 0) return known;

            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(profile)) return known;
            foreach (string root in new[] { Path.Combine(profile, "Downloads"), Path.Combine(profile, "DMM Games") })
                FindNearby(root, candidates);
            return ValidPaths(candidates);
        }

        private static string[] ValidPaths(IEnumerable<string> paths)
        {
            return paths.Where(Valid)
                .Select(path => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        private static void FindNearby(string root, List<string> candidates)
        {
            // Look for custom DMM destinations one or two folders below common roots.
            foreach (string child in ChildFolders(root)) {
                if (Valid(child)) { candidates.Add(child); continue; }
                candidates.AddRange(ChildFolders(child).Where(Valid));
            }
        }

        private static string[] ChildFolders(string path)
        {
            try {
                if (!Directory.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return new string[0];
                return Directory.EnumerateDirectories(path).Where(child => {
                    try { return (File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0; }
                    catch (IOException) { return false; }
                    catch (UnauthorizedAccessException) { return false; }
                }).ToArray();
            } catch (IOException) { return new string[0]; }
            catch (UnauthorizedAccessException) { return new string[0]; }
        }
    }
}
