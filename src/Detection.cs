using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace UmapyonPresence
{
    internal static class Native
    {
        [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X, Y; }
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern bool GetClientRect(IntPtr hwnd, out Rect rect);
        [DllImport("user32.dll")] internal static extern bool ClientToScreen(IntPtr hwnd, ref Point point);
        [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
        [DllImport("user32.dll")] internal static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] internal static extern bool ShowWindow(IntPtr hwnd, int command);
        [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr hwnd);
        internal delegate bool EnumWindowsCallback(IntPtr window, IntPtr parameter);
        [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);
    }

    internal sealed class GameSession
    {
        public int ProcessId;
        public IntPtr Window;
        public long Started;
        public string Path;
        internal static bool HachimiRpcEnabled(string gamePath)
        {
            try
            {
                string path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(gamePath), "hachimi", "config.json");
                if (!File.Exists(path)) return false;
                var config = Json.Decode<Dictionary<string, object>>(File.ReadAllText(path));
                object enabled;
                return config.TryGetValue("discord_rpc", out enabled) && enabled is bool && (bool)enabled;
            }
            catch { return false; }
        }
        public static GameSession Find(Settings settings)
        {
            foreach (string processName in settings.ProcessNames)
            {
                foreach (var process in Process.GetProcessesByName(processName))
                {
                    using (process)
                    {
                        try
                        {
                            // DMM's protected process denies the SYNCHRONIZE access requested
                            // by .NET Framework Process.HasExited. Its presence in the process
                            // snapshot is enough; optional metadata must never reject it.
                            return ReadCandidate(process.Id, () => FindWindow(process.Id),
                                () => process.StartTime, () => GameInstallationDiscovery.ProcessPath(process.Id) ?? process.MainModule.FileName);
                        }
                        catch (InvalidOperationException) { }
                        catch (System.ComponentModel.Win32Exception) { }
                    }
                }
            }
            return null;
        }
        private static IntPtr FindWindow(int pid)
        {
            IntPtr result = IntPtr.Zero; long area = 0;
            Native.EnumWindows((window, ignored) => { uint owner; Native.GetWindowThreadProcessId(window, out owner); Native.Rect rect;
                if (owner == pid && Native.IsWindowVisible(window) && Native.GetClientRect(window, out rect)) { long candidate = (long)(rect.Right - rect.Left) * (rect.Bottom - rect.Top); if (candidate > area) { result = window; area = candidate; } }
                return true;
            }, IntPtr.Zero);return result;
        }
        internal static GameSession ReadCandidate(int processId, Func<IntPtr> readWindow, Func<DateTime> readStart, Func<string> readPath)
        {
            var session = new GameSession { ProcessId = processId, Started = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), Path = "Unavailable (process permissions)" };
            try { session.Window = readWindow(); } catch (Exception ex) when (ex is InvalidOperationException || ex is System.ComponentModel.Win32Exception) { }
            try { session.Started = new DateTimeOffset(readStart().ToUniversalTime()).ToUnixTimeSeconds(); } catch (Exception ex) when (ex is InvalidOperationException || ex is System.ComponentModel.Win32Exception) { }
            try { session.Path = readPath(); } catch (Exception ex) when (ex is InvalidOperationException || ex is System.ComponentModel.Win32Exception) { }
            return session;
        }
        public bool IsForeground
        {
            get
            {
                uint pid;
                Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out pid);
                return pid == ProcessId && Window != IntPtr.Zero && Native.IsWindowVisible(Window) && !Native.IsIconic(Window);
            }
        }
        public Bitmap Capture()
        {
            if (!IsForeground) return null;
            Native.Rect client;
            if (!Native.GetClientRect(Window, out client)) return null;
            var origin = new Native.Point();
            if (!Native.ClientToScreen(Window, ref origin)) return null;
            var region = new Rectangle(origin.X, origin.Y, client.Right - client.Left, client.Bottom - client.Top);
            if (region.Width < 100 || region.Height < 100 || region.Width > 7680 || region.Height > 7680) return null;
            if (!System.Windows.Forms.SystemInformation.VirtualScreen.Contains(region)) return null;
            var bitmap = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb);
            try
            {
                // Read visible client pixels only; no hooks, injected code, or process-memory access.
                using (var graphics = Graphics.FromImage(bitmap)) graphics.CopyFromScreen(region.Location, Point.Empty, region.Size, CopyPixelOperation.SourceCopy);
                if (!IsForeground) { bitmap.Dispose(); return null; }
                return bitmap;
            }
            catch { bitmap.Dispose(); return null; }
        }
    }

    internal sealed class ScreenReader
    {
        private OcrEngine engine;
        public string Status { get; private set; } = "Not initialized";
        private bool initialized;
        public void Initialize(string language)
        {
            if (initialized) return;
            initialized = true;
            try
            {
                if (!string.IsNullOrEmpty(language) && language != "auto") engine = OcrEngine.TryCreateFromLanguage(new Language(language));
                else
                {
                    // Hachimi typically displays English; Japanese is the second choice.
                    engine = OcrEngine.TryCreateFromLanguage(new Language("en-US")) ?? OcrEngine.TryCreateFromLanguage(new Language("ja-JP")) ?? OcrEngine.TryCreateFromUserProfileLanguages();
                }
                Status = engine == null ? "No supported Windows OCR language installed; navigation detection still works" : "Windows OCR: " + engine.RecognizerLanguage.LanguageTag;
            }
            catch (Exception ex) { Status = "Windows OCR unavailable: " + ex.Message; }
            Log.Write(Status);
        }
        public async Task<string> Read(Bitmap source, string language)
        {
            Initialize(language);
            if (engine == null) return "";
            int max = Math.Min(1400, (int)OcrEngine.MaxImageDimension);
            // Reduce large game windows before recognition to cap memory and CPU use.
            double scale = Math.Min(1.0, (double)max / Math.Max(source.Width, source.Height));
            using (var resized = new Bitmap(Math.Max(1, (int)(source.Width * scale)), Math.Max(1, (int)(source.Height * scale)), PixelFormat.Format32bppArgb))
            {
                using (var graphics = Graphics.FromImage(resized))
                {
                    graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    graphics.DrawImage(source, 0, 0, resized.Width, resized.Height);
                }
                using (var stream = new InMemoryRandomAccessStream())
                {
                    using (var output = stream.AsStreamForWrite())
                    {
                        resized.Save(output, ImageFormat.Bmp);
                        output.Flush();
                        stream.Seek(0);
                        var decoder = await BitmapDecoder.CreateAsync(stream);
                        using (var softwareBitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore))
                        {
                            var result = await engine.RecognizeAsync(softwareBitmap);
                            // Exclude the bottom menu: labels there appear on every home tab.
                            return string.Join("\n", result.Lines.Where(line => line.Words.Count > 0 && line.Words.Min(word => word.BoundingRect.Y) < resized.Height * 0.91).Select(line => line.Text));
                        }
                    }
                }
            }
        }
    }

    internal static class PixelClassifier
    {
        private static readonly double[] xs = { .176, .328, .434, .666, .832 };
        private static readonly double[] ys = { .978, .981, .980, .979, .979 };
        private static readonly Color[] colors = { Color.FromArgb(254, 82, 138), Color.FromArgb(255, 133, 41), Color.FromArgb(33, 162, 247), Color.FromArgb(64, 199, 8), Color.FromArgb(231, 63, 200) };
        private static readonly string[] labels = { "Upgrade", "Story", "Home", "Race", "Gacha" };
        public static ActivityState Classify(Bitmap bitmap)
        {
            if (bitmap.Width > bitmap.Height) return null;
            int active = -1;
            for (int i = 0; i < xs.Length; i++)
            {
                var pixel = bitmap.GetPixel((int)((bitmap.Width - 1) * xs[i]), (int)((bitmap.Height - 1) * ys[i]));
                if (Similar(pixel, colors[i], 42)) { if (active >= 0) return null; active = i; }
                else if (!Similar(pixel, Color.FromArgb(226, 223, 231), 38)) return null;
            }
            return active < 0 ? null : new ActivityState { Kind = labels[active], Details = "Main Menu", State = labels[active], Source = "Navigation pixels" };
        }
        private static bool Similar(Color a, Color b, int tolerance) { return Math.Abs(a.R - b.R) <= tolerance && Math.Abs(a.G - b.G) <= tolerance && Math.Abs(a.B - b.B) <= tolerance; }
        internal static void PaintTestMenu(Bitmap bitmap, int active)
        {
            for (int i = 0; i < xs.Length; i++) bitmap.SetPixel((int)((bitmap.Width - 1) * xs[i]), (int)((bitmap.Height - 1) * ys[i]), i == active ? colors[i] : Color.FromArgb(226, 223, 231));
        }
    }

    internal static class TextClassifier
    {
        public static string Normalize(string text) { return Regex.Replace((text ?? "").ToLowerInvariant().Normalize(), @"[^\p{L}\p{N}]", ""); }
        private static bool Has(string value, params string[] tokens) { return tokens.Any(token => value.Contains(Normalize(token))); }
        public static ActivityState Classify(string text)
        {
            string value = Normalize(text);
            if (value.Length < 6) return null;
            ActivityState state = null;
            int stats = new[] { new[] { "speed", "スピード" }, new[] { "stamina", "スタミナ" }, new[] { "power", "パワー" }, new[] { "guts", "根性" }, new[] { "wisdom", "wit", "賢さ" } }.Count(group => Has(value, group));
            int aptitudes = new[] { new[] { "turf", "芝" }, new[] { "dirt", "ダート" }, new[] { "sprint", "短距離" }, new[] { "mile", "マイル" }, new[] { "medium", "中距離" }, new[] { "long", "長距離" } }.Count(group => Has(value, group));
            int styles = new[] { new[] { "runner", "逃げ" }, new[] { "leader", "先行" }, new[] { "betweener", "差し" }, new[] { "chaser", "追込" } }.Count(group => Has(value, group));
            bool careerPanel = stats >= 2 && aptitudes >= 3 && styles >= 2 && Has(value, "skill pt", "skill points", "スキルpt", "スキルポイント");
            if (careerPanel || (stats >= 3 && Has(value, "energy", "mood", "skill pt", "skill points", "next goal", "rest", "体力", "やる気", "次の目標", "お休み")))
            {
                state = new ActivityState { Kind = "Training", Details = "Career mode", State = "Raising an Umamusume", Source = "Windows OCR", StrongEvidence = careerPanel };
                // Date patterns are deliberately specific; don't publish arbitrary OCR text.
                var date = Regex.Match(text, @"\b(Junior|Classic|Senior)\s+(?:Year\s+)?(?:[–—:\-]\s*)?(?:(Early|Late)\s+)?(January|February|March|April|May|June|July|August|September|October|November|December)\b", RegexOptions.IgnoreCase);
                if (date.Success) state.State = Regex.Replace(date.Value, @"\s+", " ");
                else if (Has(value, "debut", "デビュー")) state.State = "Preparing for debut";
                var character = CharacterCatalog.FindIn(text);
                if (character != null) { state.Character = character.Name; state.CharacterId = character.Id; }
                var statValues = new List<string>();
                foreach (string stat in new[] { "Speed|スピード", "Stamina|スタミナ", "Power|パワー", "Guts|根性", "Wisdom|Wit|賢さ" })
                {
                    var number = Regex.Match(text, @"(?:\b(?:" + stat + @"))\s*[:：]?\s*(\d{1,4})(?!\d)", RegexOptions.IgnoreCase);
                    if (number.Success && int.Parse(number.Groups[1].Value) <= 9999) statValues.Add(number.Groups[1].Value);
                }
                var skill = Regex.Match(text, @"(?:Skill\s*(?:Points|Pts?)|スキル\s*(?:Pt|ポイント))\s*[:：]?\s*(\d{1,5})(?!\d)", RegexOptions.IgnoreCase);
                if (statValues.Count == 5) state.CareerStats = string.Join(" / ", statValues);
                if (skill.Success) state.CareerStats += (state.CareerStats.Length > 0 ? " | " : "") + "Skill Pt " + skill.Groups[1].Value;
            }
            else if (Has(value, "live theater", "live theatre", "ライブシアター") && Has(value, "song", "music", "ライブ", "楽曲")) state = ActivityState.Manual("Live");
            else if (Has(value, "race results", "race result", "race replay", "レース結果", "レースリプレイ")) state = ActivityState.Manual("Race");
            else if (Has(value, "main story", "extra stories", "character stories", "メインストーリー", "ウマ娘ストーリー")) state = ActivityState.Manual("Story");
            else if (Has(value, "pretty derby scout", "support card scout", "ピックアッププリティーダービーガチャ", "サポートカードガチャ")) state = ActivityState.Manual("Gacha");
            else if (Has(value, "team trials", "champions meeting", "チーム競技場", "チャンピオンズミーティング")) state = new ActivityState { Kind = "Race", Details = "Main Menu", State = "Race" };
            if (state != null) state.Source = "Windows OCR";
            return state;
        }
    }

    internal sealed class ActivityDetector
    {
        private ActivityState current = new ActivityState();
        private DateTime lastEvidence;
        private string candidate;
        private int confirmations;
        public void Reset() { current = new ActivityState(); lastEvidence = DateTime.MinValue; candidate = null; confirmations = 0; }
        public ActivityState Observe(ActivityState observation, DateTime now, bool observable)
        {
            // A minimized/background game retains the last known activity. Unknown visible
            // screens expire so training isn't shown indefinitely after returning to a menu.
            if (observation == null)
            {
                candidate = null; confirmations = 0;
                if (observable && now - lastEvidence > TimeSpan.FromSeconds(32)) current = new ActivityState();
                return current;
            }
            string key = observation.Kind + "|" + observation.State + "|" + observation.Character;
            if (candidate == key) confirmations++; else { candidate = key; confirmations = 1; }
            bool confirmed = observation.Source == "Navigation pixels" || observation.StrongEvidence || confirmations >= 2;
            if (confirmed) { current = observation; lastEvidence = now; }
            return current;
        }
    }
}
