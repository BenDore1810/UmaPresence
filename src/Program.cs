using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace UmapyonPresence
{
    internal static class Program
    {
        internal static readonly string ControlPipe = "UmapyonPresence-" + WindowsIdentity.GetCurrent().User.Value;
        internal static void SchedulePortableRemoval(Settings settings)
        {
            using (var cleanup = StartPortableRemoval(settings, Application.ExecutablePath, Process.GetCurrentProcess().Id)) { }
        }

        internal static Process StartPortableRemoval(Settings settings, string executable, int pid)
        {
            string root = Path.GetDirectoryName(Path.GetFullPath(executable));
            string manifest = Path.Combine(root, "checksums.json");
            var hashes = Json.Decode<System.Collections.Generic.Dictionary<string, string>>(File.ReadAllText(manifest));
            if (!hashes.ContainsKey("UmaPresence.exe") || !hashes.ContainsKey("settings.json")) throw new IOException("Extract the complete UmaPresence ZIP into its own folder first.");
            var files = new[] {
                "UmaPresence.exe", "UmaPresence.exe.config", "settings.json", "checksums.json",
                "LICENSE", "MinHook-LICENSE.txt", "README.md", "BUILD.md", "NOTICES.md", "RELEASE-NOTES.md", "CHECKLIST.md",
                WelcomeDialog.WelcomeSheet, WelcomeDialog.GoodbyeSheet, WelcomeDialog.LocalArtworkMarker,
                "acceptance.json", "release-manifest.json", "verification/native-policy-check.json",
                "standalone/UnityPlayer.dll", "standalone/umapyon_bridge.dll", "standalone/game-profile.json", "standalone/loader-provenance.json",
                "presence.log", "presence.log.old", "status.json", "release-smoke.json", "dependencies/cellar-v2.0.0.dll",
                "outfit-images.cache.json", "original-cover-cache.json", "maintenance-history.json",
                "diagnostic.txt", "live-diagnostic.txt", "race-paddock-diagnostics.json", "integration-replace.txt", "integration-prepare.txt"
            };
            foreach (string relative in files) {
                if (Path.IsPathRooted(relative) || relative.Contains(':') || relative.Split('/', '\\').Any(part => part == ".." || part == "." || part.Length == 0)) throw new IOException("Invalid package file path.");
                string path = Path.Combine(root, relative);
                while (!string.Equals(path, root, StringComparison.OrdinalIgnoreCase)) {
                    if ((File.Exists(path) || Directory.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Uninstall cannot move files through a linked path.");
                    path = Path.GetDirectoryName(path);
                }
            }
            if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0 || IntegrationProfiles.Hash(executable) != hashes["UmaPresence.exe"]) throw new IOException("The extracted app could not be verified.");
            if (!string.IsNullOrWhiteSpace(settings.GameDirectory)) IntegrationProfiles.UninstallCurrent(settings, fullRemoval: true);
            const string startupKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
            const string startupName = "UmapyonPresence";
            string startupCommand = "\"" + executable + "\"";
            using (var key = Registry.CurrentUser.OpenSubKey(startupKey)) {
                if (string.Equals(key?.GetValue(startupName) as string, startupCommand, StringComparison.OrdinalIgnoreCase)) {
                    using (var writable = Registry.CurrentUser.OpenSubKey(startupKey, true))
                        if (string.Equals(writable?.GetValue(startupName) as string, startupCommand, StringComparison.OrdinalIgnoreCase)) writable.DeleteValue(startupName, false);
                }
            }

            string Quote(string value) { return "'" + value.Replace("'", "''") + "'"; }
            string script = "$ErrorActionPreference='Stop'\n$source=" + Quote(root) +
                "\n$files=@(" + string.Join(",", files.Select(Quote)) + ")\n" +
                "try {\n" + (pid > 0 ? "$app=Get-Process -Id " + pid + " -ErrorAction SilentlyContinue\nif($app){Wait-Process -InputObject $app -Timeout 30}\n" : "") +
                "function Check-Link($path){while($path -and $path.StartsWith($source,[StringComparison]::OrdinalIgnoreCase)){if((Test-Path -LiteralPath $path) -and ((Get-Item -LiteralPath $path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Uninstall found a linked path'}; $path=[IO.Path]::GetDirectoryName($path)}}\n" +
                "foreach($file in $files){$path=Join-Path $source $file;Check-Link $path;if(Test-Path -LiteralPath $path -PathType Leaf){Remove-Item -LiteralPath $path -Force}}\n" +
                "foreach($name in @('generated-artwork','generated-portraits')){$folder=Join-Path $source $name;Check-Link $folder;if(Test-Path -LiteralPath $folder -PathType Container){foreach($file in (Get-ChildItem -LiteralPath $folder -File -Force)){if($file.Name -match '^(concert_[0-9]+|portrait_[0-9]+_[0-9]+_frame_v2)\\.png$'){Check-Link $file.FullName;Remove-Item -LiteralPath $file.FullName -Force}}}}\n" +
                "foreach($name in @('standalone','dependencies','verification','generated-artwork','generated-portraits')){$folder=Join-Path $source $name;Check-Link $folder;if((Test-Path -LiteralPath $folder -PathType Container) -and !@(Get-ChildItem -LiteralPath $folder -Force).Count){[IO.Directory]::Delete($folder)}}\n" +
                "if(!@(Get-ChildItem -LiteralPath $source -Force).Count){[IO.Directory]::Delete($source)}\n" +
                "} catch { Add-Type -AssemblyName System.Windows.Forms; [void][Windows.Forms.MessageBox]::Show($_.Exception.Message,'UmaPresence uninstall'); exit 1 }";
            var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"), "-NoProfile -NonInteractive -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(script))) {
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetTempPath()
            };
            return Process.Start(start) ?? throw new IOException("Could not start portable app removal.");
        }
        [STAThread]
        public static void Main(string[] args)
        {
            Native.SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Contains("--release-self-test")) { Environment.ExitCode = ReleaseSelfCheck.Run(); return; }
            if (args.Contains("--stop")) { SendControl("stop"); return; }
            if (args.Contains("--live-diagnose")) { SendControl("diagnostics"); return; }
            if (args.Contains("--replace-standalone")) {
                string report = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "integration-replace.txt");
                try {
                    var settings = Settings.Load();
                    // Each stage has its own durable recovery journal. If the
                    // replacement fails after removal, the saved Hachimi/original
                    // loader remains available; no game is stopped or launched.
                    IntegrationProfiles.Uninstall(settings); IntegrationProfiles.Prepare(settings); IntegrationProfiles.Apply(settings);
                    File.WriteAllText(report, IntegrationProfiles.AppliedMessage(settings), Encoding.UTF8);
                } catch (Exception ex) { File.WriteAllText(report, ex.Message, Encoding.UTF8); Environment.ExitCode = 1; }
                return;
            }
            if (args.Contains("--prepare-standalone")) {
                string report = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "integration-prepare.txt");
                try { var settings = Settings.Load(); IntegrationProfiles.Prepare(settings); File.WriteAllText(report, IntegrationProfiles.Status(settings, 0), Encoding.UTF8); }
                catch (Exception ex) { File.WriteAllText(report, ex.Message, Encoding.UTF8); Environment.ExitCode = 1; }
                return;
            }
            if (args.Contains("--diagnose"))
            {
                try
                {
                    var settings = Settings.Load();
                    var reader = new ScreenReader();
                    reader.Initialize(settings.OcrLanguage);
                    var game = GameSession.Find(settings);
                    string discordConnection;
                    using (var probe = new DiscordIpc())
                    {
                        try { probe.Connect(settings.ApplicationId).GetAwaiter().GetResult(); discordConnection = "READY acknowledged by Discord (no activity sent)"; }
                        catch (Exception ex) { discordConnection = ex.Message; }
                    }
                    File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "diagnostic.txt"),
                        "UmaPresence " + ReleasePolicy.Version + "\r\nRuntime: " + Environment.Version + "\r\n" + reader.Status +
                        "\r\nBundled characters: " + CharacterCatalog.Entries.Count + "\r\nDiscord: " + discordConnection + "\r\nGame: " + (game == null ? "Not running" : game.Path) +
                        "\r\nBrowser components: none\r\n" + IntegrationProfiles.Status(settings, game == null ? 0 : game.ProcessId) + "\r\n" + StandaloneAudioDiagnostics.Report(game == null ? 0 : game.ProcessId), Encoding.UTF8);
                }
                catch (Exception ex) { Log.Write("Diagnostics failed: " + ex); Environment.ExitCode = 1; }
                return;
            }
            bool first;
            using (var mutex = new Mutex(true, "Local\\UmapyonPresence-v1", out first))
            {
                if (!first) { SendControl(args.Contains("--settings") ? "settings" : "show"); return; }
                try
                {
                    Application.Run(new PresenceApp(!args.Contains("--headless"), args.Contains("--status"), args.Contains("--settings"), args.Contains("--minimized")));
                }
                catch (Exception ex)
                {
                    Log.Write("Startup failed: " + ex);
                    MessageBox.Show(ex.Message + "\r\n\r\nDetails: " + Log.FilePath, "UmaPresence", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
        private static void SendControl(string command)
        {
            try
            {
                using (var pipe = new NamedPipeClientStream(".", ControlPipe, PipeDirection.Out))
                {
                    pipe.Connect(800);
                    byte[] bytes = Encoding.ASCII.GetBytes(command + "\n");
                    pipe.Write(bytes, 0, bytes.Length);
                }
            }
            catch { }
        }
    }

    internal sealed class PresenceApp : ApplicationContext
    {
        private readonly Settings settings;
        private readonly DiscordIpc discord = new DiscordIpc();
        private readonly ScreenReader reader = new ScreenReader();
        private readonly ActivityDetector detector = new ActivityDetector();
        private readonly ConcertReader concertReader = new ConcertReader { EnableInspectionCompatibility = true };
        private readonly ConcertTracker concertTracker = new ConcertTracker();
        private readonly RaceTracker raceTracker = new RaceTracker();
        private string lastPaddockDiagnostics;
        private readonly ConcertArtworkBuilder concertArtwork = new ConcertArtworkBuilder();
        private Task artworkTask;
        private Task portraitLookupTask;
        private Task originalCoverLookupTask;
        private DateTime nextSingerCycleChange;
        private int singerCyclePage;
        private string lastSingerLine;
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 2500 };
        private readonly Control dispatcher = new Control();
        private NotifyIcon tray;
        private Icon appIcon;
        private ToolStripMenuItem pauseItem;
        private bool busy, stopping, paused;
        private int gamePid;
        private long sessionStarted;
        private string gamePath = "Not detected";
        private string mode = "Auto";
        private ActivityState state = new ActivityState();
        private string discordStatus = "Waiting for game";
        private string lastReportedStatus;
        private string lastPayload;
        private DateTime nextScan, nextConnect, nextUpdate;
        private DateTime lastAcknowledged;
        private DateTime lastScreenScan;
        private string lastScreenText = "", screenStatus = "Waiting for game screen";
        private bool gameForeground;
        private readonly ConcertPortraitRotation concertPortraitRotation = new ConcertPortraitRotation();
        private StandaloneMediaControls standaloneMedia;
        private ConcertSeekOverlay concertSeekOverlay;
        private StandaloneRuntimeStatus concertOverlayRuntime;
        private readonly StandaloneConfiguration standaloneConfiguration = new StandaloneConfiguration();
        private NamedPipeServerStream controlServer;
        private readonly object controlLock = new object();
        private readonly int ownPid = Process.GetCurrentProcess().Id;

        private SettingsWindow settingsWindow;
        public PresenceApp(bool showTray, bool showStatus = false, bool showSettings = false, bool startHidden = false)
        {
            settings = Settings.Load();
            if (!GameInstallationDiscovery.Valid(settings.GameDirectory)) { var installations = GameInstallationDiscovery.Find(settings); if (installations.Length == 1) { settings.GameDirectory = installations[0]; settings.Save(); } }
            try { if (StandaloneConfiguration.ImportHachimiPreferences(settings)) { settings.Save(); Log.Write("Standalone concert preferences initialized; existing Hachimi duplicate-casting preference preserved."); } } catch (Exception ex) { Log.Write("Concert preference import: " + ex.Message); }
            if (!File.Exists(Settings.FilePath)) settings.Save();
            dispatcher.CreateControl();
            standaloneMedia = new StandaloneMediaControls(dispatcher);
            concertSeekOverlay = new ConcertSeekOverlay();
            Log.Write("UmaPresence " + ReleasePolicy.Version + " started. Direct Discord IPC; game integration is configured separately.");
            if (showTray) CreateTray();
            timer.Tick += async (s, e) => await Tick();
            timer.Start();
            Task.Run((Action)ControlLoop);
            if (showStatus) dispatcher.BeginInvoke((Action)ShowStatus);
            if (showSettings || (showTray && !showStatus && !startHidden && !settings.StartMinimized)) dispatcher.BeginInvoke((Action)(() => {
                ShowSettings();
                if (!ShouldShowWelcome(settings, showTray, showStatus, startHidden)) return;
                bool setup = WelcomeDialog.ShowWelcome(settingsWindow, settings.GameDirectory);
                settings.WelcomeDismissed = true;
                try { settings.Save(); }
                catch (Exception error) { settingsWindow.Saved("Could not remember your welcome choice. " + error.Message); }
                settingsWindow.Navigate(setup ? "Game integration" : "Overview");
            }));
        }

        internal static bool ShouldShowWelcome(Settings settings, bool showTray, bool showStatus, bool startHidden)
        {
            return showTray && !showStatus && !startHidden && !settings.StartMinimized && !settings.WelcomeDismissed;
        }

        private void CreateTray()
        {
            if (tray != null) { tray.Visible = true; return; }
            appIcon = MakeIcon();
            var menu = new ContextMenuStrip();
            menu.Items.Add("Open UmaPresence", null, (s, e) => ShowSettings());
            pauseItem = new ToolStripMenuItem("Pause presence") { CheckOnClick = true, Checked = paused };
            pauseItem.Click += (s, e) => { paused = pauseItem.Checked; UpdateStatus(); };
            menu.Items.Add(pauseItem);
            menu.Items.Add("Help / tutorial", null, (s, e) => { ShowSettings(); settingsWindow.Navigate("Help & diagnostics"); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Quit", null, async (s, e) => await Stop());
            tray = new NotifyIcon { Icon = appIcon, Text = "UmaPresence • waiting for game", ContextMenuStrip = menu, Visible = true };
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowSettings(); };
        }
        private void SetActivity(string selected)
        {
            mode = selected; nextScan = DateTime.MinValue;
            if (mode == "Auto") { detector.Reset(); state = new ActivityState(); }
            nextUpdate = DateTime.MinValue; lastPayload = null;
        }
        private void SetStartup(bool enabled)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) {
                if (enabled) key.SetValue("UmapyonPresence", "\"" + Application.ExecutablePath + "\"");
                else key.DeleteValue("UmapyonPresence", false);
            }
        }

        private async Task Tick()
        {
            if (busy || stopping) return;
            busy = true;
            try
            {
                var game = GameSession.Find(settings);
                var now = DateTime.UtcNow;
                bool foreground = game != null && game.IsForeground;
                if (foreground && !gameForeground) nextScan = DateTime.MinValue;
                gameForeground = foreground;
                if (game == null || paused)
                {
                    standaloneMedia.Dispose();
                    concertSeekOverlay.HideControls();
                    concertOverlayRuntime = null;
                    if (lastPayload != null && discord.IsConnected)
                    {
                        try { await discord.SetActivity(null, ownPid); } catch { }
                    }
                    discord.Dispose(); lastPayload = null;
                    discordStatus = paused ? "Paused" : "Waiting for game";
                    if (game == null && gamePid != 0)
                    {
                        Log.Write("Game closed. Presence cleared.");
                        gamePid = 0; gamePath = "Not detected"; detector.Reset(); concertReader.Dispose(); concertTracker.Reset(); raceTracker.Reset(); state = new ActivityState(); mode = "Auto";
                    }
                    UpdateStatus();
                    return;
                }
                if (game.ProcessId != gamePid)
                {
                    gamePid = game.ProcessId; sessionStarted = game.Started; gamePath = game.Path;
                    state = new ActivityState(); detector.Reset(); concertTracker.Reset(); raceTracker.Reset(); lastPayload = null;
                    nextScan = nextConnect = nextUpdate = DateTime.MinValue;
                    Log.Write("Game detected: PID " + gamePid + ".");
                }
                var standaloneStatus = concertOverlayRuntime = StandaloneRuntime.Read(gamePid);
                bool basicPresence = standaloneStatus?.RequiresBasicPresence == true;
                string configGamePath = !string.IsNullOrEmpty(settings.GameDirectory) ? Path.Combine(settings.GameDirectory, "umamusume.exe") : gamePath;
                if (!basicPresence && GameSession.HachimiRpcEnabled(configGamePath))
                {
                    concertSeekOverlay.HideControls();
                    if (discord.IsConnected && lastPayload != null) { try { await discord.SetActivity(null, ownPid); } catch { } }
                    discord.Dispose(); lastPayload = null;
                    discordStatus = "Hachimi discord_rpc is enabled; turn it off and restart the game to use UmaPresence";
                    UpdateStatus(); return;
                }
                OutfitCatalog.Refresh(settings);
                DataMaintenance.RefreshAutomatic(settings);
                concertReader.EnableInspectionCompatibility = standaloneStatus == null;
                if (basicPresence) concertReader.Dispose();
                var concert = settings.ConcertBridge && !basicPresence ? concertReader.Read(gamePid) : null;
                if (concertReader.EnableInspectionCompatibility && concert != null && concert.Status != 2
                    && (concert.Active || concertReader.Career != null || concertReader.Menu != null || concertReader.Race != null))
                    IntegrationProfiles.ObserveHachimiSession(settings.GameDirectory, game.Path, gamePid, game.Started, concert.Tick);
                if (settings.ConcertBridge && standaloneStatus?.Ready == true && concert != null && concert.Status != 2)
                    StandaloneUpdates.Observe(settings, gamePid, concert.Active || concertReader.Career != null
                        || concertReader.Menu != null || concertReader.Race != null);
                await standaloneConfiguration.Observe(settings, gamePid);
                standaloneMedia.Observe(settings, gamePid, concert);
                concertSeekOverlay.Observe(settings, game, standaloneStatus, concert);
                if (concert != null) concert.MainPerformerCount = settings.ConcertMainPerformerCount > 0 ? Math.Min(18, settings.ConcertMainPerformerCount) : OutfitCatalog.MainCount(concert.MusicId);
                var liveState = mode == "Auto" ? concertTracker.Observe(concert, DateTimeOffset.UtcNow) : null;
                var raceState = mode == "Auto" ? raceTracker.Observe(concertReader.Race, now) : null;
                var nativeState = mode == "Auto" ? NativeActivityPolicy.Select(concertReader.Career, liveState, raceState, concertReader.Menu) : null;
                var withoutStale = mode == "Auto" ? NativeActivityPolicy.ClearEnded(state, nativeState) : state;
                if (!object.ReferenceEquals(state, withoutStale))
                {
                    state = withoutStale; detector.Reset(); nextScan = nextUpdate = DateTime.MinValue;
                }
                if (mode == "Auto" && raceState == null && state.Race != null) { state = new ActivityState(); detector.Reset(); nextScan = nextUpdate = DateTime.MinValue; }
                if (basicPresence)
                {
                    const string fallback = "Standalone could not connect; showing basic presence";
                    timer.Interval = 2500;
                    if (screenStatus != fallback) { detector.Reset(); concertTracker.Reset(); raceTracker.Reset(); nextUpdate = DateTime.MinValue; }
                    state = new ActivityState(); screenStatus = fallback;
                }
                else if (mode == "Auto" && concertReader.Career != null && concertReader.Career.Legacy)
                {
                    timer.Interval = 1000; if (NativeActivityPolicy.CareerContextChanged(state.Career, concertReader.Career)) nextUpdate = DateTime.MinValue; state = nativeState;
                }
                else if (liveState != null)
                {
                    bool entering = state.Concert == null;
                    if (entering || state.Concert.MusicId != concert.MusicId) { concertPortraitRotation.Reset(); singerCyclePage = 0; lastSingerLine = null; nextSingerCycleChange = DateTime.MaxValue; }
                    concertPortraitRotation.Observe(concert, settings, now);
                    if (settings.ConcertCycleSingerNames && now >= nextSingerCycleChange) { singerCyclePage++; nextSingerCycleChange = DateTime.MaxValue; }
                    concert.SingerCycleElapsed = (double)singerCyclePage * settings.ConcertSingerCycleSeconds;
                    timer.Interval = 500;
                    state = liveState;
                    if (entering) nextUpdate = lastAcknowledged == DateTime.MinValue ? DateTime.MinValue : lastAcknowledged.ToUniversalTime().AddSeconds(settings.ConcertPortraitIntervalSeconds);
                    if (artworkTask == null || artworkTask.IsCompleted) artworkTask = concertArtwork.SaveCover(concert);
                    if (portraitLookupTask == null || portraitLookupTask.IsCompleted) portraitLookupTask = OutfitCatalog.RefreshMissing(concert, settings);
                    if (originalCoverLookupTask == null || originalCoverLookupTask.IsCompleted) originalCoverLookupTask = OriginalConcertCovers.RefreshMissing(concert, settings);
                }
                else if (raceState != null) { timer.Interval = 500; if (state.Race == null || state.Race.Results != raceState.Race.Results || state.Race.Preparing != raceState.Race.Preparing) nextUpdate = DateTime.MinValue; state = raceState; }
                else if (mode == "Auto" && concertReader.Career != null) { timer.Interval = 1000; if (NativeActivityPolicy.CareerContextChanged(state.Career, concertReader.Career)) nextUpdate = DateTime.MinValue; state = nativeState; }
                else if (mode == "Auto" && concertReader.Menu != null)
                {
                    timer.Interval = 1000;
                    if (state.Source != NativeMenuSnapshot.ActivitySource || state.Details != nativeState.Details || state.State != nativeState.State) nextUpdate = DateTime.MinValue;
                    state = nativeState; screenStatus = "Native menu: " + concertReader.Menu.View;
                }
                else if (mode == "Auto" && concert != null)
                {
                    timer.Interval = 1000; state = new ActivityState(); screenStatus = "Native bridge connected; waiting for menu data";
                }
                else if (mode != "Auto") { timer.Interval = 2500; state = ActivityState.Manual(mode); }
                else if (settings.ScreenDetection && now >= nextScan)
                {
                    timer.Interval = 2500;
                    if (state.Concert != null || state.Career != null || state.Race != null) { state = new ActivityState(); detector.Reset(); }
                    if (game.IsForeground)
                    {
                        nextScan = now.AddSeconds(settings.ScanIntervalSeconds);
                        ActivityState observation = null;
                        using (var screenshot = game.Capture())
                        {
                            screenStatus = screenshot == null ? "Game capture unavailable" : "Reading game screen";
                            if (screenshot != null)
                            {
                                lastScreenScan = now;
                                if (settings.OcrDetection)
                                {
                                    try { lastScreenText = await reader.Read(screenshot, settings.OcrLanguage); observation = TextClassifier.Classify(lastScreenText); }
                                    catch (Exception ex) { Log.Write("Screen recognition skipped: " + ex.Message); }
                                }
                                if (observation == null) observation = PixelClassifier.Classify(screenshot);
                                // Career activity must come from game telemetry, independent of resolution/focus.
                                if (observation != null && (observation.Kind == "Training" || (observation.Kind == "Race" && observation.Source == "Windows OCR"))) observation = null;
                                screenStatus = observation == null ? "Screen read; activity not recognized" : "Recognized " + observation.Kind;
                            }
                        }
                        // Closing the game or switching windows during recognition must not publish an old screen.
                        if (stopping) return;
                        var stillRunning = GameSession.Find(settings);
                        if (stillRunning == null || stillRunning.ProcessId != gamePid) return;
                        state = detector.Observe(observation, now, true);
                    }
                }
                if (!discord.IsConnected && now >= nextConnect)
                {
                    nextConnect = now.AddSeconds(10);
                    try { await discord.Connect(settings.ApplicationId); lastPayload = null; nextUpdate = DateTime.MinValue; discordStatus = "Connected to Discord"; }
                    catch (Exception) { discordStatus = "Waiting for Discord desktop"; }
                }
                if (discord.IsConnected)
                {
                    object activity = state.ToActivity(settings, sessionStarted);
                    string payload = Json.Encode(activity);
                    if (payload != lastPayload && now >= nextUpdate)
                    {
                        try
                        {
                            var publishingRace = state.Race;
                            await discord.SetActivity(activity, ownPid);
                            if (stopping) return;
                            lastPayload = payload; lastAcknowledged = DateTime.Now; nextUpdate = now.AddSeconds(state.Race != null ? (state.Race.Results || state.Race.Preparing || state.Race.Team.Any(e => e.Metrics != null) ? 3 : 5) : state.Concert != null ? settings.ConcertPortraitIntervalSeconds : settings.UpdateIntervalSeconds);
                            if (publishingRace != null) raceTracker.Published(publishingRace, DateTime.UtcNow);
                            if (state.Concert != null) concertPortraitRotation.Published(state.Concert, settings, DateTime.UtcNow);
                            if (state.Concert != null && settings.ConcertCycleSingerNames)
                            {
                                string singerLine = state.Concert.SingerLine(settings);
                                if (singerLine != lastSingerLine) { lastSingerLine = singerLine; nextSingerCycleChange = DateTime.UtcNow.AddSeconds(settings.ConcertSingerCycleSeconds); }
                            }
                            discordStatus = "Presence acknowledged by Discord";
                            Log.Write("Presence updated: " + state.Details + " / " + state.State + " (" + state.Source + ").");
                        }
                        catch (Exception ex) { discordStatus = ex.Message; Log.Write(ex.Message); discord.Dispose(); nextConnect = now.AddSeconds(10); }
                    }
                }
                UpdateStatus();
            }
            catch (Exception ex) { if (!stopping) { discordStatus = ex.Message; Log.Write("Update failed: " + ex.Message); UpdateStatus(); } }
            finally { busy = false; }
        }

        private void UpdateStatus()
        {
            string label = paused ? "Paused" : gamePid == 0 ? "Waiting for game" : state.Details + " • " + state.State;
            if (tray != null) tray.Text = ("UmaPresence • " + label).Substring(0, Math.Min(63, ("UmaPresence • " + label).Length));
            string key = gamePid + "|" + label + "|" + discordStatus + "|" + reader.Status + "|" + concertReader.Status + "|" + lastAcknowledged.Ticks + "|" + lastScreenScan.Ticks + "|" + gameForeground + "|" + DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond / 10;
            key += "|" + concertSeekOverlay.Status + "|" + concertSeekOverlay.LastCommand + "|" + concertOverlayRuntime?.SeekReason + "|" + concertOverlayRuntime?.PlaybackStage;
            if (state.Concert != null) key += "|" + (state.Concert.Paused ? state.Concert.Position.ToString("F2") : (DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond / 10).ToString());
            if (key != lastReportedStatus)
            {
                lastReportedStatus = key;
                try
                {
                    string paddockDiagnostics = concertReader.RaceDiagnostics;
                    if (gamePid != 0 && !string.IsNullOrEmpty(paddockDiagnostics) && paddockDiagnostics != lastPaddockDiagnostics && (concertReader.Race == null || concertReader.Race.Preparing))
                    {
                        File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "race-paddock-diagnostics.json"), Json.Encode(new { gamePid, diagnostics = paddockDiagnostics, race = concertReader.Race, updated = DateTimeOffset.Now.ToString("O") }), new UTF8Encoding(false));
                        lastPaddockDiagnostics = paddockDiagnostics;
                    }
                    File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "status.json"), Json.Encode(new {
                        appPid = ownPid, gamePid, gamePath, paused, activity = label, source = state.Source, discord = discordStatus,
                        ocr = reader.Status, bridge = concertReader.Status, concertSongId = state.Concert == null ? 0 : state.Concert.MusicId, concertCast = state.Concert == null ? null : state.Concert.CastIds, artwork = concertArtwork.Status,
                        gameForeground, screenStatus, lastScreenScan = lastScreenScan == DateTime.MinValue ? null : lastScreenScan.ToString("O"), lastScreenText,
                        concertOverlay = concertSeekOverlay.Status, concertOverlayLastCommand = concertSeekOverlay.LastCommand,
                        concertOverlayRuntime = concertOverlayRuntime == null ? null : new {
                            concertOverlayRuntime.State, concertOverlayRuntime.Backend, Capabilities = (ulong)concertOverlayRuntime.Capabilities,
                            concertOverlayRuntime.PlaybackStage, concertOverlayRuntime.SeekReason, concertOverlayRuntime.SeekStart, concertOverlayRuntime.SeekEnd
                        },
                        concertPosition = state.Concert == null ? 0 : state.Concert.Position, concertDuration = state.Concert == null ? 0 : state.Concert.Duration, concertPaused = state.Concert != null && state.Concert.Paused,
                        concertDresses = state.Concert == null ? null : state.Concert.DressIds,
                        outfitDiagnostics = concertReader.OutfitDiagnostics,
                        careerDiagnostics = concertReader.CareerDiagnostics, careerResultRaw = concertReader.CareerResultRaw, career = state.Career, nativeMenu = concertReader.Menu,
                        raceDiagnostics = concertReader.RaceDiagnostics, race = state.Race,
                        outfitCatalog = OutfitCatalog.Status, mainPerformerCount = state.Concert == null ? 0 : state.Concert.MainPerformerCount,
                        discordActivity = discord.LastActivity,
                        originalConcertCover = state.Concert == null ? null : OriginalConcertCovers.Source(state.Concert.MusicId), originalCoverLookup = OriginalConcertCovers.Status,
                        lastAcknowledged = lastAcknowledged == DateTime.MinValue ? null : lastAcknowledged.ToString("O"), updated = DateTimeOffset.Now.ToString("O")
                    }), new UTF8Encoding(false));
                }
                catch { }
            }
        }

        private string DiagnosticText()
        {
            long memory;
            using (var process = Process.GetCurrentProcess()) memory = process.WorkingSet64 / 1024 / 1024;
            return "UmaPresence " + ReleasePolicy.Version + "\r\n\r\n" +
                "Release policy: " + (ReleasePolicy.ExperimentalAudio ? "Private development build; experimental audio available" : "Public prerelease; original concert audio") + "\r\n" +
                "Game: " + (gamePid == 0 ? "Not running" : "Running (PID " + gamePid + ")") + "\r\n" +
                "Location: " + gamePath + "\r\n\r\n" +
                "Discord: " + discordStatus + "\r\n" +
                "Last confirmed update: " + (lastAcknowledged == DateTime.MinValue ? "None yet" : lastAcknowledged.ToString("yyyy-MM-dd HH:mm:ss")) + "\r\n" +
                "Activity: " + state.Details + " / " + state.State + "\r\n" +
                "Detection: " + state.Source + "\r\n" +
                "OCR: " + reader.Status + "\r\n" +
                "Game foreground: " + gameForeground + "\r\n" +
                "Screen scan: " + screenStatus + "\r\n" +
                "Last screen read: " + (lastScreenScan == DateTime.MinValue ? "None" : lastScreenScan.ToLocalTime().ToString("HH:mm:ss")) + "\r\n" +
                "Concert bridge: " + concertReader.Status + "\r\n" +
                "Standalone media: " + standaloneMedia.Status + "\r\n" +
                "Standalone configuration: " + standaloneConfiguration.Status + "\r\n" +
                StandaloneAudioDiagnostics.Report(gamePid) + "\r\n" +
                "Concert seek overlay: " + concertSeekOverlay.Status + "\r\n" +
                "Artwork: " + concertArtwork.Status + "\r\n" +
                "Memory (working set): " + memory + " MB\r\n" +
                "Screen interval: " + settings.ScanIntervalSeconds + " seconds (foreground game only)\r\n\r\n" +
                "Screens are processed locally and discarded. No browser or GameTora.\r\n" +
                IntegrationProfiles.Status(settings, gamePid) + "\r\n" +
                "Discord desktop and activity sharing must be enabled.\r\n\r\n" +
                "Log: " + Log.FilePath;
        }
        private void ShowSettings()
        {
            if (settingsWindow == null || settingsWindow.IsDisposed)
                settingsWindow = new SettingsWindow(settings, DiagnosticText, () => gamePid,
                    () => { nextUpdate = DateTime.MinValue; lastPayload = null; nextScan = DateTime.MinValue; },
                    async () => {
                        var concert = state.Concert;
                        if (concert == null || !concert.Active) throw new InvalidOperationException("Play or pause a song in Live Theater first.");
                        concertArtwork.RetryCover(); await concertArtwork.SaveCover(concert);
                        string filename = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "generated-artwork", "concert_" + concert.MusicId + ".png");
                        if (!File.Exists(filename)) throw new IOException(concertArtwork.Status);
                    }, new SettingsActions {
                        Overview = () => "Game: " + (gamePid == 0 ? "Not running" : "Connected") + "\r\nDiscord: " + discordStatus + "\r\nActivity: " + (gamePid == 0 ? "Waiting for game" : state.Details + " — " + state.State),
                        BridgeStatus = () => paused ? "Presence paused" : !settings.ConcertBridge ? "Disabled in settings" : concertReader.Status,
                        Activity = () => mode, SelectActivity = SetActivity, ChooseCharacter = ChooseCharacter,
                        Paused = () => paused, Pause = value => { paused = value; if (pauseItem != null) pauseItem.Checked = value; UpdateStatus(); },
                        Startup = IsStartupEnabled, SetStartup = SetStartup,
                        CurrentSong = () => state.Concert != null && state.Concert.Active ? state.Concert.MusicId : 0,
                        RuntimePreferences = () => standaloneConfiguration.Status
                    });
            settingsWindow.Show(); Native.ShowWindow(settingsWindow.Handle, 9); settingsWindow.BringToFront(); settingsWindow.Activate(); Native.SetForegroundWindow(settingsWindow.Handle);
        }
        private void ShowStatus() { ShowSettings(); settingsWindow.Navigate("Overview"); }
        private void ChooseCharacter()
        {
            using (var dialog = new Form { Text = "Character / artwork", Size = new Size(420, 210), FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, StartPosition = FormStartPosition.CenterScreen })
            {
                var hint = new Label { Text = "Override the character shown on your presence:", Left = 16, Top = 16, Width = 380 };
                var combo = new ComboBox { Left = 16, Top = 44, Width = 370, DropDownStyle = ComboBoxStyle.DropDownList };
                combo.Items.Add("Automatic (when detected)");
                var characters = CharacterCatalog.Entries.OrderBy(c => c.Name).ToList();
                foreach (var c in characters) combo.Items.Add(c.Name);
                combo.SelectedIndex = Math.Max(0, characters.FindIndex(c => c.Id == settings.ManualCharacterId) + 1);
                var save = new Button { Text = "Save", Left = 296, Top = 120, Width = 90, DialogResult = DialogResult.OK };
                dialog.Controls.AddRange(new Control[] { hint, combo, save }); dialog.AcceptButton = save;
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    string oldName = settings.ManualCharacter, oldId = settings.ManualCharacterId;
                    var selected = combo.SelectedIndex > 0 ? characters[combo.SelectedIndex - 1] : null;
                    settings.ManualCharacter = selected == null ? "" : selected.Name;
                    settings.ManualCharacterId = selected == null ? "" : selected.Id;
                    try {
                        settings.Save(); nextUpdate = DateTime.MinValue; lastPayload = null;
                        if (settingsWindow != null && !settingsWindow.IsDisposed) settingsWindow.Saved("Saved. Character override applies now; Discord may take a moment to display it.");
                    } catch (Exception ex) { settings.ManualCharacter = oldName; settings.ManualCharacterId = oldId; MessageBox.Show(ex.Message, "Could not save character override", MessageBoxButtons.OK, MessageBoxIcon.Error); }
                }
            }
        }
        private bool IsStartupEnabled()
        {
            try { using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) return key != null && key.GetValue("UmapyonPresence") != null; } catch { return false; }
        }
        private static Icon MakeIcon()
        {
            using (var stream = typeof(Program).Assembly.GetManifestResourceStream("umapyon.ico"))
            using (var icon = new Icon(stream, SystemInformation.SmallIconSize)) return (Icon)icon.Clone();
        }

        private void ControlLoop()
        {
            while (!stopping)
            {
                try
                {
                    var security = new PipeSecurity();
                    security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User, PipeAccessRights.FullControl, AccessControlType.Allow));
                    using (var server = new NamedPipeServerStream(Program.ControlPipe, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.None, 256, 256, security))
                    {
                        lock (controlLock) { if (stopping) return; controlServer = server; }
                        server.WaitForConnection();
                        string command = "";
                        int b;
                        while (command.Length < 16 && (b = server.ReadByte()) >= 0 && b != 10) command += (char)b;
                        if (!stopping) dispatcher.BeginInvoke((Action)(async () => { if (command == "stop") await Stop(); else if (command == "show") { CreateTray(); ShowStatus(); } else if (command == "settings") { CreateTray(); ShowSettings(); } else if (command == "diagnostics") File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"live-diagnostic.txt"),DiagnosticText(),Encoding.UTF8); }));
                    }
                }
                catch { if (!stopping) Thread.Sleep(1000); }
                finally { lock (controlLock) controlServer = null; }
            }
        }
        private async Task Stop()
        {
            if (stopping) return;
            stopping = true; timer.Stop();
            try { if (discord.IsConnected) await discord.SetActivity(null, ownPid); } catch { }
            discord.Dispose(); Log.Write("Stopped. Presence cleared.");
            ExitThread();
        }
        protected override void ExitThreadCore()
        {
            stopping = true; timer.Dispose(); discord.Dispose(); concertReader.Dispose();
            lock (controlLock) { if (controlServer != null) controlServer.Dispose(); }
            if (tray != null) { tray.Visible = false; tray.Dispose(); }
            if (appIcon != null) appIcon.Dispose();
            if (settingsWindow != null) settingsWindow.Dispose();
            if (standaloneMedia != null) standaloneMedia.Dispose();
            if (concertSeekOverlay != null) concertSeekOverlay.Dispose();
            dispatcher.Dispose();
            base.ExitThreadCore();
        }
    }
}
