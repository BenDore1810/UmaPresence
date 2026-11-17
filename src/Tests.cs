using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UmapyonPresence
{
    internal static class Tests
    {
        // Render the real settings form without activating a desktop window.
        private sealed class BackgroundSettingsWindow : SettingsWindow
        {
            internal BackgroundSettingsWindow(Settings settings) : base(settings, () => "Background layout inspection", () => 0, () => {}, () => Task.FromResult(0)) { }
            internal BackgroundSettingsWindow(Settings settings, SettingsActions actions) : base(settings, () => "Background action inspection", () => 0, () => {}, () => Task.FromResult(0), actions) { }
            protected override bool ShowWithoutActivation { get { return true; } }
            protected override System.Windows.Forms.CreateParams CreateParams {
                get { var value = base.CreateParams; value.ExStyle |= 0x08000000 | 0x00000080; return value; }
            }
        }
        private static int passed;
        private static void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); passed++; }
        private static void ConcertOverlayPolicyTests()
        {
            var runtime = new StandaloneRuntimeStatus { State = 4, Backend = 1, Capabilities = RuntimeCapability.Telemetry | RuntimeCapability.Playback };
            var concert = new ConcertSnapshot { Active = true, Duration = 120 };
            Check(ConcertSeekOverlay.HiddenReason("Always", true, runtime, concert, false) == null && !runtime.CanSeek,
                "verified Standalone playback shows progress before seeking is ready");
            Check(ConcertSeekOverlay.HiddenReason("Paused", true, runtime, concert, false) != null,
                "paused-only mode hides progress during playback");
            concert.Paused = true;
            Check(ConcertSeekOverlay.HiddenReason("Paused", true, runtime, concert, false) == null &&
                ConcertSeekOverlay.HiddenReason("Off", true, runtime, concert, false) != null &&
                ConcertSeekOverlay.HiddenReason("Always", false, runtime, concert, false) != null,
                "paused progress respects visibility and focus");
            runtime.Capabilities |= RuntimeCapability.Seek;
            Check(!runtime.CanSeek, "seek capability with a missing window cannot enable dragging");
            runtime.SeekStart = 1; runtime.SeekEnd = 119;
            Check(runtime.CanSeek, "validated seek window enables dragging");
            concert.Active = false;
            Check(ConcertSeekOverlay.HiddenReason("Always", true, runtime, concert, false) != null, "leaving the concert hides controls");
            concert.Active = true; runtime.Backend = 2;
            Check(!runtime.CanSeek && ConcertSeekOverlay.HiddenReason("Always", true, runtime, concert, false) != null,
                "Hachimi ownership hides Standalone controls");
            var portrait = new Rectangle(200, 30, 240, 900);
            var landscape = new Rectangle(-900, 20, 1280, 720);
            var portraitBar = ConcertSeekOverlay.OverlayBounds(portrait, 32, 600, 12, 64);
            var landscapeBar = ConcertSeekOverlay.OverlayBounds(landscape, 32, 600, 12, 64);
            Check(portrait.Contains(portraitBar) && portraitBar.Width == 216 &&
                landscape.Contains(landscapeBar) && landscapeBar.Width == 600,
                "song bar fits narrow portrait and wide landscape game windows");
        }
        [STAThread]
        public static int Main(string[] args)
        {
            Native.SetProcessDPIAware();
            System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
            Console.OutputEncoding = new UTF8Encoding(false);
            if (args.Length == 1 && args[0] == "--standalone-update-regression") {
                try { StandaloneUpdateTests.Run(); return 0; }
                catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
            }
            if (args.Length == 2 && args[0] == "--welcome-uninstall-regression") {
                try { WelcomeUninstallRegression(args[1]); return 0; }
                catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
            }
            if (args.Length == 3 && args[0] == "--welcome-dialog-preview") {
                WelcomeDialogPreview(args[1], args[2]);
                return 0;
            }
            if (args.Contains("--pisa-dev-policy")) {
                Check(ReleasePolicy.PisaDev && ReleasePolicy.PublicRelease && ReleasePolicy.ExperimentalAudio, "Pisa enables experimental audio and retains the portable installer");
                Check(ReleasePolicy.StandaloneUpdateChannel != ReleasePolicy.Version, "Pisa cannot use a public-only Standalone update");
                using (var asset = typeof(Program).Assembly.GetManifestResourceStream("special-week.png"))
                    Check(asset != null && asset.Length > 0, "Pisa includes the Special Week sidebar artwork");
                var settings = new Settings { ConcertAudioMode = "Duet", ConcertVoice1 = 1001, ConcertVoice2 = 1002 };
                Check(StandaloneConfiguration.Flags(settings, RuntimeCapability.Routing) == 16, "Pisa sends Duet when routing is available");
                using (var page = new ConcertSettingsPage(settings, () => 0, () => {}, message => {})) {
                    Func<System.Windows.Forms.Control, IEnumerable<System.Windows.Forms.Control>> walk = null;
                    walk = root => root.Controls.Cast<System.Windows.Forms.Control>().SelectMany(child => new[] { child }.Concat(walk(child)));
                    Check(walk(page).Any(control => control.Text == "Advanced audio · Experimental"), "Pisa exposes experimental audio in Concert settings");
                }
                return 0;
            }
            if (args.Contains("--public-release")) {
                Check(ReleasePolicy.PublicRelease, "public release compilation is active");
                var defaults = new Settings();
                Check(defaults.ApplicationId == "1563519446503522344" && defaults.GameIntegrationMode == "Standalone", "public Discord application and Standalone defaults");
                Check(defaults.GameDirectory == "" && defaults.ManualCharacter == "" && defaults.ConcertArtwork.Count == 0 && defaults.CareerPortraitAssets.Count == 0, "public defaults contain no installation or roster overrides");
                Check(defaults.ConcertAudioMode == "Original" && defaults.StandaloneSeekBarMode == "Paused", "original audio and paused seek bar defaults");
                foreach (string mode in new[] { "Balanced", "Lead", "Solo", "Duet" }) {
                    var privatePreferences = new Settings { ConcertAudioMode = mode, ConcertAllowDuplicates = true, ConcertVoice1 = 1001, ConcertVoice2 = 1002 };
                    Check(StandaloneConfiguration.Flags(privatePreferences, RuntimeCapability.Formation | RuntimeCapability.Mix | RuntimeCapability.Routing) == 1
                        && privatePreferences.ConcertAudioMode == mode, "public runtime requests duplicate casting and blocks " + mode);
                }
                Check(StandaloneConfiguration.Flags(new Settings { ConcertAllowDuplicates = true }, RuntimeCapability.Seek) == 0,
                    "duplicate casting requires the runtime formation capability");
                ConcertOverlayPolicyTests();
                Check(!ReleasePolicy.ExperimentalAudio, "experimental release audio is disabled");
                var coverSettings = new Settings { ConcertPreferOriginalCover = true };
                coverSettings.ConcertArtwork["1176"] = "concert_1176";
                Check(SongCatalog.ImageKey(1176, coverSettings) == OriginalConcertCovers.Url(1176)
                    && SongCatalog.ImageKey(999999, coverSettings) == coverSettings.LargeImage, "public cover preference uses originals or the app icon");
                coverSettings.ConcertPreferOriginalCover = false;
                Check(SongCatalog.ImageKey(1176, coverSettings) == "concert_1176", "registered cover works after opting in");
                using (var stream = typeof(Program).Assembly.GetManifestResourceStream("umapyon.png"))
                using (var image = Image.FromStream(stream))
                    Check(image.Width == 256 && image.Height == 256, "Public sidebar logo decodes from the bundled PNG");
                using (var page = new ConcertSettingsPage(defaults, () => 0, () => {}, message => {})) {
                    Func<System.Windows.Forms.Control, IEnumerable<System.Windows.Forms.Control>> walk = null;
                    walk = root => root.Controls.Cast<System.Windows.Forms.Control>().SelectMany(child => new[] { child }.Concat(walk(child)));
                    var controls = walk(page).ToArray();
                    Check(!controls.Any(control => control.Name == "ConcertAudioMode" || control.Text == "Advanced audio · Experimental"), "experimental audio controls are absent from public settings");
                    Check(controls.OfType<System.Windows.Forms.CheckBox>().Single(control => control.Text == "Use the same Uma in multiple slots").Enabled, "duplicate casting control is enabled");
                    Check(controls.OfType<System.Windows.Forms.ComboBox>().Any(control => control.Enabled && control.Items.Contains("Always during a concert")), "public concert settings expose the seek bar");
                }
                bool rejected = false; try { LoaderHelper.Verify(new byte[198656]); } catch (InvalidDataException) { rejected = true; }
                Check(rejected, "unverified helper rejected");
                if (args.Length > 1) {
                    byte[] helper = File.ReadAllBytes(args[1]); LoaderHelper.Verify(helper);
                    Check(true, "official pinned x64 helper verifies"); helper[helper.Length - 1] ^= 1;
                    rejected = false; try { LoaderHelper.Verify(helper); } catch (InvalidDataException) { rejected = true; }
                    Check(rejected, "modified official helper rejected");
                }
                Console.WriteLine(passed + " public release checks passed."); return 0;
            }
            if (args.Contains("--help-regression")) {
                System.Windows.Forms.Application.EnableVisualStyles();
                using (var form = new BackgroundSettingsWindow(new Settings())) {
                    form.StartPosition = System.Windows.Forms.FormStartPosition.Manual; form.Location = new Point(-20000,-20000); form.ShowInTaskbar = false; form.Show(); form.Navigate("Help & diagnostics");
                    Func<System.Windows.Forms.Control, IEnumerable<System.Windows.Forms.Control>> walk = null;
                    walk = root => root.Controls.Cast<System.Windows.Forms.Control>().SelectMany(child => new[] { child }.Concat(walk(child)));
                    var help = walk(form).OfType<DesktopHelp>().Single();
                    Check(walk(form).OfType<System.Windows.Forms.Button>().Any(button => button.Text == "Open app folder"), "Help includes Open app folder");
                    var original = help.GuideLabels; var handles = original.Select(label => label.Handle).ToArray();
                    var expected = new[] { "Install UmaPresence", "Switch integration", "Control your concert", "Keep your data current", "Find the next useful check", "Uninstall or reinstall", "After an update" };
                    float scale; using (var graphics = form.CreateGraphics()) scale = graphics.DpiX / 96f;
                    for (int cycle = 0; cycle < 100; cycle++) {
                        form.Size = new Size((int)((cycle % 2 == 0 ? 920 : 1280)*scale),(int)(700*scale));
                        var page = help.Parent as DesktopStack; page.AutoScrollPosition = new Point(0,int.MaxValue);
                        int topic = new[] {3,2,3,0,4,1,5,6}[cycle % 8]; help.SelectedTopic = topic;
                        System.Windows.Forms.Application.DoEvents();
                        var labels = help.GuideLabels;
                        if (labels.Length != 9 || labels[0].Text != expected[topic]) throw new Exception("Wrong or accumulated Help topic at cycle " + cycle);
                        for (int i=0;i<9;i++) if (!ReferenceEquals(original[i],labels[i]) || labels[i].Handle != handles[i]) throw new Exception("Help label handle was recreated");
                        int stale = labels.Count(label => label.Text.StartsWith("Click Refresh game data"));
                        if (stale != (topic == 3 ? 1 : 0)) throw new Exception("Stale or repeated metadata paragraph at cycle " + cycle);
                        for (int i=1;i<9;i++) if (labels[i-1].Bottom > labels[i].Top) throw new Exception("Help text overlaps at cycle " + cycle);
                    }
                    Check(true,"100 topic changes after scrolling/resizing keep exactly nine stable label controls");
                    Check(true,"Data-to-Concert transitions remove the metadata paragraph without duplicate or overlapping text");
                    Check(help.GuideLabels[0].Font.FontFamily.Name == help.GuideLabels[2].Font.FontFamily.Name && help.GuideLabels[0].Font.Bold, "Help uses one text family with a bold heading weight");
                    form.Navigate("Game integration");
                    walk(form).OfType<System.Windows.Forms.Button>().Single(button => button.Text == "Help with this status").PerformClick();
                    Check(help.SelectedTopic == DesktopHelp.UpdateTopic, "Game integration opens the matching update guide");
                    foreach (int topic in new[] {3,2,0,5,6}) {
                        help.SelectedTopic = topic; System.Windows.Forms.Application.DoEvents();
                        using (var bitmap=new Bitmap(form.Width,form.Height)) {form.DrawToBitmap(bitmap,new Rectangle(0,0,form.Width,form.Height));bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"help-switch-"+topic+".png"));}
                    }
                }
                Console.WriteLine(passed+" Help switching checks passed."); return 0;
            }
            if (args.Contains("--ui-save-test")) {
                System.Windows.Forms.Application.EnableVisualStyles();
                string original = File.Exists(Settings.FilePath) ? File.ReadAllText(Settings.FilePath) : null;
                try {
                    var configuration = new Settings { GameDirectory = "" };
                    using (var form = new BackgroundSettingsWindow(configuration)) {
                        form.StartPosition = System.Windows.Forms.FormStartPosition.Manual; form.Location = new Point(-20000,-20000); form.ShowInTaskbar = false; form.Show();
                        Func<System.Windows.Forms.Control, System.Collections.Generic.IEnumerable<System.Windows.Forms.Control>> descendants = null;
                        descendants = root => root.Controls.Cast<System.Windows.Forms.Control>().SelectMany(child => new[] { child }.Concat(descendants(child)));
                        form.Navigate("Presence"); System.Windows.Forms.Application.DoEvents();
                        var controls = descendants(form).Where(c => c.Visible).ToArray();
                        var portrait = controls.OfType<System.Windows.Forms.CheckBox>().Single(c => c.Text == "Show character portraits"); portrait.Checked = false;
                        controls.OfType<System.Windows.Forms.Button>().Single(c => c.Text == "Save changes").PerformClick();
                        Check(!Settings.Load().ShowCharacter && form.SavedMessage.Contains("apply now"), "Presence Save writes the preference and explains immediate effect");
                        form.Navigate("Concert"); System.Windows.Forms.Application.DoEvents(); controls = descendants(form).Where(c => c.Visible).ToArray();
                        controls.OfType<System.Windows.Forms.CheckBox>().Single(c => c.Text == "Use the same Uma in multiple slots").Checked = true;
                        controls.OfType<System.Windows.Forms.Button>().Single(c=>c.Text=="Advanced audio · Experimental").PerformClick();
                        controls = descendants(form).Where(c=>c.Visible).ToArray();
                        var modeBox=controls.OfType<System.Windows.Forms.ComboBox>().Single(c=>c.Name=="ConcertAudioMode");
                        modeBox.SelectedItem=modeBox.Items.Cast<ConcertAudioChoice>().Single(item=>item.Key=="Solo");
                        controls = descendants(form).Where(c => c.Visible).ToArray();
                        controls.OfType<System.Windows.Forms.ComboBox>().Where(c => c.Items.Count > 50).First().SelectedIndex = 1;
                        controls.OfType<System.Windows.Forms.Button>().Single(c => c.Text == "Save changes").PerformClick();
                        Check(Settings.Load().ConcertAllowDuplicates && Settings.Load().ConcertAudioMode == "Solo", "Concert Save persists duplicates and a selected arrangement with no game running");
                        Check(form.SavedMessage.Contains("formation screen") && form.SavedMessage.Contains("Load the concert again"), "Concert Save popup explains both formation and reload effects");
                        bool paused = false;
                        Check(!configuration.StartMinimized, "Normal launch defaults to opening the desktop window");
                        var pauseActions = new SettingsActions { Paused = () => paused, Pause = value => paused = value };
                        using (var other = new BackgroundSettingsWindow(configuration, pauseActions)) {
                            other.ShowInTaskbar = false; other.Location = new Point(-20000,-20000); other.StartPosition = System.Windows.Forms.FormStartPosition.Manual; other.Show();
                            descendants(other).OfType<System.Windows.Forms.Button>().Single(c => c.Text == "Pause / resume presence").PerformClick();
                            Check(paused, "Overview pause invokes the companion action");
                        }
                    }
                    Console.WriteLine(passed + " UI save checks passed."); return 0;
                } catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
                finally { if (original == null) { if (File.Exists(Settings.FilePath)) File.Delete(Settings.FilePath); } else File.WriteAllText(Settings.FilePath, original, new UTF8Encoding(false)); }
            }
            if (args.Contains("--compatibility-regression")) {
                StandaloneGameCompatibilityTests.Run(Check);
                return 0;
            }
            if (args.Contains("--installer-regression")) {
                try { StandaloneInstallerTests(); Console.WriteLine(passed + " installer checks passed."); return 0; } catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
            }
            if (args.Contains("--rival-regression")) {
                try { RaceTests(); Console.WriteLine(passed + " race checks passed."); return 0; } catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
            }
            if (args.Contains("--profile-points-regression")) {
                try { ProfilePointsTests(); return 0; } catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
            }
            if (args.Contains("--integration-page-check")) {
                try {
                    System.Windows.Forms.Application.EnableVisualStyles();
                    Func<System.Windows.Forms.Control, IEnumerable<System.Windows.Forms.Control>> walk = null;
                    walk = root => root.Controls.Cast<System.Windows.Forms.Control>().SelectMany(child => new[] { child }.Concat(walk(child)));
                    using (var page = new IntegrationSettingsPage(new Settings { GameDirectory = "", GameIntegrationMode = "Standalone" }, () => 0, () => { }, message => { })) {
                        var controls = walk(page).ToArray(); var buttons = controls.OfType<System.Windows.Forms.Button>().ToArray();
                        const string uninstallLabel = "Uninstall UmaPresence integration…";
                        Check(buttons.Any(button => button.Text == "Install Standalone") && buttons.Any(button => button.Text == uninstallLabel), "Install, reinstall and uninstall actions are visible on the main Game integration page");
                        Check(controls.OfType<System.Windows.Forms.ComboBox>().Count() == 1 && controls.OfType<System.Windows.Forms.ComboBox>().Single().Items.Count == 2 && buttons.Count(button => button.Text == "Apply switch") == 1, "Existing Hachimi/Standalone selector remains the only mode control");
                        Check(controls.OfType<System.Windows.Forms.Label>().Any(label => label.Text.StartsWith("Starting profile:")), "Starting loader detection is visible in the integration status");
                    }
                    Console.WriteLine(passed + " integration page checks passed."); return 0;
                } catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
            }
            if (args.Contains("--game-running-check")) {
                try {
                    int at = Array.IndexOf(args, "--game-running-check");
                    var settings = at >= 0 && at + 1 < args.Length && File.Exists(args[at + 1]) ? Json.Decode<Settings>(File.ReadAllText(args[at + 1], Encoding.UTF8)) : Settings.Load();
                    Console.WriteLine(GameInstallationDiscovery.IsGameRunning(settings.GameDirectory) ? "Target game is running." : "Target game is not running."); return 0;
                } catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
            }
            if (args.Contains("--media-self-test")) {
                try { Console.WriteLine(StandaloneMediaControls.SelfTest()); return 0; } catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
            }
            if (args.Contains("--rotation-regression")) {
                try { StandaloneRotationTests(); return 0; } catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
            }
            if (args.Contains("--song-catalog-check")) {
                try {
                Console.WriteLine(DataMaintenance.ReloadSongs(Settings.Load()));
                var song = SongCatalog.Find(1082);
                Check(song.Title != "Song 1082" && song.Members == 20 && song.MainCount == 3, "Installed l'Arc de gloire retains its title and 20-performer metadata with three main singers after refresh");
                Console.WriteLine(Json.Encode(song)); return 0;
                } catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
            }
            if (args.Contains("--settings-preview")) {
                System.Windows.Forms.Application.EnableVisualStyles();
                int settingsArgument = Array.IndexOf(args, "--preview-settings");
                var previewSettings = settingsArgument >= 0 ? Json.Decode<Settings>(File.ReadAllText(args[settingsArgument + 1], Encoding.UTF8)) : Settings.Load();
                bool background = args.Contains("--background-inspect");
                using (var form = new BackgroundSettingsWindow(previewSettings)) {
                    form.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
                    form.Location = new Point(-20000, -20000); form.ShowInTaskbar = false;
                    form.Show(); form.PerformLayout();
                    bool scaled = args.Contains("--scaled-ui");
                    if (scaled) form.Scale(new SizeF(1.5f, 1.5f));
                    Check(form.PageNames.Count() == 6 && form.SelectedPage == "Overview", "Sidebar opens Overview with six separate feature pages");
                    float displayScale; using (var graphics = form.CreateGraphics()) displayScale = graphics.DpiX / 96f;
                    Console.WriteLine("Actual display DPI: " + displayScale * 96 + "; production text renderer enabled");
                    var sizes = new[] { new Size((int)(1140*displayScale),(int)(820*displayScale)), new Size((int)(920*displayScale),(int)(640*displayScale)), new Size((int)(1280*displayScale),(int)(900*displayScale)) };
                    foreach (var size in sizes) {
                        form.Size = size;
                        foreach (string page in form.PageNames.ToArray()) {
                            var nav=form.Controls.Cast<System.Windows.Forms.Control>().SelectMany(host=>host.Controls.Cast<System.Windows.Forms.Control>()).OfType<System.Windows.Forms.FlowLayoutPanel>().SelectMany(host=>host.Controls.OfType<DesktopNavigation>()).ToArray();
                            nav.Single(button=>button.AccessibleName==page).PerformClick(); form.PerformLayout(); System.Windows.Forms.Application.DoEvents();
                            if(nav.Count(button=>button.Selected)!=1||!nav.Single(button=>button.AccessibleName==page).Selected)throw new Exception("Selected navigation did not follow the clicked tab: "+page);
                            foreach(var button in nav)using(var navImage=new Bitmap(button.Width,button.Height)){
                                button.DrawToBitmap(navImage,new Rectangle(Point.Empty,navImage.Size));
                                var fill=navImage.GetPixel(button.Width/2,DesktopDesign.Px(button,6));
                                if(fill.ToArgb()!=(button.Selected?DesktopDesign.NavigationSelected:DesktopDesign.Sidebar).ToArgb())throw new Exception("Missing persistent selected-tab background: "+button.Text);
                                if(button.Selected&&navImage.GetPixel(DesktopDesign.Px(button,5),button.Height/2).ToArgb()!=DesktopDesign.NavigationMarker.ToArgb())throw new Exception("Missing selected-tab marker: "+button.Text);
                            }
                            if (page == "Game integration") {
                                Action<System.Windows.Forms.Control> expand = null;
                                expand = control => { foreach (System.Windows.Forms.Control child in control.Controls) { var button = child as System.Windows.Forms.Button; if (button != null && button.Text == "Advanced recovery…") button.PerformClick(); expand(child); } };
                                expand(form); System.Windows.Forms.Application.DoEvents();
                            }
                            Check(form.SelectedPage == page, "Sidebar navigation opens " + page + " at " + size.Width);
                            if (args.Contains("--layout-details")) {
                                Action<System.Windows.Forms.Control,int> dump = null;
                                dump = (control,depth) => { if (control.Visible) Console.WriteLine(new string(' ',depth)+control.GetType().Name+" "+control.Bounds+" pad="+control.Padding+" margin="+control.Margin+" font="+control.Font.Size+" dpi="+control.DeviceDpi+" "+control.Text.Substring(0, Math.Min(24,control.Text.Length))); foreach(System.Windows.Forms.Control child in control.Controls)dump(child,depth+1); }; dump(form,0);
                            }
                            Action<System.Windows.Forms.Control> inspect = null;
                            inspect = control => {
                                var flow = control as System.Windows.Forms.FlowLayoutPanel;
                                if (flow != null && flow.Visible && flow.AutoScroll && flow.HorizontalScroll.Visible) throw new Exception("Horizontal page scrolling: " + page);
                                var stack = control as DesktopStack;
                                if (stack != null && stack.Visible) {
                                    if (!stack.Card) {
                                        stack.AutoScrollPosition = Point.Empty; stack.PerformLayout();
                                        var heading = stack.Controls.OfType<System.Windows.Forms.Label>().FirstOrDefault(label => label.Name == "PageHeading");
                                        if (heading != null && (heading.Width < stack.ClientSize.Width * .75 || heading.Height > DesktopDesign.Px(heading,70))) throw new Exception("Heading collapsed or inflated: " + page);
                                    }
                                    var children = stack.Controls.Cast<System.Windows.Forms.Control>().Where(child => child.Visible && child.Tag as string != "hidden").OrderBy(child => child.Top).ToArray();
                                    for (int index = 1; index < children.Length; index++) if (children[index-1].Bottom > children[index].Top+1) throw new Exception("Overlapping settings: " + page);
                                }
                                if (control is DesktopNavigation && control.Visible && System.Windows.Forms.TextRenderer.MeasureText(control.Text, control.Font).Width + DesktopDesign.Px(control,54) > control.Width) throw new Exception("Clipped navigation text: " + control.Text);
                                foreach (System.Windows.Forms.Control child in control.Controls) inspect(child);
                            }; inspect(form);
                            using (var bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height)); bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings-" + page.Replace(" & ", "-").Replace(" ", "-") + "-" + size.Width + ".png")); }
                            if (!background) Check(form.SelectNextControl(form.ActiveControl, true, true, true, true) && form.ActiveControl != null, "Keyboard focus advances on " + page);

                        }
                    }
                    form.Saved("Saved. Load the concert again to apply the vocal arrangement.");
                    Check(form.SavedMessage.Contains("Load the concert again"), "Save popup states when the change takes effect");
                    using (var bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height)); bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings-saved-popup.png")); }
                    Check(SettingsUi.ConcertEffect(new Settings(), new Settings { ConcertAudioMode = "Solo", ConcertVoice1 = 1001 }).Contains("Load the concert again"), "Entering Solo explains the required concert reload");
                    Check(SettingsUi.ConcertEffect(new Settings { ConcertAudioMode = "Duet" }, new Settings()).Contains("Load the concert again"), "Leaving a routed arrangement also requires a concert reload");
                    Check(SettingsUi.ConcertEffect(new Settings(), new Settings { ConcertAllowDuplicates = true }).Contains("formation screen"), "Duplicate preference save explains where it applies");
                    Check(SettingsUi.ConcertEffect(new Settings(), new Settings { ConcertAudioMode = "Lead" }).Contains("during playback"), "Safe gain changes do not ask for a concert reload");
                }
                return 0;
            }
            if (args.Contains("--maintenance-check")) {
                var configuration = Settings.Load(); Console.WriteLine(DataMaintenance.RefreshGame(configuration)); Console.WriteLine(DataMaintenance.RefreshTranslations(configuration)); Console.WriteLine(DataMaintenance.BridgeStatus(configuration, 0)); return 0;
            }
            if (args.Length == 2 && args[0] == "--capture-song-cover") {
                var artwork = new ConcertArtworkBuilder();
                artwork.SaveCover(new ConcertSnapshot { Active = true, MusicId = int.Parse(args[1]) }).GetAwaiter().GetResult();
                Console.WriteLine(artwork.Status); return artwork.Status.StartsWith("Diagonal artwork ready:") ? 0 : 2;
            }
            if (args.Length == 2 && args[0] == "--bake-portrait") { PortraitBaker.Prepare(int.Parse(args[1]), Settings.Load()).GetAwaiter().GetResult(); Console.WriteLine(PortraitBaker.Folder); return 0; }
            if (args.Length == 2 && args[0] == "--classify-image")
            {
                try
                {
                    using (var image = new Bitmap(args[1]))
                    {
                        var reader = new ScreenReader();
                        string text = reader.Read(image, Settings.Load().OcrLanguage).GetAwaiter().GetResult();
                        var observed = TextClassifier.Classify(text);
                        Console.WriteLine(text);
                        Console.WriteLine("CLASSIFICATION: " + (observed == null ? "Unknown" : Json.Encode(new { observed.Kind, observed.Details, observed.State, observed.Character, observed.Source })));
                    }
                    return 0;
                }
                catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
            }
            if (args.Length == 3 && args[0] == "--export-covers")
            {
                try { CoverExport.Run(args[1], args[2]); return 0; }
                catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
            }
            if (args.Contains("--bridge-probe") || args.Contains("--bridge-pid"))
            {
                var game = GameSession.Find(Settings.Load());
                int selectedPid = args.Contains("--bridge-pid") ? int.Parse(args[Array.IndexOf(args, "--bridge-pid") + 1]) : game == null ? 0 : game.ProcessId;
                using (var bridge = new ConcertReader())
                {
                    var data = selectedPid == 0 ? null : bridge.Read(selectedPid);
                    Console.WriteLine(selectedPid == 0 ? "Game not running" : "Game PID " + selectedPid);
                    Console.WriteLine(bridge.Status);
                    Console.WriteLine("CAREER: " + (bridge.Career == null ? "None" : Json.Encode(bridge.Career)));
                    Console.WriteLine("CAREER METADATA: " + bridge.CareerDiagnostics);
                    Console.WriteLine("CAREER RAW: " + bridge.CareerRaw);
                    Console.WriteLine("CAREER RESULT RAW: " + bridge.CareerResultRaw);
                    Console.WriteLine("RACE: " + (bridge.Race == null ? "None" : Json.Encode(bridge.Race)));
                    Console.WriteLine("MENU: " + (bridge.Menu == null ? "None" : Json.Encode(bridge.Menu)));
                    Console.WriteLine("RACE METADATA: " + bridge.RaceDiagnostics);
                    Console.WriteLine("RACE METRICS STATUS: " + bridge.RaceMetricDiagnostics);
                    if (bridge.Race != null) Console.WriteLine("RACE METRICS: " + Json.Encode(bridge.Race.Team.Select(e => new { e.Name, e.Gate, e.Position, Values = e.Metrics == null ? null : new { e.Metrics.Flags, e.Metrics.Current, e.Metrics.Peak, e.Metrics.Finish, e.Metrics.Seconds, e.Metrics.GapSeconds, e.Metrics.GapLengths } })));
                    if (data != null) Console.WriteLine(Json.Encode(new { data.Active, data.Paused, data.MusicId, data.Title, data.Singers, data.CastIds, data.Position, data.Duration }));
                    if (data != null) {
                        var settings = Settings.Load(); OutfitCatalog.Refresh(settings);
                        if (bridge.Career != null) Console.WriteLine("CAREER CARD: " + Json.Encode(bridge.Career.ToActivity(settings, 1)));
                        Console.WriteLine("CONCERT CARD: " + Json.Encode(data.ToActivity(settings, 1)));
                        for (int i = 0; i < data.PortraitSlots(settings).Length; i++) { data.PortraitIndex = i; Console.WriteLine("CONCERT HOVER " + i + ": " + Json.Encode(((Dictionary<string, object>)((Dictionary<string, object>)data.ToActivity(settings, 1))["assets"])["small_text"])); }
                        Console.WriteLine("ORIGINAL JP NAME: " + GameText.Find("6", 1143, settings));
                    }
                }
                return 0;
            }
            if (args.Contains("--probe-game"))
            {
                foreach (var process in System.Diagnostics.Process.GetProcessesByName("umamusume"))
                using (process)
                {
                    Console.WriteLine("Game PID: " + process.Id);
                    try { Console.WriteLine("HasExited: " + process.HasExited); } catch (Exception ex) { Console.WriteLine("HasExited FAILED: " + ex.Message); }
                    try { Console.WriteLine("StartTime: " + process.StartTime); } catch (Exception ex) { Console.WriteLine("StartTime FAILED: " + ex.Message); }
                    try { Console.WriteLine("Window: " + process.MainWindowHandle); } catch (Exception ex) { Console.WriteLine("Window FAILED: " + ex.Message); }
                    try { Console.WriteLine("Path: " + process.MainModule.FileName); } catch (Exception ex) { Console.WriteLine("Path FAILED: " + ex.Message); }
                }
                var detected = GameSession.Find(new Settings());
                Console.WriteLine("Companion detector: " + (detected == null ? "NOT DETECTED" : "PID " + detected.ProcessId + ", window " + detected.Window + ", path " + detected.Path));
                return 0;
            }
            try { Run().GetAwaiter().GetResult(); Console.WriteLine(passed + " checks passed."); return 0; }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }
        private static void WelcomeUninstallRegression(string folder)
        {
            var fresh = Json.Decode<Settings>(File.ReadAllText(Path.Combine(folder, "settings.json")));
            Check(PresenceApp.ShouldShowWelcome(fresh, true, false, false), "A fresh package shows welcome on interactive launch");
            Check(!PresenceApp.ShouldShowWelcome(fresh, true, false, true) && !PresenceApp.ShouldShowWelcome(fresh, false, false, false), "Minimized and headless launch skip welcome");
            Check(!PresenceApp.ShouldShowWelcome(Json.Decode<Settings>("{}"), true, false, false), "Settings from older installs skip welcome");
            using (var dialog = new WelcomeDialog(false, null)) {
                Check(((System.Windows.Forms.Button)dialog.AcceptButton).Text == "Set up game integration", "Welcome offers game integration setup");
                Check(((System.Windows.Forms.Button)dialog.CancelButton).Text == "Skip for now", "Welcome offers Skip for now");
                Check(ChooseDialogButton(dialog, dialog.CancelButton) == System.Windows.Forms.DialogResult.Cancel, "Skip for now returns to the app");
            }
            using (var dialog = new WelcomeDialog(false, Path.Combine("Games", "Umamusume")))
                Check(ChooseDialogButton(dialog, dialog.AcceptButton) == System.Windows.Forms.DialogResult.OK, "Setup returns the game integration choice");
            fresh.WelcomeDismissed = true;
            Check(!PresenceApp.ShouldShowWelcome(Json.Decode<Settings>(Json.Encode(fresh)), true, false, false), "Saved welcome dismissal survives the next launch");
            using (var dialog = new WelcomeDialog(true, null)) {
                Check(dialog.AcceptButton == dialog.CancelButton && dialog.CancelButton.DialogResult == System.Windows.Forms.DialogResult.Cancel, "Uninstall defaults to Cancel");
                Check(ChooseDialogButton(dialog, dialog.CancelButton) == System.Windows.Forms.DialogResult.Cancel && File.Exists(Path.Combine(folder, "UmaPresence.exe")), "Cancelling the dialog leaves the extracted app intact");
            }
            string executable = Path.Combine(folder, "UmaPresence.exe");
            File.WriteAllText(Path.Combine(folder, "keep-test-file.txt"), "Unrelated file");
            File.WriteAllText(Path.Combine(folder, "presence.log.old"), "Old log fixture");
            using (var cleanup = Program.StartPortableRemoval(new Settings(), executable, 0)) {
                Check(cleanup.WaitForExit(15000) && cleanup.ExitCode == 0, "Full uninstall completes on a disposable extraction");
            }
            Check(Directory.GetFileSystemEntries(folder).Select(Path.GetFileName).SequenceEqual(new[] { "keep-test-file.txt" }), "Cleanup removes installed files, animation sheets and logs while preserving unrelated files");
        }

        private static System.Windows.Forms.DialogResult ChooseDialogButton(WelcomeDialog dialog, System.Windows.Forms.IButtonControl button)
        {
            dialog.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
            dialog.Location = new Point(-20000, -20000);
            dialog.Shown += (sender, args) => dialog.BeginInvoke((Action)button.PerformClick);
            return dialog.ShowDialog();
        }

        private static void WelcomeDialogPreview(string assets, string output)
        {
            Directory.CreateDirectory(output);
            foreach (bool uninstall in new[] { false, true }) {
                foreach (bool animate in new[] { false, true }) {
                    string name = (uninstall ? "uninstall" : "welcome") + (animate ? "-motion" : "-still");
                    string fixturePath = Path.Combine("Games", "DMM", "Long path for checking how the detected game folder wraps at a smaller window width", "Umamusume");
                    using (var dialog = new WelcomeDialog(uninstall, fixturePath, assets, animate))
                    using (var timer = new System.Windows.Forms.Timer { Interval = 875 }) {
                        dialog.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
                        dialog.Location = new Point(-20000, -20000);
                        int frame = 0;
                        byte[] Capture(int index)
                        {
                            using (var bitmap = new Bitmap(dialog.Width, dialog.Height))
                            using (var stream = new MemoryStream()) {
                                dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                                bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                                if (index == 0 || index == 1) File.WriteAllBytes(Path.Combine(output, name + "-" + index + ".png"), stream.ToArray());
                                return stream.ToArray();
                            }
                        }
                        byte[] initial = null, motion = null, nextLoop = null;
                        dialog.Shown += (sender, args) => {
                            foreach (int width in new[] { 780, 480 }) {
                                dialog.FitToScreen(DesktopDesign.Px(dialog, width + 24));
                                foreach (var label in dialog.Controls.OfType<System.Windows.Forms.Label>().Where(label => label.Visible))
                                    Check(label.Height >= WelcomeDialog.MeasureCopy(label, label.Width) && dialog.ClientRectangle.Contains(label.Bounds), name + " copy fits at width " + width);
                                foreach (var button in new[] { dialog.AcceptButton, dialog.CancelButton }.Cast<System.Windows.Forms.Control>())
                                    Check(button.Parent.ClientRectangle.Contains(button.Bounds) && dialog.ClientRectangle.Contains(button.Parent.Bounds), name + " buttons fit at width " + width);
                            }
                            dialog.FitToScreen(DesktopDesign.Px(dialog, 804));
                            initial = Capture(0);
                            timer.Start();
                        };
                        timer.Tick += (sender, args) => {
                            frame++;
                            if (frame == 1) motion = Capture(1);
                            if (frame == 4) nextLoop = Capture(4);
                            if (frame == 5) {
                                timer.Stop();
                                Check(initial.SequenceEqual(motion) != animate, name + " first and moving frames match the motion setting");
                                Check(nextLoop.SequenceEqual(Capture(5)) != animate, name + " keeps moving on the next loop only when enabled");
                                dialog.Close();
                            }
                        };
                        dialog.ShowDialog();
                    }
                }
            }
        }

        private static void ProfilePointsTests()
        {
            byte[] Packet(params int[] fields) { var bytes = new byte[fields.Length * 4]; Buffer.BlockCopy(fields, 0, bytes, 0, bytes.Length); return bytes; }
            var assets = new Func<CareerSnapshot, Dictionary<string, object>>(career => (Dictionary<string, object>)((Dictionary<string, object>)career.ToActivity(new Settings(), 1))["assets"]);
            var detail = new byte[316];
            Packet(0x4c455831, 1, 102503, 66212, 5557, 2176, 1629, 1295, 1124, 1832, 12, 12, 0).CopyTo(detail, 0);
            Check(LegacyDetails.ParsePacket(detail.Take(308).ToArray()).WinsSummary() == "12/12 wins", "Older Legacy packets keep the wins line without a points value");
            Packet(0x3154504c, 0).CopyTo(detail, 308);
            Check(LegacyDetails.ParsePacket(detail).WinsSummary() == "12/12 wins", "A missing saved total leaves the points portion blank");
            Packet(0x3154504c, 7123).CopyTo(detail, 308);
            var legacy = CareerSnapshot.ApplyResult(Packet(0x43525331, 1, 102503, 14, 66212, 73, 7, 5557), null);
            legacy.LegacyDetails = LegacyDetails.ParsePacket(detail);
            Check(assets(legacy)["small_text"].ToString().Replace('\u00a0', ' ').Contains("12/12 W • 7,123 pts"), "Legacy displays saved skill points beside wins");
            var profile = new byte[120];
            Packet(0x32444c52, 1, 102503, 1).CopyTo(profile, 0);
            Encoding.UTF8.GetBytes("Trainer A").CopyTo(profile, 16);
            Check(legacy.ApplyRivalProfile(profile) && legacy.ToState().Details.EndsWith("— Rivals") && assets(legacy)["small_text"].ToString().Contains("7,123"), "Rival details retain the compact saved points row");
            var partner = CareerSnapshot.ApplyResult(Packet(0x43525331, 1, 102503, 14, 0, 73, 46, 5557), legacy);
            Array.Clear(detail, 12, 4); Array.Clear(detail, 20, 20);
            partner.LegacyDetails = LegacyDetails.ParsePacket(detail);
            Array.Clear(profile, 12, 4);
            Check(partner.ApplyRivalProfile(profile) && partner.ToState().Details.EndsWith("— Practice Partner") && partner.ResultLine == "12/12 W • 7,123 pts • 0 white factors", "Bare Practice Partner retains its own points and summary");
            string partnerHover = assets(partner)["large_text"].ToString();
            Check(partnerHover.Contains("[") && partnerHover.StartsWith("Trainer\u00a0:\u00a0Trainer\u00a0A\u2028") && !partnerHover.Contains("White") && Encoding.UTF8.GetByteCount(partnerHover) <= 128, "Bare Practice Partner hover puts the spaced trainer label before the name and costume");
            var normalPartner = CareerSnapshot.ApplyResult(Packet(0x43525331, 1, 102503, 14, 66212, 73, 39, 5557), null);
            Check(normalPartner.ApplyRivalProfile(profile) && assets(normalPartner)["large_text"].ToString() == partnerHover, "Regular and Bare Practice Partner share the portrait hover layout");
            normalPartner.RivalTrainer = "Long Trainer Display Name";
            string longHover = assets(normalPartner)["large_text"].ToString();
            Check(longHover.StartsWith("Long\u00a0Trainer\u00a0Display\u00a0Name\u2028") && longHover.Contains("[") && Encoding.UTF8.GetByteCount(longHover) <= 128, "Long trainer names omit the label and retain the complete name when it fits");
            legacy.RivalTrainer = "おにぎり";
            legacy.RivalMarginKind = 5;
            legacy.RivalGapLengths = 2;
            var rivalPayload = (Dictionary<string, object>)legacy.ToState().ToActivity(new Settings(), 1);
            var rivalAssets = (Dictionary<string, object>)rivalPayload["assets"];
            Check(rivalAssets["large_text"].ToString().Replace("\u2060", "").Replace('\u202f', ' ').Replace('\u00a0', ' ') == "Trainer : おにぎり\u20281st 👑 • Won by 2 L", "Japanese Rival trainer and result stay separated after payload formatting");
            legacy.RivalTrainer = "Long Trainer Display Name";
            Check(assets(legacy)["large_text"].ToString().StartsWith("Long\u00a0Trainer\u00a0Display\u00a0Name\u2028"), "Rival hover drops the label for long trainer names");
            var roomProfile = new byte[128];
            profile.CopyTo(roomProfile, 0);
            Packet(4).CopyTo(roomProfile, 12);
            Packet(0x31544c52).CopyTo(roomProfile, 120);
            BitConverter.GetBytes(0.13f).CopyTo(roomProfile, 124);
            Check(legacy.ApplyRivalProfile(roomProfile) && assets(legacy)["large_text"].ToString().Replace('\u202f', ' ').Contains("4th\u00a0•\u00a0+0.13s to 1st"), "Room Rival separates placement and recorded finish-time gap with full-width spaces around the bullet");
            Check(OutfitCatalog.DressForCard(100903) == 100902 && OutfitCatalog.CareerPortrait(100903, new Settings()).EndsWith("/100903.png"), "Daiwa Scarlet’s festival card resolves its own alternate portrait");
            var reopened = CareerSnapshot.ApplyResult(Packet(0x43525331, 1, 102503, 14, 66212, 73, 7, 5557), partner);
            Check(NativeActivityPolicy.CareerContextChanged(partner, reopened) && !reopened.PracticePartner && reopened.RivalTrainer == null && reopened.LegacyDetails == null, "Switching back to owned Legacy clears Practice Partner data");
        }

        private static async Task Run()
        {
            StandaloneRotationTests();
            StandaloneOwnershipTests();
            StandaloneConfigurationTests();
            int officialKind; float officialLengths;
            Check(OfficialMarginInspection.Label("$(nb)9 lengths", out officialKind, out officialLengths) && officialKind == 0 && officialLengths == 9 && OfficialMarginInspection.Label("<b>$(nb)クビ</b>", out officialKind, out officialLengths) && officialKind == 3 && !OfficialMarginInspection.Label("+0.03s", out officialKind, out officialLengths), "Official margin reader handles captured Hachimi markup and Japanese close labels without converting time gaps");
            string officialFixture = File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "bridge", "fixtures", "ikuno-official-rows-v26.txt"));
            Func<RaceSnapshot> officialRace = () => new RaceSnapshot { Results = true, Program = 100901, ProgramType = 2, Runners = 18, Team = new[] { new RaceEntry { CharacterId = 1063, Gate = 6, Position = 1, Metrics = new RaceMetrics { Flags = 14, Seconds = 141.4174f, Finish = 24.26f, Peak = 24.26f } } } };
            var capturedOfficialRace = officialRace();
            Check(OfficialMarginInspection.Apply(officialFixture, capturedOfficialRace) && capturedOfficialRace.Team[0].Metrics.Margin == 4 && capturedOfficialRace.Team[0].Metrics.Tooltip(capturedOfficialRace.Team[0], true).Replace('\u00a0', ' ').Contains("The Rest Nowhere"), "Captured active Ikuno result supplies official Distance winner text with matching race, gate, character and finish time");
            string protectedDistance = capturedOfficialRace.Team[0].Metrics.Tooltip(capturedOfficialRace.Team[0], true);
            var distanceRows = protectedDistance.Split(' ').Select(row => row.Replace('\u00a0', ' ').Replace("\u2060", "").Trim('\u2007')).ToArray();
            Check(protectedDistance.Contains("\u2060|\u2060") && protectedDistance.Contains("\u2060/\u2060") && protectedDistance.Split(' ')[2].StartsWith("\u2007\u2007") && protectedDistance.Split(' ')[2].EndsWith("\u2007\u2007"), "Distance speed row protects punctuation and uses balanced spacing to keep its time separate");
            Check(distanceRows.Length == 4 && distanceRows[0] == "Ikuno Dictus — 1st" && distanceRows[1] == "The Rest Nowhere" && distanceRows[2] == "C: 24.3 | P: 24.3 m/s" && distanceRows[3] == "2:21.4", "Official Distance winner uses the requested four separately protected rows and preserves time");
            var numericalWinner = new RaceMetrics { Flags = 110, Margin = 0, GapLengths = 9, Seconds = 116.885529f, Finish = 24.44f, Peak = 24.7f }.Tooltip(new RaceEntry { CharacterId = 1063, Position = 1 }, true).Split(' ').Last().Replace('\u00a0', ' ');
            Check(numericalWinner == "1:56.9 • Won by 9 lengths", "Numeric official winning margin stays on the same protected row as race time");
            var otherOfficialRace = officialRace(); otherOfficialRace.Team[0].Metrics.Seconds += 1;
            Check(!OfficialMarginInspection.Apply(officialFixture, otherOfficialRace) && !OfficialMarginInspection.Apply(officialFixture.Replace("VALUE 100901", "VALUE 100902"), officialRace()) && !OfficialMarginInspection.Apply(officialFixture.Replace("VALUE 1063", "VALUE 1001"), officialRace()), "Official result inspection rejects stale finish times, wrong race IDs and mismatched horse identity");
            Check(DataMaintenance.ValidAsset("concert_1195") && !DataMaintenance.ValidAsset("https://example.com/image.png") && !DataMaintenance.ValidAsset("concert 1195") && !DataMaintenance.ValidAsset("../settings"), "Artwork registration rejects URLs and paths rather than treating them as uploaded Discord asset names");
            bool invalidAssetRejected = false;
            try { DataMaintenance.RegisterAsset(new Settings(), 0, "concert_1195"); } catch (ArgumentException) { invalidAssetRejected = true; }
            Check(invalidAssetRejected, "Invalid song registration cannot mutate settings or save a mapping");
            SongCatalogRefreshTests();
            JapaneseTooltipTests();
            ConcertTests();
            RaceTests();
            var careerPacket = new byte[48];
            int[] careerValues = { unchecked((int)0x43415231), 1, 100101, 25, 1, 500, 300, 400, 250, 320, 120, 0 };
            for (int i = 0; i < careerValues.Length; i++) Buffer.BlockCopy(BitConverter.GetBytes(careerValues[i]), 0, careerPacket, i * 4, 4);
            var liveCareer = CareerSnapshot.Parse(careerPacket);
            CareerExtraTests(liveCareer);
            AlmondLegacyTests();
            Check(liveCareer != null && liveCareer.Stats == "500/300/400/250/320 | 120", "Native career stats use the requested compact separators in game order");
            Check(CareerSnapshot.TurnText(1) == "Y1, Early April" && CareerSnapshot.TurnText(25) == "Y2, Early January" && CareerSnapshot.TurnText(72) == "Y3, Late December", "Career turn labels match UmaLauncher including pre-debut turns");
            string liveCareerPayload = Json.Encode(liveCareer.ToActivity(new Settings(), 100));
            Check(liveCareerPayload.Contains("Training - Y2, Early January") && liveCareerPayload.Contains("500/300/400/250/320 | 120") && liveCareerPayload.Replace('\u00a0', ' ').Contains("Founding of the URA Finale!"), "Career payload matches UmaLauncher layout with local scenario tooltip");
            Check(CareerLabels.Scenario(14, new Settings()).Contains("Tracen Ramen") && CareerLabels.Outfit(100601, new Settings()).Contains("Starlight Beat"), "Current scenario and outfit names resolve locally without a browser");
            string pisaHover = CareerLabels.PortraitTooltip("Victoire Pisa", 114301, new Settings());
            Check(pisaHover.Split(' ').Length == 2 && pisaHover.StartsWith("Victoire\u00a0Pisa [") && pisaHover.Contains("Lueur\u00a0Angélique") && pisaHover.EndsWith("]"), "Career portrait keeps Pisa's complete bracketed costume together using the agreed space method");
            Check(SongCatalog.Resource<Dictionary<string, Dictionary<string, string>>>("career-labels.json")["5"].Keys.All(id => { string hover = CareerLabels.PortraitTooltip("Victoire Pisa", int.Parse(id), new Settings()); return Encoding.UTF8.GetByteCount(hover) <= 128 && (!hover.Contains("[") || hover.EndsWith("]")) && !hover.Contains('\n'); }), "All career costume labels preserve bracket endings and protected name/outfit groups within the payload budget");
            var careerResult = new byte[32]; int[] finishedValues = { unchecked((int)0x43525331), 1, 100601, 14, 29095, 27, 3, 39 };
            for (int i = 0; i < finishedValues.Length; i++) Buffer.BlockCopy(BitConverter.GetBytes(finishedValues[i]), 0, careerResult, i * 4, 4);
            var completedCareer = CareerSnapshot.ApplyResult(careerResult, liveCareer);
            Check(completedCareer.Results && completedCareer.ResultLine == "UE • 29,095 • 27 skills", "Career result shows evaluation rank, formatted score and acquired skill count");
            Check(CareerRanks.Gauge(29095) == "UE ━━◆── UE1" && CareerRanks.ProgressText(29095) == "49% toward UE1" && CareerRanks.Label(29400) == "UE1", "Rank marker uses installed game thresholds and advances only at the next rank boundary");
            var resultPayload = Json.Decode<Dictionary<string, object>>(Json.Encode(completedCareer.ToActivity(new Settings(), 12345)));
            var resultAssets = (Dictionary<string, object>)resultPayload["assets"];
            Check(resultPayload["details"].ToString() == "Oguri Cap — Tracen Ramen" && resultAssets["large_text"].ToString().Replace('\u00a0', ' ').Contains("UE ━━◆── UE1") && resultAssets["small_text"].ToString().Replace('\u00a0', ' ').Contains("Tracen Ramen"), "Result heading names the Uma and scenario; portrait hover shows the marker and circle keeps scenario tooltip");
            var rivalResult = (byte[])careerResult.Clone(); Buffer.BlockCopy(BitConverter.GetBytes(7), 0, rivalResult, 24, 4);
            var rivalCareer = CareerSnapshot.ApplyResult(rivalResult, null);
            var rivalProfile = new byte[120];
            Buffer.BlockCopy(BitConverter.GetBytes(unchecked((int)0x32444c52)), 0, rivalProfile, 0, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(1), 0, rivalProfile, 4, 4); Buffer.BlockCopy(BitConverter.GetBytes(rivalCareer.Card), 0, rivalProfile, 8, 4); Buffer.BlockCopy(BitConverter.GetBytes(1), 0, rivalProfile, 12, 4);
            byte[] rivalTrainer = Encoding.UTF8.GetBytes("Trainer A"); Buffer.BlockCopy(rivalTrainer, 0, rivalProfile, 16, rivalTrainer.Length);
            Buffer.BlockCopy(BitConverter.GetBytes(5), 0, rivalProfile, 112, 4); Buffer.BlockCopy(BitConverter.GetBytes(2.75f), 0, rivalProfile, 116, 4);
            Check(rivalCareer.ApplyRivalProfile(rivalProfile), "Versioned Rival profile packet accepts a verified numeric margin");
            var rivalProfilePayload = (Dictionary<string, object>)rivalCareer.ToActivity(new Settings(), 12345);
            var rivalProfileAssets = (Dictionary<string, object>)rivalProfilePayload["assets"];
            string rivalProfileHover = rivalProfileAssets["large_text"].ToString().Replace('\u00a0', ' ');
            Check(rivalProfileHover.Replace("\u202f", " ") == "Trainer : Trainer A\u20281st 👑 • Won by 2.75 L" && !rivalProfileHover.Contains("Starlight Beat") && !rivalProfileHover.Contains("toward") && !rivalProfileHover.Contains("acquired") && rivalProfilePayload["state"].ToString() == completedCareer.ResultLine, "Rival portrait hover has only trainer and placement rows while rank, evaluation and skills stay on the main card");
            var oldRivalProfile = new byte[112]; Buffer.BlockCopy(BitConverter.GetBytes(unchecked((int)0x31444c52)), 0, oldRivalProfile, 0, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(1), 0, oldRivalProfile, 4, 4); Buffer.BlockCopy(BitConverter.GetBytes(rivalCareer.Card), 0, oldRivalProfile, 8, 4); Buffer.BlockCopy(BitConverter.GetBytes(4), 0, oldRivalProfile, 12, 4); Buffer.BlockCopy(rivalTrainer, 0, oldRivalProfile, 16, rivalTrainer.Length);
            var oldBridgeCareer = CareerSnapshot.ApplyResult((byte[])rivalResult.Clone(), completedCareer);
            bool oldProfileAccepted = oldBridgeCareer.ApplyRivalProfile(oldRivalProfile) && oldBridgeCareer.RivalMarginKind == 0;
            var oldProfileAssets = (Dictionary<string, object>)((Dictionary<string, object>)oldBridgeCareer.ToActivity(new Settings(), 12345))["assets"];
            Check(oldProfileAccepted && oldProfileAssets["large_text"].ToString().Replace('\u00a0', ' ') == "Trainer : Trainer A\u20284th", "Older installed bridge profile packets remain usable without an unverified margin");
            oldBridgeCareer = CareerSnapshot.ApplyResult((byte[])careerResult.Clone(), oldBridgeCareer);
            Check(oldBridgeCareer.RivalTrainer == null && oldBridgeCareer.RivalPlacement == 0, "A new profile sample clears stale Rival context when the dialog closes");
            var trainingAssets = (Dictionary<string, object>)Json.Decode<Dictionary<string, object>>(Json.Encode(new CareerSnapshot { Card = 100601, Scenario = 14 }.ToActivity(new Settings(), 12345)))["assets"];
            var policyConcert = new ActivityState { Concert = new ConcertSnapshot { Active = true, MusicId = 1176 } };
            var policyRace = new ActivityState { Race = new RaceSnapshot { Preparing = true } };
            Check(NativeActivityPolicy.Select(liveCareer, null, null).Career == liveCareer && NativeActivityPolicy.Select(completedCareer, null, null).Career.Results, "Training and evaluation select the current native career phase");
            Check(NativeActivityPolicy.Select(liveCareer, null, policyRace) == policyRace && NativeActivityPolicy.Select(completedCareer, policyConcert, policyRace) == policyConcert, "A race or concert supersedes cached career data during transitions");
            foreach (var oldNativeState in new[] { liveCareer.ToState(), completedCareer.ToState(), policyRace, policyConcert })
            {
                var cleared = NativeActivityPolicy.ClearEnded(oldNativeState, NativeActivityPolicy.Select(null, null, null));
                Check(cleared.Career == null && cleared.Race == null && cleared.Concert == null && cleared.Kind == "Playing" && NativeActivityPolicy.ClearEnded(oldNativeState, policyConcert) == oldNativeState, "Leaving " + (oldNativeState.Career != null ? oldNativeState.Career.Results ? "career results" : "training" : oldNativeState.Race != null ? "race" : "concert") + " clears stale data even without a foreground screen scan");
            }
            Check(resultAssets["large_image"].Equals(trainingAssets["large_image"]) && resultAssets["small_image"].Equals(trainingAssets["small_image"]) && Convert.ToInt64(((Dictionary<string, object>)resultPayload["timestamps"])["start"]) == 12345, "Career result preserves both portrait/icon and original game-launch timer");
            var legacyPacket = (byte[])careerResult.Clone(); Buffer.BlockCopy(BitConverter.GetBytes(7), 0, legacyPacket, 24, 4);
            var legacyCareer = CareerSnapshot.ApplyResult(legacyPacket, null);
            string extraFixture = File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "bridge", "fixtures", "legacy-extras-live.txt"));
            var legacyDetails = LegacyDetails.ParseInspection(extraFixture);
            Check(legacyDetails != null && legacyDetails.Speed == 2176 && legacyDetails.Wins == 16 && legacyDetails.Races == 16 && legacyDetails.Factors.Length == 28, "Verified live veteran record supplies stats, wins and inheritance factors");
            Check(legacyDetails.FactorSummary == "Stam2★ Turf2★ Unique2★", "Factor stars use master-data rarity rather than the zero factor-level getter");
            Check(LegacyDetails.WhiteCount(legacyDetails.Factors) == 25 && LegacyDetails.WhiteCount(new[] { 202, 1102, 10250302 }) == 0 && LegacyDetails.WhiteCount(new[] { int.MaxValue }) == -1, "White-factor counts exclude blue, red and unique factors and reject unknown classifications");
            string gp1 = File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "bridge", "fixtures", "legacy-gp1-live.txt"));
            string gp2 = File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "bridge", "fixtures", "legacy-gp2-live.txt"));
            var family = LegacyDetails.ParseInspection(extraFixture + gp1 + gp2);
            Check(family != null && LegacyDetails.WhiteCount(family.GrandparentFactors[0]) == 25 && LegacyDetails.WhiteCount(family.GrandparentFactors[1]) == 23 && family.Tooltip("Tracen Ramen").Replace('\u00a0', ' ').Contains("White factors: 25") && family.Tooltip("Tracen Ramen").Replace('\u00a0', ' ').Contains("GP1: 25 • GP2: 23"), "Captured grandparent arrays provide separate white-factor counts without adding them into the selected Uma total");
            var waitingLegacy = new CareerSnapshot { Card = 102503, EvaluationScore = 66212, Legacy = true };
            var readyLegacy = new CareerSnapshot { Card = 102503, EvaluationScore = 66212, Legacy = true, LegacyDetails = legacyDetails };
            var familyLegacy = new CareerSnapshot { Card = 102503, EvaluationScore = 66212, Legacy = true, LegacyDetails = family };
            Check(NativeActivityPolicy.CareerContextChanged(waitingLegacy, readyLegacy) && NativeActivityPolicy.CareerContextChanged(readyLegacy, familyLegacy) && !NativeActivityPolicy.CareerContextChanged(familyLegacy, new CareerSnapshot { Card = 102503, EvaluationScore = 66212, Legacy = true, LegacyDetails = LegacyDetails.ParseInspection(extraFixture + gp1 + gp2) }), "Arriving Legacy hover data refreshes immediately, while identical repeated samples keep the normal update interval");
            Check(LegacyDetails.ParseInspection(extraFixture.Replace("_trainedCharaId\nVALUE 5557", "_trainedCharaId\nVALUE 5558")) == null, "Legacy extras reject a switched record ID");
            Check(Encoding.UTF8.GetByteCount(legacyDetails.Tooltip("Tracen Ramen")) <= 128 && legacyDetails.Tooltip("Tracen Ramen").Contains("2176/1629/1295/1124/1832"), "Legacy hover includes five stats and remains within the Discord byte limit");
            var extraBytes = new byte[308];
            int[] extraValues = { 0x4c455831, 1, 102503, 66212, 5557, 2176, 1629, 1295, 1124, 1832, 16, 16, 3, 202, 1102, 10250302 };
            for (int i = 0; i < extraValues.Length; i++) Buffer.BlockCopy(BitConverter.GetBytes(extraValues[i]), 0, extraBytes, i * 4, 4);
            var nativeExtra = LegacyDetails.ParsePacket(extraBytes);
            Check(nativeExtra != null && nativeExtra.FactorSummary == legacyDetails.FactorSummary && nativeExtra.Matches(new CareerSnapshot { Legacy = true, Card = 102503, EvaluationScore = 66212 }) && !nativeExtra.Matches(new CareerSnapshot { Legacy = true, Card = 102503, EvaluationScore = 66000 }), "Native Legacy extras match the selected evaluation and preserve verified factor stars");
            var gpBytes = new byte[552];
            int[] gpHeader = { 0x4c475031, 1, 102503, 66212, 5557, 0 };
            for (int i = 0; i < gpHeader.Length; i++) Buffer.BlockCopy(BitConverter.GetBytes(gpHeader[i]), 0, gpBytes, i * 4, 4);
            for (int group = 0; group < 2; group++) {
                int offset = 24 + group * 264;
                Buffer.BlockCopy(BitConverter.GetBytes(family.GrandparentCards[group]), 0, gpBytes, offset, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(family.GrandparentFactors[group].Length), 0, gpBytes, offset + 4, 4);
                for (int i = 0; i < family.GrandparentFactors[group].Length; i++) Buffer.BlockCopy(BitConverter.GetBytes(family.GrandparentFactors[group][i]), 0, gpBytes, offset + 8 + i * 4, 4);
            }
            nativeExtra.ApplyGrandparents(gpBytes);
            Check(LegacyDetails.WhiteCount(nativeExtra.GrandparentFactors[0]) == 25 && LegacyDetails.WhiteCount(nativeExtra.GrandparentFactors[1]) == 23, "Native grandparent packet preserves the two independent captured factor counts");
            Buffer.BlockCopy(BitConverter.GetBytes(5558), 0, gpBytes, 16, 4);
            var otherExtra = LegacyDetails.ParsePacket(extraBytes); otherExtra.ApplyGrandparents(gpBytes);
            Check(otherExtra.GrandparentFactors.All(ids => ids == null), "Grandparent telemetry from another selected record is rejected");
            extraBytes[4] = 0; Check(LegacyDetails.ParsePacket(extraBytes) == null, "Inactive native Legacy extras clear without retaining the previous veteran");
            Check(OutfitCatalog.DressForCard(102502) == 102513 && OutfitCatalog.DressForCard(102503) == 102526 && OutfitCatalog.CareerPortrait(102503, new Settings()).Contains("1025_102526"), "Career portraits map both alternate Cafe cards to their actual race costumes");
            Check(NativeActivityPolicy.Select(legacyCareer, policyConcert, policyRace).Career.Legacy && NativeActivityPolicy.Select(null, policyConcert, policyRace) == policyConcert, "An open Legacy detail takes priority and closing it restores the active concert");
            Check(NativeActivityPolicy.CareerContextChanged(legacyCareer, liveCareer) && NativeActivityPolicy.CareerContextChanged(liveCareer, completedCareer) && NativeActivityPolicy.CareerContextChanged(liveCareer, new CareerSnapshot { Card = 102201, Scenario = 14 }) && !NativeActivityPolicy.CareerContextChanged(liveCareer, liveCareer), "Career, result and Legacy context changes request an immediate presence refresh");
            const string homeController = "QUERY SceneManager/_currentViewController\nCLASS Gallop.HomeHubViewController\nQUERY SceneManager/_currentViewController/ChildCurrentController\nCLASS Gallop.HomeViewController\n";
            var nativeHome = NativeMenuSnapshot.ParseInspection(homeController);
            var nativeVeterans = NativeMenuSnapshot.ParseInspection(homeController.Replace("Gallop.HomeViewController", "Gallop.TrainedCharaListViewController"));
            Check(nativeHome.ToState().Details == "Main Menu" && nativeHome.ToState().State == "Home" && nativeVeterans.ToState().Details == "Veterans", "Verified Home and Veterans controllers resolve English menu labels without OCR");
            Check(NativeMenuSnapshot.ParseInspection("QUERY SceneManager/_currentViewController\nNULL\n") == null && NativeMenuSnapshot.ParseInspection("QUERY SceneManager/_currentViewController\nCLASS Gallop.HomeHubViewController\n") == null, "Missing and still-loading menu controllers cannot retain a prior menu");
            Check(new NativeMenuSnapshot { Root = "NewHub", View = "NewUnmappedViewController" }.ToState().Details == "Playing Umamusume", "Unknown native controllers use neutral text instead of a guessed screen label");
            Check(NativeActivityPolicy.Select(legacyCareer, policyConcert, policyRace, nativeHome).Career.Legacy && NativeActivityPolicy.Select(liveCareer, null, policyRace, nativeHome) == policyRace && NativeActivityPolicy.Select(liveCareer, null, null, nativeHome).Career == liveCareer && NativeActivityPolicy.Select(null, null, null, nativeHome).Details == "Main Menu", "Legacy, concerts, races and career retain priority over native menus");
            var nativeMenuBytes = new byte[200]; Buffer.BlockCopy(BitConverter.GetBytes(0x4d454e31), 0, nativeMenuBytes, 0, 4); nativeMenuBytes[4] = 1;
            Buffer.BlockCopy(Encoding.ASCII.GetBytes("HomeHubViewController"), 0, nativeMenuBytes, 8, 21); Buffer.BlockCopy(Encoding.ASCII.GetBytes("TrainedCharaListViewController"), 0, nativeMenuBytes, 104, 30);
            Check(NativeMenuSnapshot.Parse(nativeMenuBytes).ToState().Details == "Veterans", "Native menu packet preserves the active child controller");
            nativeMenuBytes[4] = 0; Check(NativeMenuSnapshot.Parse(nativeMenuBytes) == null, "Inactive native menu packets clear after leaving the view");
            var legacyPayload = Json.Decode<Dictionary<string, object>>(Json.Encode(legacyCareer.ToActivity(new Settings(), 12345)));
            Check(legacyCareer.Legacy && legacyCareer.ToState().Kind == "Legacy" && legacyPayload["details"].ToString() == "Oguri Cap — Legacy" && legacyPayload["state"].ToString() == "UE • 29,095 • 27 skills", "Opened Legacy detail uses its own record with the requested rank, evaluation and skills layout");
            string legacyInspection = File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "bridge", "fixtures", "legacy-inspection.txt"));
            var inspectedLegacy = LegacyInspectionReader.Parse(legacyInspection);
            Check(inspectedLegacy != null && inspectedLegacy.Card == 102201 && inspectedLegacy.EvaluationScore == 69833 && inspectedLegacy.SkillCount == 71 && inspectedLegacy.Legacy, "Captured veteran dialog data resolves the selected Fine Motion independently of career data");
            Check(LegacyInspectionReader.Parse(legacyInspection.Replace("_trainedCharaId\nVALUE 17", "_trainedCharaId\nVALUE 18")) == null && LegacyInspectionReader.Parse(legacyInspection.Replace("/Info/Id\nVALUE 102201", "/Info/Id\nVALUE 102202")) == null, "Switching the selected veteran or portrait rejects a previous record instead of showing the wrong Uma");
            Check(LegacyInspectionReader.Parse(legacyInspection.Replace("FIELD _isDispClosed : Boolean = false", "FIELD _isDispClosed : Boolean = true")) == null && LegacyInspectionReader.Parse(legacyInspection.Replace("FIELD _isDispClosing : Boolean = false", "FIELD _isDispClosing : Boolean = true")) == null, "Closing or closed veteran dialogs cannot retain a presence");
            Check(LegacyInspectionReader.Parse(legacyInspection.Replace("CLASS Gallop.DialogTrainedCharacterDetail", "CLASS Gallop.DialogOther")) == null && LegacyInspectionReader.Parse("") == null, "An unrelated or missing dialog cannot become Legacy presence");
            legacyPacket[4] = 0; Check(CareerSnapshot.ApplyResult(legacyPacket, null) == null, "Closed Legacy detail cannot retain a stale activity");
            Buffer.BlockCopy(BitConverter.GetBytes(-1), 0, careerResult, 20, 4); Buffer.BlockCopy(BitConverter.GetBytes(1), 0, careerResult, 24, 4);
            Check(!CareerSnapshot.ApplyResult(careerResult, liveCareer).ResultLine.Contains("skills") && !liveCareer.Results, "Unknown acquired count is omitted; result parsing never mutates the training snapshot");
            careerResult[4] = 0; Check(CareerSnapshot.ApplyResult(careerResult, liveCareer) == liveCareer, "Inactive completed-career record cannot replace training"); careerResult[4] = 1;
            Buffer.BlockCopy(BitConverter.GetBytes(-1), 0, careerResult, 16, 4); Check(CareerSnapshot.ApplyResult(careerResult, liveCareer) == liveCareer, "Invalid result evaluation is rejected instead of inventing a rank");
            careerPacket[4] = 0;
            Check(CareerSnapshot.Parse(careerPacket) == null, "Leaving career mode cannot publish cached work-data stats");
            Check(CareerSnapshot.ParseAvailable(careerPacket, "Scene: Home ") == null, "Partial career data is rejected outside the career scene");
            Buffer.BlockCopy(BitConverter.GetBytes(0), 0, careerPacket, 12, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(0), 0, careerPacket, 40, 4);
            var partialCareer = CareerSnapshot.ParseAvailable(careerPacket, "Scene: SingleMode [WorkDataManager]");
            Check(partialCareer != null && partialCareer.Title == "Training" && partialCareer.Stats == "500/300/400/250/320", "Verified partial career data shows stats without inventing date or skill points");
            Buffer.BlockCopy(BitConverter.GetBytes(7347), 0, careerPacket, 40, 4);
            Check(CareerSnapshot.ParseAvailable(careerPacket, "Scene: SingleMode ").Stats == "500/300/400/250/320 | 7347", "Verified skill points display even while the career date remains unavailable");
            careerPacket[4] = 1;
            Buffer.BlockCopy(BitConverter.GetBytes(-1), 0, careerPacket, 20, 4);
            Check(CareerSnapshot.Parse(careerPacket) == null, "Invalid career stats are rejected");
            var protectedProcess = GameSession.ReadCandidate(123, () => new IntPtr(456), () => new DateTime(2026, 10, 24, 10, 0, 0, DateTimeKind.Utc), () => { throw new System.ComponentModel.Win32Exception(5); });
            Check(protectedProcess.ProcessId == 123 && protectedProcess.Window == new IntPtr(456), "Protected game remains detected when executable path is denied");
            var inaccessibleMetadata = GameSession.ReadCandidate(789, () => { throw new System.ComponentModel.Win32Exception(5); }, () => { throw new System.ComponentModel.Win32Exception(5); }, () => { throw new System.ComponentModel.Win32Exception(5); });
            Check(inaccessibleMetadata.ProcessId == 789 && inaccessibleMetadata.Started > 0, "Denied optional metadata never discards a detected game");
            Check(CharacterCatalog.Entries.Count > 100, "Offline character metadata embedded");
            Check(CharacterCatalog.FindIn("Training Special Week")?.Id == "1001", "Recognizes character name");
            Check(CharacterCatalog.FindIn("Special Week Tokai Teio") == null, "Does not guess when multiple characters appear");
            Check(CharacterCatalog.HasIcon("1001"), "Uses verified Discord artwork key");
            var training = TextClassifier.Classify("Special Week\nClassic Year Early March\nSpeed 500 Stamina 300 Power 400 Guts 250 Wit 320\nEnergy 80\nSkill Pts 120\nNext Goal");
            Check(training != null && training.Kind == "Training" && training.CharacterId == "1001", "English training detection and character");
            Check(training.State == "Classic Year Early March", "Training date extracted from restricted pattern");
            Check(training.CareerStats == "500 / 300 / 400 / 250 / 320 | Skill Pt 120", "Career card uses five stats in game order and skill points");
            var careerActivity = Json.Encode(training.ToActivity(new Settings { ApplicationId = "1563519446503522344" }, 100));
            Check(careerActivity.Contains("Classic Year Early March") && careerActivity.Contains("Special Week • 500 / 300 / 400 / 250 / 320 | Skill Pt 120") && careerActivity.Contains("wsrv.nl"), "Custom Discord application receives career date, character, stats and portrait");
            Check(TextClassifier.Classify("スピード スタミナ パワー 根性 賢さ 体力 やる気 次の目標").Kind == "Training", "Japanese training detection");
            Check(TextClassifier.Classify("Speed Power Stamina Guts Wit") == null, "Stats alone aren't treated as training");
            string careerPanelText = "Guts\nPower\nTurf\nDirt\nSprint\nMile\nMedium\nLong\n[Starlight Beat]\nSkills\nSkill Pt 7347\nRunner Leader\nBetweener Chaser";
            var careerPanel = TextClassifier.Classify(careerPanelText);
            Check(careerPanel != null && careerPanel.Kind == "Training" && careerPanel.Details == "Career mode", "Reported career panel recognizes singular Skill Pt with partially visible stats");
            Check(careerPanel.CareerStats == "Skill Pt 7347", "Partially visible career stats publish only recognized skill points");
            Check(new ActivityDetector().Observe(careerPanel, DateTime.UtcNow, true).Kind == "Training", "Strong career controls confirm on the first visible scan");
            Check(TextClassifier.Classify("Guts Power Turf Dirt Sprint Mile Medium Long Runner Leader Betweener Chaser") == null, "Aptitude panels without career skill points do not invent career mode");
            Check(TextClassifier.Classify("Home Story Race Gacha Training") == null, "Generic menu labels don't invent activity");
            Check(TextClassifier.Classify("Main Story Character Stories").Kind == "Story", "Story detection");
            Check(TextClassifier.Classify("Race Results").Kind == "Race", "Race results detection");
            Check(TextClassifier.Classify("Live Theater Song Selection").Kind == "Live", "Live theater detection");
            Check(TextClassifier.Classify("Support Card Scout").Kind == "Gacha", "Scout detection");
            using (var image = new Bitmap(540, 960))
            {
                Check(PixelClassifier.Classify(image) == null, "Unknown screenshot has no fabricated state");
                for (int i = 0; i < 5; i++)
                {
                    PixelClassifier.PaintTestMenu(image, i);
                    Check(PixelClassifier.Classify(image) != null, "Navigation detection tab " + i);
                }
            }
            var detector = new ActivityDetector();
            var now = DateTime.UtcNow;
            Check(detector.Observe(training, now, true).Kind == "Playing", "One OCR observation doesn't flicker the presence");
            Check(detector.Observe(training, now.AddSeconds(8), true).Kind == "Training", "Two OCR observations confirm training");
            Check(detector.Observe(null, now.AddSeconds(80), false).Kind == "Training", "Minimized game retains last known state");
            Check(detector.Observe(null, now.AddSeconds(90), true).Kind == "Playing", "Unknown visible screen expires stale activity");
            var settings = new Settings();
            var activity = Json.Decode<Dictionary<string, object>>(Json.Encode(training.ToActivity(settings, 12345)));
            Check(activity["name"].ToString() == "Uma Musume : Pretty Derby", "Game display name overrides developer application name including colon");
            Check(((Dictionary<string, object>)activity["assets"])["large_image"].ToString() == "chara_1001", "Character artwork in actual Discord payload");
            Check(!activity.ContainsKey("buttons"), "No external links or GameTora buttons");
            settings.ShowCharacter = false;
            activity = Json.Decode<Dictionary<string, object>>(Json.Encode(training.ToActivity(settings, 12345)));
            Check(!activity["state"].ToString().Contains("Special Week"), "Hide-character option honored");
            Check(Encoding.UTF8.GetByteCount(ActivityState.Limit(new string('馬', 100))) <= 128, "UTF-8 activity byte limit");
            Check(!char.IsHighSurrogate(ActivityState.Limit(string.Concat(Enumerable.Repeat("🐎", 60))).Last()), "No split Unicode surrogate pair");
            await ProtocolTest();
            using (var invalid = new MemoryStream(new byte[] { 1, 0, 0, 0, 255, 255, 255, 127 }))
            {
                bool rejected = false;
                try { await DiscordIpc.ReadFrame(invalid); } catch (InvalidDataException) { rejected = true; }
                Check(rejected, "Rejects oversized IPC frames");
            }
            await OcrTest();
        }

        private static void SongCatalogRefreshTests()
        {
            var bundledSongs = SongCatalog.Resource<List<SongEntry>>("songs.json");
            try {
                SongCatalog.SetGameSongs(new List<SongEntry> { new SongEntry { Id = 1001, Title = "Updated title", Members = 18, MainCount = 3 }, new SongEntry { Id = 900001, Title = "New catalog song", Members = 20, MainCount = 3 } });
                Check(SongCatalog.Find(1082).Title == bundledSongs.First(song => song.Id == 1082).Title && SongCatalog.Find(1082).Members == 20, "A partial metadata refresh cannot erase the bundled l'Arc de gloire title or total performer count");
                Check(SongCatalog.Find(1001).Title == "Updated title" && SongCatalog.Find(900001).Title == "New catalog song", "Refreshed entries override bundled metadata and retain newly discovered songs");
            } finally { SongCatalog.SetGameSongs(bundledSongs); }
        }
        private sealed class InstallerFixture : IDisposable
        {
            internal readonly string Root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "installer-fixture-" + Guid.NewGuid().ToString("N"));
            internal string Game, Binaries, Active;
            internal Settings Settings;
            internal InstallerFixture(bool hachimi = true) {
                Game = Path.Combine(Root, "game"); Binaries = Path.Combine(Root, "binaries"); Active = Path.Combine(Game, "umamusume.exe.local");
                Directory.CreateDirectory(Path.Combine(Game, "umamusume_Data")); Directory.CreateDirectory(Binaries);
                foreach (string file in new[] { "umamusume.exe", "UnityPlayer.dll", "GameAssembly.dll" }) File.WriteAllText(Path.Combine(Game, file), "game fixture " + file);
                if (hachimi) { Directory.CreateDirectory(Active); File.WriteAllText(Path.Combine(Active, "UnityPlayer.dll"), "hachimi fixture"); File.WriteAllText(Path.Combine(Active, "apphelp.dll"), "helper fixture"); File.WriteAllText(Path.Combine(Active, "keep.txt"), "user file"); Directory.CreateDirectory(Path.Combine(Game, "hachimi")); File.WriteAllText(Path.Combine(Game, "hachimi", "config.json"), "{}"); }
                foreach (string file in new[] { "UnityPlayer.dll", "umapyon_bridge.dll", "apphelp.dll" }) File.WriteAllText(Path.Combine(Binaries, file), file == "apphelp.dll" ? "helper fixture" : "standalone fixture " + file);
                if (ReleasePolicy.PublicRelease) {
                    File.Copy(LoaderHelper.Require(), Path.Combine(Binaries, "apphelp.dll"), true);
                    if (hachimi) File.Copy(LoaderHelper.Require(), Path.Combine(Active, "apphelp.dll"), true);
                }
                File.WriteAllText(Path.Combine(Binaries, "game-profile.json"), Json.Encode(new { unity = new { sha256 = IntegrationProfiles.Hash(Path.Combine(Game, "UnityPlayer.dll")) }, assembly = new { sha256 = IntegrationProfiles.Hash(Path.Combine(Game, "GameAssembly.dll")) } }));
                File.WriteAllText(Path.Combine(Binaries, "loader-provenance.json"), Json.Encode(new { verifiedHachimiSha256 = hachimi ? IntegrationProfiles.Hash(Path.Combine(Active, "UnityPlayer.dll")) : "", helperSha256 = IntegrationProfiles.Hash(Path.Combine(Binaries, "apphelp.dll")), standaloneSha256 = IntegrationProfiles.Hash(Path.Combine(Binaries, "UnityPlayer.dll")), bridgeSha256 = IntegrationProfiles.Hash(Path.Combine(Binaries, "umapyon_bridge.dll")) }));
                Settings = new Settings { GameDirectory = Game, GameIntegrationMode = "Standalone" };
            }
            public void Dispose() { IntegrationProfiles.Fault = null; string resolved = Path.GetFullPath(Root), parent = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory); if (!resolved.StartsWith(parent, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(resolved).StartsWith("installer-fixture-")) throw new IOException("Invalid fixture cleanup path"); if (Directory.Exists(resolved)) Directory.Delete(resolved, true); }
        }
        private static void StandaloneInstallerTests()
        {
            using (var fixture = new InstallerFixture()) {
                IntegrationProfiles.Prepare(fixture.Settings, fixture.Binaries);
                string loader = Path.Combine(fixture.Active, "UnityPlayer.dll");
                File.WriteAllText(loader, "updated Hachimi fixture");
                string hash = IntegrationProfiles.Hash(loader);
                string config = Path.Combine(fixture.Game, "hachimi", "config.json");
                File.WriteAllText(config, "{\"load_libraries\":[\"other.dll\"]}");
                IntegrationProfiles.InstallHachimi(fixture.Settings, fixture.Binaries, settings => { });
                IntegrationProfiles.InstallHachimi(fixture.Settings, fixture.Binaries, settings => { });
                Check(IntegrationProfiles.Hash(loader) == hash && File.ReadAllText(config).Contains("other.dll") && Directory.GetFiles(Path.Combine(fixture.Game, "hachimi", "plugins"), "*.dll").Length == 1, "An updated active Hachimi loader can register the bridge repeatedly without replacing the loader or other plugins");
                bool refused = false;
                try { IntegrationProfiles.InstallStandalone(fixture.Settings, fixture.Binaries, settings => { }); }
                catch (IOException) { refused = true; }
                Check(refused && IntegrationProfiles.Hash(loader) == hash, "Switching still verifies updated loader files before moving them");
            }
            using (var fixture = new InstallerFixture()) {
                fixture.Settings.GameIntegrationMode = "Hachimi";
                string config = Path.Combine(fixture.Game, "hachimi", "config.json");
                File.WriteAllText(config, "{\"load_libraries\":[\"other.dll\"],\"discord_rpc\":false}");
                IntegrationProfiles.InstallHachimi(fixture.Settings, fixture.Binaries, settings => { });
                Check(File.ReadAllText(config).Contains("umapyon_bridge_managed_") && File.ReadAllText(config).Contains("other.dll"), "Hachimi setup registers the bundled bridge and preserves unrelated plugins");
                bool saved = false;
                IntegrationProfiles.InstallStandalone(fixture.Settings, fixture.Binaries, settings => saved = settings.GameIntegrationMode == "Standalone");
                Check(saved && IntegrationProfiles.AppliedMessage(fixture.Settings).StartsWith("Standalone loader applied successfully.") && File.Exists(Path.Combine(fixture.Active, "umapyon_bridge.dll")), "Install activates the activity bridge and saves Standalone even from a Hachimi selection");
                IntegrationProfiles.InstallStandalone(fixture.Settings, fixture.Binaries, settings => { });
                IntegrationProfiles.InstallHachimi(fixture.Settings, fixture.Binaries, settings => { });
                Check(fixture.Settings.GameIntegrationMode == "Hachimi" && Directory.GetFiles(Path.Combine(fixture.Game, "hachimi", "plugins"), "*.dll").Length == 1, "Switching back to Hachimi reuses its verified bridge without duplicates");
                IntegrationProfiles.UninstallCurrent(fixture.Settings);
                Check(!File.ReadAllText(config).Contains("umapyon_bridge_managed_") && File.ReadAllText(config).Contains("other.dll"), "Uninstall removes the managed Hachimi bridge registration and keeps unrelated plugins");
                Check(IntegrationProfiles.DetectStartingProfile(fixture.Settings, fixture.Binaries) == "Hachimi Edge" && !IntegrationProfiles.CanUninstall(fixture.Settings), "Reapplying the install is safe and uninstall restores Hachimi");
            }
            using (var fixture = new InstallerFixture(false)) {
                Check(IntegrationProfiles.DetectStartingProfile(fixture.Settings, fixture.Binaries) == "Original DMM", "Unmanaged verified game build is recognized as original DMM");
                IntegrationProfiles.Prepare(fixture.Settings, fixture.Binaries);
                Check(IntegrationProfiles.DetectStartingProfile(fixture.Settings, fixture.Binaries) == "Original DMM" && IntegrationProfiles.CanUninstall(fixture.Settings), "Clean DMM origin remains visible and uninstall becomes available after profile preparation");
                IntegrationProfiles.Apply(fixture.Settings); IntegrationProfiles.Uninstall(fixture.Settings);
                Check(IntegrationProfiles.DetectStartingProfile(fixture.Settings, fixture.Binaries) == "Original DMM" && !IntegrationProfiles.CanUninstall(fixture.Settings), "Uninstall returns to the verified original DMM profile and clears managed removal state");
            }
            using (var fixture = new InstallerFixture()) {
                Check(IntegrationProfiles.DetectStartingProfile(fixture.Settings, fixture.Binaries) == "Hachimi Edge", "Verified pre-existing loader is recognized as Hachimi Edge");
                IntegrationProfiles.Prepare(fixture.Settings, fixture.Binaries);
                Check(IntegrationProfiles.DetectStartingProfile(fixture.Settings, fixture.Binaries) == "Hachimi Edge" && IntegrationProfiles.CanUninstall(fixture.Settings), "Hachimi origin remains visible in the prepared integration status");
                IntegrationProfiles.Apply(fixture.Settings);
                Check(IntegrationProfiles.DetectStartingProfile(fixture.Settings, fixture.Binaries) == "Hachimi Edge", "Switching to Standalone preserves the detected starting profile");
                fixture.Settings.GameIntegrationMode = "Hachimi"; IntegrationProfiles.Apply(fixture.Settings);
                IntegrationProfiles.Uninstall(fixture.Settings);
                Check(IntegrationProfiles.DetectStartingProfile(fixture.Settings, fixture.Binaries) == "Hachimi Edge" && !IntegrationProfiles.CanUninstall(fixture.Settings), "Hachimi is restored after Standalone uninstall and the starting profile is reported correctly");
            }
            using (var fixture = new InstallerFixture()) {
                File.WriteAllText(Path.Combine(fixture.Active, "UnityPlayer.dll"), "unrecognized loader");
                Check(IntegrationProfiles.DetectStartingProfile(fixture.Settings, fixture.Binaries) == "Unknown", "Unrecognized loader is never mislabeled as original DMM or Hachimi");
            }
            using (var fixture = new InstallerFixture(false)) {
                IntegrationProfiles.Prepare(fixture.Settings, fixture.Binaries);
                File.AppendAllText(Path.Combine(fixture.Game, "GameAssembly.dll"), " launcher update");
                bool rejected = false;
                try { IntegrationProfiles.Apply(fixture.Settings); } catch (IOException) { rejected = true; }
                Check(rejected && !Directory.Exists(fixture.Active), "Game update between preparation and activation leaves the official loader active");
            }
            foreach (string stage in new[] { "PrepareRecorded", "PrepareMoved" }) using (var fixture = new InstallerFixture()) {
                IntegrationProfiles.Fault = name => { if (name == stage) throw new IOException("Simulated interruption"); };
                try { IntegrationProfiles.Prepare(fixture.Settings, fixture.Binaries); } catch (IOException) { }
                IntegrationProfiles.Fault = null; IntegrationProfiles.Recover(fixture.Settings); IntegrationProfiles.Recover(fixture.Settings);
                Check(File.ReadAllText(Path.Combine(fixture.Active, "UnityPlayer.dll")) == "hachimi fixture", stage + ": preparation recovery preserves active Hachimi");
                IntegrationProfiles.Apply(fixture.Settings); fixture.Settings.GameIntegrationMode = "Hachimi"; IntegrationProfiles.Apply(fixture.Settings);
                Check(File.ReadAllText(Path.Combine(fixture.Active, "keep.txt")) == "user file", stage + ": recovered profiles round-trip with unknown user files intact");
            }
            foreach (bool restore in new[] { false, true }) foreach (string stage in new[] { "SwitchPrepared", "SourceMoved", "SourceRecorded", "TargetMoved", "TargetRecorded" }) using (var fixture = new InstallerFixture()) {
                IntegrationProfiles.Prepare(fixture.Settings, fixture.Binaries);
                IntegrationProfiles.Fault = name => { if (name == stage) throw new IOException("Simulated interruption"); };
                try { if (restore) IntegrationProfiles.RestoreOriginal(fixture.Settings); else IntegrationProfiles.Apply(fixture.Settings); } catch (IOException) { }
                IntegrationProfiles.Fault = null; IntegrationProfiles.Recover(fixture.Settings); IntegrationProfiles.Recover(fixture.Settings);
                Check(File.ReadAllText(Path.Combine(fixture.Active, "UnityPlayer.dll")) == "hachimi fixture", (restore ? "Restore " : "Switch ") + stage + ": repeatable rollback restores Hachimi");
            }
            using (var fixture = new InstallerFixture(false)) {
                IntegrationProfiles.Prepare(fixture.Settings, fixture.Binaries); IntegrationProfiles.Apply(fixture.Settings); IntegrationProfiles.RestoreOriginal(fixture.Settings);
                Check(!Directory.Exists(fixture.Active), "Clean-install restore removes only managed forwarding profile");
                fixture.Settings.GameIntegrationMode = "Hachimi"; bool rejected = false; try { IntegrationProfiles.Apply(fixture.Settings); } catch (IOException) { rejected = true; }
                Check(rejected, "Clean install never invents a Hachimi backup");
            }
            using (var fixture = new InstallerFixture()) {
                IntegrationProfiles.Prepare(fixture.Settings, fixture.Binaries); IntegrationProfiles.Apply(fixture.Settings);
                Check(IntegrationProfiles.AppliedMessage(fixture.Settings).StartsWith("Standalone loader applied successfully."), "Apply confirmation verifies active files and the completed journal");
                File.AppendAllText(Path.Combine(fixture.Active, "UnityPlayer.dll"), " modified");
                bool rejected = false; try { IntegrationProfiles.AppliedMessage(fixture.Settings); } catch (IOException) { rejected = true; }
                Check(rejected, "Apply confirmation never reports success for changed loader files");
            }
            using (var fixture = new InstallerFixture()) {
                IntegrationProfiles.Prepare(fixture.Settings, fixture.Binaries); File.AppendAllText(Path.Combine(fixture.Active, "keep.txt"), " changed");
                bool rejected = false; try { IntegrationProfiles.Apply(fixture.Settings); } catch (IOException) { rejected = true; }
                Check(rejected && File.ReadAllText(Path.Combine(fixture.Active, "UnityPlayer.dll")) == "hachimi fixture", "Modified unrecognized profile files block switching before any move");
            }
            foreach (string stage in new[] { "RemoveRecorded", "RemoveMoved", "RemoveFile" }) using (var fixture = new InstallerFixture()) {
                IntegrationProfiles.Prepare(fixture.Settings, fixture.Binaries); IntegrationProfiles.Apply(fixture.Settings);
                IntegrationProfiles.Fault = name => { if (name == stage) throw new IOException("Simulated interruption"); };
                try { IntegrationProfiles.Uninstall(fixture.Settings); } catch (IOException) { }
                IntegrationProfiles.Fault = null; IntegrationProfiles.Recover(fixture.Settings); IntegrationProfiles.Recover(fixture.Settings);
                Check(File.ReadAllText(Path.Combine(fixture.Active, "UnityPlayer.dll")) == "hachimi fixture" && !Directory.Exists(Path.Combine(fixture.Game, "umapresence", "profiles", "standalone")), stage + ": interrupted uninstall recovers with Hachimi restored");
                IntegrationProfiles.Prepare(fixture.Settings, fixture.Binaries);
                Check(Directory.Exists(Path.Combine(fixture.Game, "umapresence", "profiles", "standalone")), stage + ": clean reinstall after recovered removal");
            }
            foreach (bool recover in new[] { false, true }) using (var fixture = new InstallerFixture()) {
                string config = Path.Combine(fixture.Game, "hachimi", "config.json");
                File.WriteAllText(config, "{\"load_libraries\":[\"other.dll\"]}");
                IntegrationProfiles.InstallHachimi(fixture.Settings, fixture.Binaries, settings => { });
                IntegrationProfiles.Prepare(fixture.Settings, fixture.Binaries);
                IntegrationProfiles.InstallStandalone(fixture.Settings, fixture.Binaries, settings => { });
                IntegrationProfiles.Fault = stage => { if (stage == "RemoveRecorded") throw new IOException("Simulated interruption"); };
                try { IntegrationProfiles.UninstallCurrent(fixture.Settings, fullRemoval: true); }
                catch (IOException) { }
                finally { IntegrationProfiles.Fault = null; }
                Check(IntegrationProfiles.State(fixture.Settings).Step == "Removing", "Updated-Hachimi fixture starts at the persisted removal boundary");
                string loader = Path.Combine(fixture.Active, "UnityPlayer.dll");
                string helper = Path.Combine(fixture.Active, "apphelp.dll");
                File.WriteAllText(loader, "updated Hachimi loader");
                File.WriteAllText(helper, "updated Hachimi helper");
                string dictionary = DataMaintenance.TranslationPath(fixture.Settings);
                Directory.CreateDirectory(Path.GetDirectoryName(dictionary));
                File.WriteAllText(dictionary, "{\"16\":{\"1111\":\"Language update\"}}");
                if (recover) IntegrationProfiles.Recover(fixture.Settings);
                else IntegrationProfiles.UninstallCurrent(fixture.Settings, fullRemoval: true);
                Check(File.ReadAllText(loader) == "updated Hachimi loader"
                    && File.ReadAllText(helper) == "updated Hachimi helper"
                    && File.ReadAllText(dictionary).Contains("Language update")
                    && File.ReadAllText(config).Contains("other.dll") && !File.ReadAllText(config).Contains("umapyon_bridge_managed_")
                    && !Directory.GetFiles(Path.Combine(fixture.Game, "hachimi", "plugins"), "*.dll").Any()
                    && !Directory.Exists(Path.Combine(fixture.Game, "umapresence", "profiles", "standalone")),
                    (recover ? "Recovery" : "Full uninstall retry") + " finishes removal while preserving the updated active Hachimi loader");
            }
            using (var fixture = new InstallerFixture()) {
                IntegrationProfiles.Prepare(fixture.Settings, fixture.Binaries);
                string loader = Path.Combine(fixture.Active, "UnityPlayer.dll");
                string helper = Path.Combine(fixture.Active, "apphelp.dll");
                File.WriteAllText(loader, "updated Hachimi loader");
                File.WriteAllText(helper, "updated Hachimi helper");
                string path = Path.Combine(fixture.Binaries, "loader-provenance.json");
                var provenance = Json.Decode<Dictionary<string, string>>(File.ReadAllText(path));
                provenance["verifiedHachimiSha256"] = IntegrationProfiles.Hash(loader);
                provenance["verifiedHachimiHelperSha256"] = IntegrationProfiles.Hash(helper);
                File.WriteAllText(path, Json.Encode(provenance));
                IntegrationProfiles.InstallStandalone(fixture.Settings, fixture.Binaries, settings => { });
                IntegrationProfiles.InstallHachimi(fixture.Settings, fixture.Binaries, settings => { });
                Check(File.ReadAllText(loader) == "updated Hachimi loader" && File.ReadAllText(helper) == "updated Hachimi helper",
                    "A recognized updated Hachimi pair can be saved before switching and restored intact");
            }
            using (var fixture = new InstallerFixture()) {
                IntegrationProfiles.InstallStandalone(fixture.Settings, fixture.Binaries, settings => { });
                string saved = Path.Combine(fixture.Game, "umapresence", "profiles", "hachimi", "UnityPlayer.dll");
                File.WriteAllText(saved, "unverified saved Hachimi loader");
                File.AppendAllText(Path.Combine(fixture.Game, "GameAssembly.dll"), " game update");
                bool refused = false;
                try { IntegrationProfiles.UninstallCurrent(fixture.Settings, fullRemoval: true); }
                catch (IOException) { refused = true; }
                Check(refused && Directory.Exists(fixture.Active), "Uninstall leaves the active Standalone loader in place when the Hachimi restore cannot be verified");
                IntegrationProfiles.ReturnToStockDmm(fixture.Settings);
                IntegrationProfiles.UninstallCurrent(fixture.Settings, fullRemoval: true);
                Check(!Directory.Exists(fixture.Active) && File.ReadAllText(saved) == "unverified saved Hachimi loader",
                    "Explicit stock fallback enables cleanup after a game update and preserves the saved Hachimi files");
            }
            using (var fixture = new InstallerFixture()) {
                string dictionary = DataMaintenance.TranslationPath(fixture.Settings);
                Directory.CreateDirectory(Path.GetDirectoryName(dictionary));
                File.WriteAllText(dictionary, "{\"5\":{\"12345\":\"Test outfit\"}}");
                DataMaintenance.ReadTranslations(fixture.Settings, refresh: true);
                Check(CareerLabels.Outfit(12345, fixture.Settings).Trim() == "Test outfit", "A valid language pack is used independently of loader state");
                File.WriteAllText(dictionary, "{\"5\":null}");
                DataMaintenance.ReadTranslations(fixture.Settings, refresh: true);
                Check(CareerLabels.Outfit(12345, fixture.Settings).Trim() == "Test outfit", "An invalid language pack keeps the last valid value");
                File.Delete(dictionary);
                DataMaintenance.ReadTranslations(fixture.Settings, refresh: true);
                Check(CareerLabels.Outfit(12345, fixture.Settings).Trim() == "Test outfit", "A missing language pack keeps the last valid value");
            }
        }
        private static void StandaloneConfigurationTests()
        {
            var unavailable = new Settings { ConcertAllowDuplicates=true, ConcertAudioMode="Solo" };
            Check(StandaloneConfiguration.Missing(unavailable,RuntimeCapability.Telemetry).Contains("duplicate casting") && StandaloneConfiguration.Missing(unavailable,RuntimeCapability.Telemetry).Contains("singer arrangements"), "Unsupported stored choices are reported explicitly instead of claiming they applied");
            unavailable.ConcertAudioMode="Original";unavailable.ConcertAllowDuplicates=false;
            Check(StandaloneConfiguration.Missing(unavailable,RuntimeCapability.Telemetry)=="", "Original game audio requires no experimental hook");
            Check(ConcertAudioChoice.All.Select(item=>item.Key).Distinct().Count()==5 && ConcertAudioChoice.Name("Balanced")=="Balance duplicate voices", "Plain audio names preserve all five existing native setting keys");
            using(var fixture=new InstallerFixture()) {
                string acb=new string('A',32),awb=new string('B',32),folder=Path.Combine(fixture.Game,"umamusume_Data","Persistent","dat");
                Directory.CreateDirectory(Path.Combine(folder,"AA"));Directory.CreateDirectory(Path.Combine(folder,"BB"));
                File.WriteAllText(Path.Combine(folder,"AA",acb),"cue fixture");File.WriteAllBytes(Path.Combine(folder,"BB",awb),new byte[32]);
                string catalogue=Path.Combine(fixture.Root,"recordings.json");
                File.WriteAllText(catalogue,Json.Encode(new {rows=new[]{new {song=1176,voice=1003,acb,awb,awbBytes=32},new {song=1193,voice=1004,acb,awb,awbBytes=32}}}));
                Check(ConcertRecordingCatalog.Available(fixture.Settings,1176,catalogue).SetEquals(new[]{1003}), "Singer choices are scoped to the active song's cached recordings");
                File.Delete(Path.Combine(folder,"BB",awb));
                Check(ConcertRecordingCatalog.Available(fixture.Settings,1176,catalogue).Count==0, "Missing audio data cannot appear as a selectable cached recording");
                Check(ConcertRecordingCatalog.Available(fixture.Settings,0,catalogue)==null, "A closed concert is reported as unchecked, not as zero supported singers");
            }
            using (var fixture = new InstallerFixture()) {
                File.WriteAllText(Path.Combine(fixture.Game, "hachimi", "config.json"), "{\"live_theater_allow_same_chara\":true}");
                Check(StandaloneConfiguration.ImportHachimiPreferences(fixture.Settings) && fixture.Settings.ConcertAllowDuplicates, "Standalone preserves the existing Hachimi duplicate-casting preference on first setup");
                fixture.Settings.ConcertAllowDuplicates = false;
                Check(!StandaloneConfiguration.ImportHachimiPreferences(fixture.Settings) && !fixture.Settings.ConcertAllowDuplicates, "Later standalone duplicate preference changes are never overwritten by reimport");
            }
            var settings = new Settings { ConcertAllowDuplicates = true, ConcertAudioMode = "Balanced" };
            Check(StandaloneConfiguration.Flags(settings, RuntimeCapability.Formation) == 1, "Formation config does not require a working audio adapter");
            Check(StandaloneConfiguration.Flags(settings, RuntimeCapability.Mix) == 2, "Mix config does not enable unavailable formation hooks");
            settings.ConcertAudioMode = "Solo";
            Check(StandaloneConfiguration.Flags(settings, RuntimeCapability.Routing) == 8, "Vocal routing is independent of the gain adapter");
            Check(StandaloneConfiguration.Flags(settings, RuntimeCapability.Mix) == 0, "Unsupported Solo routing cannot be sent as a gain setting");
            settings.ConcertAudioMode = "Lead";
            Check(StandaloneConfiguration.Flags(settings, RuntimeCapability.Mix) == 6, "Lead emphasis keeps duplicate balancing enabled");
            settings.ConcertAudioMode = "Original";
            Check(StandaloneConfiguration.Flags(settings, RuntimeCapability.Mix) == 0, "Original arrangement disables companion attenuation");
            var runtime = new StandaloneRuntimeStatus { Pid = 42, State = 4, Backend = 1, Nonce = 0x1234 };
            var command = StandaloneRuntime.Command(runtime, 6, 0, 0, 1, 1001, 1002);
            Check(command.Length == 56 && BitConverter.ToInt32(command, 8) == 42 && BitConverter.ToUInt64(command, 16) == 0x1234 && BitConverter.ToInt32(command, 24) == 6 && BitConverter.ToInt32(command, 44) == 1001, "Native configuration command packing preserves session and voice identity");
        }
        private static void StandaloneOwnershipTests()
        {
            var bytes = new byte[240];
            Action<int, int> put = (offset, value) => Buffer.BlockCopy(BitConverter.GetBytes(value), 0, bytes, offset, 4);
            put(0, unchecked((int)0x55505231)); put(4, 1); put(8, 42); put(12, 4); put(16, 123); put(24, 127); put(44, 1);
            var standalone = StandaloneRuntime.Parse(bytes, 42);
            Check(standalone.Ready && !standalone.OwnershipConflict, "Only an actually ready standalone backend can own concert controls");
            put(12, 7); put(44, 2);
            var conflict = StandaloneRuntime.Parse(bytes, 42);
            Check(conflict.OwnershipConflict && !conflict.Ready, "Competing integration disables controls even when capability bits remain set");
            bool rejected = false;
            try { StandaloneRuntime.Command(conflict, 3, 1176, 20); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "Conflict status cannot issue playback or seek commands");
            put(12, 4);
            Check(!StandaloneRuntime.Parse(bytes, 42).Ready, "A ready state from another backend never enables standalone controls");
            Check(StandaloneRuntime.Parse(bytes, 43) == null, "Another game process cannot supply playback ownership");
            put(44, 1); put(32, 1);
            Check(StandaloneRuntime.Parse(bytes, 42) == null, "An incomplete ownership status update fails closed");
            var extended = new byte[320];Buffer.BlockCopy(bytes,0,extended,0,bytes.Length);Buffer.BlockCopy(BitConverter.GetBytes(0),0,extended,32,4);
            Buffer.BlockCopy(BitConverter.GetBytes(0x55504131u),0,extended,256,4);Buffer.BlockCopy(BitConverter.GetBytes(2u),0,extended,260,4);
            Buffer.BlockCopy(BitConverter.GetBytes(.154f),0,extended,296,4);Buffer.BlockCopy(BitConverter.GetBytes(153.846f),0,extended,300,4);
            Check(Math.Abs(StandaloneRuntime.Parse(extended,42).SeekStart-.154)<.00001 && Math.Abs(StandaloneRuntime.Parse(extended,42).SeekEnd-153.846)<.0001,"Validated native cue windows survive status transport");
            Buffer.BlockCopy(BitConverter.GetBytes(1u),0,extended,260,4);
            Check(StandaloneRuntime.Parse(extended,42).SeekEnd==0,"Older audio trace versions never expose reserved bytes as seek limits");
            Buffer.BlockCopy(BitConverter.GetBytes(2u),0,extended,260,4);Buffer.BlockCopy(BitConverter.GetBytes(float.NaN),0,extended,300,4);
            Check(StandaloneRuntime.Parse(extended,42).SeekEnd==0,"Nonfinite native seek limits are rejected");
        }
        private static void StandaloneRotationTests()
        {
            var origin = new DateTime(2026, 10, 29, 0, 0, 0, DateTimeKind.Utc);
            Func<bool, RaceSnapshot> snapshot = reverse => new RaceSnapshot { Results = true, Program = 800001, ProgramType = 2, Distance = 2400, Team = (reverse ? new[] { 3, 2, 1 } : new[] { 1, 2, 3 }).Select(gate => new RaceEntry { Gate = gate, CharacterId = 1000 + gate, Position = gate }).ToArray() };
            var tracker = new RaceTracker(); tracker.Observe(snapshot(false), origin); tracker.Published(origin);
            tracker.Observe(snapshot(false), origin.AddSeconds(1)); tracker.Published(origin.AddSeconds(1));
            tracker.Observe(snapshot(false), origin.AddSeconds(2)); tracker.Published(origin.AddSeconds(2));
            var next = tracker.Observe(snapshot(false), origin.AddSeconds(3));
            Check(next.Race.Team[next.Race.Page].Gate == 2, "Metadata acknowledgements do not extend the first race page's three-second hold");
            tracker.Published(origin.AddSeconds(3));
            var reordered = tracker.Observe(snapshot(true), origin.AddSeconds(4));
            Check(reordered.Race.Team[reordered.Race.Page].Gate == 2, "Harmless telemetry reordering cannot restart the race page cycle");
            var third = tracker.Observe(snapshot(false), origin.AddSeconds(6));
            Check(third.Race.Team[third.Race.Page].Gate == 3, "Third registered racer is reached after two acknowledged holds without starvation");
            var stalled = tracker.Observe(snapshot(false), origin.AddSeconds(60));
            Check(stalled.Race.Team[stalled.Race.Page].Gate == 3, "An unacknowledged race page cannot be skipped by elapsed time or reconnect delay");
            foreach (int count in new[] { 1, 2, 3 }) {
                var fair = new RaceTracker(); var clock = origin;
                Func<RaceSnapshot> duplicateTeam = () => new RaceSnapshot { Results = true, Program = 800002, Distance = 2400, Team = Enumerable.Range(1, count).Select(gate => new RaceEntry { Gate = gate, CharacterId = 1001, Position = gate }).ToArray() };
                for (int turn = 0; turn < count * 3; turn++) {
                    var page = fair.Observe(duplicateTeam(), clock).Race;
                    if (page.Team[page.Page].Gate != turn % count + 1) throw new Exception("Duplicate-character gate was starved");
                    fair.Published(page, clock);
                    for (int refresh = 1; refresh < 6; refresh++) { var update = fair.Observe(duplicateTeam(), clock.AddSeconds(refresh * .4)); fair.Published(update.Race, clock.AddSeconds(refresh * .4)); }
                    clock = clock.AddSeconds(3);
                }
                Check(true, count + " duplicate-identity entrants complete three fair cycles during rapid updates");
            }
        }
        private static void JapaneseTooltipTests()
        {
            var festival = new LegacyDetails { Speed = 2232, Stamina = 1858, Power = 1604, Guts = 1557, Wisdom = 1612, Wins = 11, Races = 12 };
            string festivalHover = JapaneseTooltips.Other(festival.Tooltip("朋、史解き ―一年式駿大祭―"));
            Check(festivalHover.Replace("\u2060", "").StartsWith("駿大祭 ") && !festivalHover.Contains("…") && JapaneseTooltips.ScenarioTitle("Tracen Ramen") == "Tracen Ramen", "Japanese festival uses a readable short title without an ellipsis and leaves English scenario titles unchanged");
            string jp = JapaneseTooltips.Character("Daiwa Scarlet", "[陣風の花紺青]", "Chisa Kimura");
            string[] rows = jp.Split(' ');
            Check(rows.Length == 3 && rows[0] == "Daiwa\u00a0Scarlet" && rows[1].Replace("\u2060", "") == "[陣風の花紺青]" && rows[1].Count(c => c == '\u2060') >= 7 && rows[2] == "CV:\u00a0Chisa\u00a0Kimura", "Japanese fallback costume is a separate protected bracketed row, with zero-width no-break boundaries between Japanese glyphs");
            string english = "Victoire\u00a0Pisa [Lueur\u00a0Angélique] CV:\u00a0Shimano\u00a0Hana";
            var assets = new Dictionary<string, object> { { "large_text", english }, { "small_text", "GP1:\u00a023\u00a0•\u00a0GP2:\u00a025" } };
            var payload = new Dictionary<string, object> { { "assets", assets } };
            string before = Json.Encode(payload); JapaneseTooltips.Apply(payload);
            Check(Json.Encode(payload) == before && JapaneseTooltips.Other(english) == english, "Japanese-only dispatcher leaves every byte of English tooltip output unchanged");
            Check(JapaneseTooltips.Other(jp) == jp && JapaneseTooltips.Other("ダイワスカーレット\u00a0—\u00a01st Current:\u202f20.0\u202f\u2060|\u2060\u202fPeak:\u202f21.0").StartsWith("ダ\u2060"), "Existing Japanese protection is idempotent and an English speed-row joiner does not bypass Japanese race-name protection");
            string partial = "ダ\u2060イワスカーレット\u00a0—\u00a01st";
            string completed = JapaneseTooltips.Other(partial);
            Check(completed.Replace("\u2060", "") == partial.Replace("\u2060", "") && completed.Contains("イ\u2060ワ") && JapaneseTooltips.Other(completed) == completed, "Partially protected Japanese names receive every missing boundary without changing their text or duplicating protection");
            string overprotected = JapaneseTooltips.Other(JapaneseTooltips.Protect(new string('花', 40)) + " GP1:\u00a023\u00a0•\u00a0GP2:\u00a025");
            Check(Encoding.UTF8.GetByteCount(overprotected) <= 128 && overprotected.Contains("GP1:\u00a023\u00a0•\u00a0GP2:\u00a025") && JapaneseTooltips.Other(overprotected) == overprotected, "Preprotected Japanese labels still respect the byte budget while preserving the complete grandparent row");
            foreach (string text in new[] { "サウンズオブアース\u00a0—\u00a01st Finish:\u00a026.3\u00a0|\u00a0Peak:\u00a027.5\u00a0m/s Won\u00a0by\u00a03\u00a0L", "朋、史解き―午式駿大祭― 2169/1807/1828/1530/1460 14/14\u00a0wins White\u00a0factors:\u00a020 GP1:\u00a023\u00a0•\u00a0GP2:\u00a025", "桜花賞 阪神\u00a0-\u00a0芝\u00a0-\u00a01600m" }) {
                string result = JapaneseTooltips.Other(text);
                Check(Encoding.UTF8.GetByteCount(result) <= 128 && result.Contains('\u2060') && (!text.Contains("1st") || result.Contains("1st")) && (!text.Contains("2169/") || result.Contains("2169/1807/1828/1530/1460")), "Japanese race/scenario/Legacy tooltip protection preserves numerical data within the payload limit");
            }
            foreach (int length in new[] { 7, 20, 60 }) {
                string result = JapaneseTooltips.Character("ダイワスカーレット", "[" + new string('花', length) + "]", "木村千咲");
                Check(Encoding.UTF8.GetByteCount(result) <= 128 && result.Contains("[") && result.Contains("]"), "Long Japanese fallback titles retain bracket boundaries and safe UTF-8 truncation");
            }
            Check(SongCatalog.Find(1195).Title == "繚乱縁舞" && SongCatalog.Find(1195).MainCount == 6, "New song 1195 resolves its original game title and six main performers");
        }

        private static void AlmondLegacyTests()
        {
            string fixtures = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "bridge", "fixtures");
            var legacy = CareerSnapshot.ApplyResult(File.ReadAllBytes(Path.Combine(fixtures, "almond-result-v24.bin")), null);
            var details = LegacyDetails.ParsePacket(File.ReadAllBytes(Path.Combine(fixtures, "almond-legacy-v24.bin")));
            details.ApplyGrandparents(File.ReadAllBytes(Path.Combine(fixtures, "almond-family-v24.bin")));
            legacy.LegacyDetails = details;
            Check(legacy.Card == 112901 && legacy.RecordId == 6384 && details.Matches(legacy) && details.Factors.Contains(2048102) && LegacyDetails.WhiteCount(details.Factors) == 20, "Captured Almond Eye Legacy counts the new white factor from the installed master instead of hiding all factors");
            var assets = (Dictionary<string, object>)((Dictionary<string, object>)legacy.ToActivity(Settings.Load(), 1))["assets"];
            string small = assets["small_text"].ToString().Replace('\u00a0', ' '), large = assets["large_text"].ToString().Replace('\u00a0', ' ');
            Check(small.Contains("駿大祭") && !small.Contains("…") && small.Contains("2169/1807/1828/1530/1460") && small.Contains("14/14 wins") && small.Replace(": ", ":").Contains("White factors:20 GP1:23 • GP2:25") && !small.Contains("|") && Encoding.UTF8.GetByteCount(assets["small_text"].ToString()) <= 128, "Almond Eye uses the short Japanese scenario title and preserves stats, wins and all factor counts without a pipe or dropped rows");
            var protectedRows = assets["small_text"].ToString().Split(' ');
            Check(protectedRows.Length == 5 && protectedRows[2].Contains("wins") && protectedRows[2].StartsWith("\u2007") && protectedRows[2].EndsWith("\u2007") && protectedRows[3].Contains("White") && protectedRows[3].Contains(":\u00a020") && protectedRows[4].Replace('\u00a0', ' ') == "GP1: 23 • GP2: 25", "Normal Legacy keeps both spaced GP labels in one protected row, separate from its own white-factor count");
            var ramen = new LegacyDetails { Score = 1, Speed = 2195, Stamina = 1662, Power = 1484, Guts = 1206, Wisdom = 1646, Wins = 18, Races = 18, Factors = details.Factors, GrandparentFactors = details.GrandparentFactors };
            string[] ramenRows = ramen.Tooltip("Tracen Ramen").Split(' ');
            Check(ramenRows.Length == 5 && !ramenRows[3].Contains("GP1") && ramenRows[4].Contains("GP1") && ramenRows[4].Contains("GP2"), "Tracen Ramen screenshot regression: GP1 cannot attach to White factors while GP2 wraps alone");
            string bareFixture = File.ReadAllText(Path.Combine(fixtures, "bare-tachyon-core-live.txt"));
            var bare = BareLegacyInspectionReader.Parse(bareFixture);
            Check(bare != null && bare.Card == 103202 && bare.RecordId == 5299 && bare.BareLegacy && bare.SkillCount == 0 && bare.LegacyDetails.Wins == 20 && bare.LegacyDetails.Races == 22, "Live bare Tachyon is selected by visible identity and explicit succession-only flag, with zero stats and no fabricated evaluation");
            bare.LegacyDetails.Factors = new[] { 202, 2202, 1000303 };
            string bareHover = bare.LegacyDetails.BareTooltip();
            Check(!bareHover.Contains("|") && !bareHover.Contains('\u2007') && bareHover.Split(' ').Length == 2 && bareHover.StartsWith("Bare\u00a0Legacy\u00a020/22\u00a0wins "), "Bare Legacy restores its first compact header/wins group with no artificial padding, independent of normal Legacy");
            Check(BareLegacyInspectionReader.Parse(bareFixture.Replace("/@get_IsSuccessionOnly\nVALUE 1", "/@get_IsSuccessionOnly\nVALUE 0")) == null && BareLegacyInspectionReader.Parse(bareFixture.Replace("/@get_IsDispClosed\nVALUE 0", "/@get_IsDispClosed\nVALUE 1")) == null && BareLegacyInspectionReader.Parse(bareFixture.Replace("_trainedCharaId\nVALUE 5299", "_trainedCharaId\nVALUE 5300")) == null, "Bare compatibility rejects normal records, closed dialogs and a changed selected ID");
            Check(large.Contains("US9") && large.Contains("74,968") && large.Contains("68 acquired skills") && !large.Contains("Rank 99") && large.Contains("35% toward LG"), "Almond Eye's actual US9 score restores the correct progress toward LG instead of the missing Rank 99 label");
            Check(CareerRanks.Label(75999) == "US9" && CareerRanks.Label(76000) == "LG" && CareerRanks.Label(76600) == "LG1" && CareerRanks.Label(90900) == "LG24" && CareerRanks.Label(91399) == "LG24" && CareerRanks.ProgressText(90899) == "99% toward LG24", "New LG ranks use installed game thresholds, include LG10 through LG24 and advance only at exact boundaries");
            Check(CareerRanks.Label(91400) == null && CareerRanks.ProgressText(90900) == null && CareerRanks.ProgressText(29095) == "49% toward UE1", "LG24 has no invented LG25 target and existing lower-rank progress remains unchanged");
            var partial = new LegacyDetails { Speed = 1, Stamina = 2, Power = 3, Guts = 4, Wisdom = 5, Wins = 1, Races = 1, Score = 1, Factors = new[] { int.MaxValue }, GrandparentFactors = new[] { details.GrandparentFactors[0], details.GrandparentFactors[1] } };
            Check(!partial.Tooltip("Scenario").Replace('\u00a0', ' ').Contains("| •"), "An unavailable own-factor count cannot leave an orphan bullet before the grandparent counts");
        }

        private static void CareerExtraTests(CareerSnapshot career)
        {
            var bytes = new byte[232];
            int[] header = { 0x43455831, 1, career.Card, career.Turn, 31, 56, 104, 5, 1234, 5 };
            for (int i = 0; i < header.Length; i++) Buffer.BlockCopy(BitConverter.GetBytes(header[i]), 0, bytes, i * 4, 4);
            byte[] goal = Encoding.UTF8.GetBytes("Place in the top 5"); Buffer.BlockCopy(goal, 0, bytes, 40, goal.Length);
            var extra = CareerExtras.Parse(bytes, career);
            string hover = extra.Tooltip("Scenario\\nTitle");
            Check(hover.Replace('\u00a0', ' ').Contains("Energy: 56/104 | Great") && hover.Replace('\u00a0', ' ').Contains("Next: Place in the top 5") && hover.Replace('\u00a0', ' ').Contains("5 turns left") && !hover.Contains("skill") && !hover.Contains("\\n") && !hover.Contains('\n'), "Ongoing career hover shows variable maximum energy, motivation, next objective and remaining turns with protected row groups");
            bytes[12]++; Check(CareerExtras.Parse(bytes, career) == null, "An objective from another career turn cannot attach to the current stats"); bytes[12]--;
            bytes[20] = 105; Check(CareerExtras.Parse(bytes, career) == null, "Impossible energy data cannot become a career tooltip"); bytes[20] = 56;
            bytes[16] = 1; extra = CareerExtras.Parse(bytes, career);
            Check(!extra.Tooltip("Scenario").Contains("Next:") && !extra.Tooltip("Scenario").Contains("turn") && extra.Tooltip("Scenario").Contains("56/104"), "Missing objective metadata is omitted without inventing a goal or countdown");
            string longGoal = new CareerExtras { Flags = 27, Energy = 56, MaxEnergy = 104, Motivation = 5, Remaining = 5, Goal = new string('界', 190) }.Tooltip(new string('界', 80));
            Check(Encoding.UTF8.GetByteCount(longGoal) <= 128 && longGoal.Contains("56/104") && longGoal.Replace('\u00a0', ' ').Contains("5 turns left"), "Long Japanese career labels retain energy and countdown within Discord's UTF-8 asset budget");
            var result = new byte[32]; int[] bare = { 0x43525331, 1, 102503, 0, 0, 25, 14, 0 };
            for (int i = 0; i < bare.Length; i++) Buffer.BlockCopy(BitConverter.GetBytes(bare[i]), 0, result, i * 4, 4);
            var snapshot = CareerSnapshot.ApplyResult(result, career);
            var details = new LegacyDetails { Card = 102503, Score = 0, Id = 17, Wins = 12, Races = 14, Factors = new[] { 202, 1102, 10250302 } };
            snapshot.LegacyDetails = details;
            var payload = (Dictionary<string, object>)snapshot.ToActivity(new Settings(), 100);
            var assets = (Dictionary<string, object>)payload["assets"];
            Check(snapshot.BareLegacy && snapshot.Legacy && payload["details"].ToString() == "Manhattan Cafe — Bare Legacy" && payload["state"].ToString() == "12/14 wins • 0 white factors" && !assets["large_text"].ToString().Contains("◆") && assets["large_text"].ToString().Contains("Manhattan\u00a0Cafe") && !assets["large_text"].ToString().Contains('\n') && !assets["small_text"].ToString().Contains("0/0/0/0/0") && assets["small_text"].ToString().Replace('\u00a0', ' ').Contains("White factors: 0"), "Bare Legacy uses protected portrait name/outfit groups and retains wins/white factors without rank, stats or skills");
            result[24] = 4; Check(CareerSnapshot.ApplyResult(result, career) == career, "A zero evaluation without the explicit bare-record validation flag cannot masquerade as bare Legacy");
            result[24] = 14; result[4] = 0; Check(CareerSnapshot.ApplyResult(result, career) == career, "Closing bare Legacy restores the active career instead of retaining the selected record");
        }
        private static void ConcertTests()
        {
            ulong tick = ConcertReader.GetTickCount64();
            byte[] packet = new byte[256];
            Action<int, byte[]> set = (offset, bytes) => Buffer.BlockCopy(bytes, 0, packet, offset, bytes.Length);
            set(0, BitConverter.GetBytes(0x554d4150)); set(4, BitConverter.GetBytes(1)); set(8, BitConverter.GetBytes(2)); set(12, BitConverter.GetBytes(1234)); set(16, BitConverter.GetBytes(tick));
            set(24, BitConverter.GetBytes(1)); set(28, BitConverter.GetBytes(0)); set(32, BitConverter.GetBytes(1176)); set(36, BitConverter.GetBytes(3)); set(40, BitConverter.GetBytes(39.0)); set(48, BitConverter.GetBytes(154.0));
            set(56, BitConverter.GetBytes(1001)); set(60, BitConverter.GetBytes(1002)); set(64, BitConverter.GetBytes(1003));
            var sample = ConcertReader.Parse(packet, 1234, tick);
            Check(sample.DressIds.Length == 0, "Legacy bridge remains supported without outfit IDs");
            Check(!sample.NativeLegacyDetails && !sample.NativeCareerResults, "Older bridges retain compatibility instead of falsely retiring their development reader");
            set(208, BitConverter.GetBytes(0x314e4143)); set(212, BitConverter.GetBytes(3));
            sample = ConcertReader.Parse(packet, 1234, tick);
            Check(sample.NativeLegacyDetails && sample.NativeCareerResults, "Native career capabilities identify when the diagnostic reader can retire");
            set(204, BitConverter.GetBytes(0x44525331));
            set(132, BitConverter.GetBytes(100101)); set(136, BitConverter.GetBytes(100201)); set(140, BitConverter.GetBytes(100301));
            sample = ConcertReader.Parse(packet, 1234, tick);
            Check(sample.DressIds.SequenceEqual(new[] { 100101, 100201, 100301 }), "Optional outfit extension preserves formation ordering");
            Check(sample != null && sample.Active && sample.Position == 39 && sample.Duration == 154, "Native packet offsets decode exact concert timing");
            Check(sample.Title == "VOLTAGE", "Current game song ID resolves VOLTAGE");
            Check(sample.Singers == "Special Week, Silence Suzuka, Tokai Teio", "Uses three selected main performers");
            var duplicates = new ConcertSnapshot { CastIds = new[] { 1022, 1022, 1022 }, DressIds = new[] { 102201, 102201, 102201 } };
            Check(duplicates.Singers == "Fine Motion Trio" && duplicates.MainPortraitSlots.Length == 1, "Duplicate formations collapse names and identical portraits");
            var expectedGroups = new[] { "Duo", "Trio", "Quartet", "Quintet", "Sextet", "Septet", "Octet", "Nonet", "Decet" };
            for (int copies = 2; copies <= 10; copies++)
            {
                var formation = new ConcertSnapshot { CastIds = Enumerable.Repeat(1022, copies).Concat(new[] { 1025 }).ToArray(), MainPerformerCount = copies + 1 };
                Check(formation.Singers == "Fine Motion " + expectedGroups[copies - 2] + ", Manhattan Cafe", "Duplicate label " + expectedGroups[copies - 2] + " preserves other singer names");
            }
            duplicates.DressIds[2] = 102202;
            Check(duplicates.MainPortraitSlots.Length == 2, "Same character in different outfits retains distinct portrait slots");
            var tracker = new ConcertTracker();
            var playing = tracker.Observe(sample, DateTimeOffset.UtcNow);
            var settings = new Settings();
            Check(SongCatalog.OutfitPortrait(1022, 102201, settings).Contains("1022_102201"), "Original racing dress uses its matching frameless racing portrait");
            Check(SongCatalog.OutfitPortrait(1143, 114301, settings).Contains("1143_114301"), "Victoire Pisa uses the same game portrait format as other racing outfits");
            Check(SongCatalog.OutfitPortrait(1001, 100102, settings).Contains("1001_100102"), "Alternate Special Week racing outfit never selects student art");
            Check(SongCatalog.OutfitPortrait(1022, 7, settings).Contains("microcms-assets"), "Non-racing costume uses student fallback");
            settings.ConcertPortraitZoomFactor = 1.1;
            Check(SongCatalog.OutfitPortrait(1143, 114301, settings).Contains("cw=291"), "Ten-percent portrait zoom preserves the original crop center");
            string studentPortrait = SongCatalog.FilledPortrait(SongCatalog.OutfitPortrait(1022, 7, settings));
            Check(studentPortrait.Contains("cw=269&ch=269") && studentPortrait.Contains("cy=0") && !studentPortrait.Contains("%25") && studentPortrait.Length < 240, "Student portrait uses a wider ear-preserving square pixel crop and a short URL without encoded percentages");
            Check(SongCatalog.Resource<Dictionary<string, string>>("student-portraits.json").Keys.All(id => SongCatalog.FilledPortrait(SongCatalog.OutfitPortrait(int.Parse(id), 0, settings)).Length < 240), "Every bundled student portrait uses the bounded mobile-compatible URL form");
            settings.ConcertPortraitZoomFactor = 1.0;
            var lookupEntry = new OutfitEntry { CharacterId = 1001, DressId = 100102 };
            string lookupJson = Json.Encode(new { query = new { pages = new Dictionary<string, object> { { "1", new { title = "File:Game Asset chara stand 1001 100102.png", imageinfo = new[] { new { url = "https://umamusu.wiki/w/images/2/29/Game_Asset_chara_stand_1001_100102.png" } } } } } } });
            Check(OutfitCatalog.ApplyLookup(lookupJson, new List<OutfitEntry> { lookupEntry }) == 1 && lookupEntry.Url.Contains("1001_100102"), "Automatic artwork response resolves the exact requested outfit");
            Check(OutfitCatalog.ApplyLookup(lookupJson.Replace("https://umamusu.wiki/", "https://unrelated.invalid/"), new List<OutfitEntry> { lookupEntry }) == 0, "Automatic artwork lookup rejects unrelated image origins");
            OutfitCatalog.Refresh(Settings.Load());
            Check(OutfitCatalog.Status.StartsWith("Game catalog refreshed"), "Windows built-in SQLite reads installed game master without extra runtime dependencies");
            Check(OutfitCatalog.MainCount(1156) == 4 && OutfitCatalog.MainCount(1035) == 10 && OutfitCatalog.MainCount(1037) == 2, "Game metadata determines four, ten and two main performers");
            var fourMain = new ConcertSnapshot { CastIds = new[] { 1025, 1133, 1129, 1114, 1007 }, MainPerformerCount = OutfitCatalog.MainCount(1156) };
            Check(fourMain.Singers.Contains("Buena Vista") && !fourMain.Singers.Contains("Gold Ship"), "Four-main song includes its fourth performer and excludes back dancers");
            settings.ConcertCycleSingerNames = true;
            sample.SingerCycleElapsed = 0;
            Check(sample.SingerLine(settings) == "Special Week", "Slow singer cycle starts with the full first name");
            sample.SingerCycleElapsed = 4.9;
            Check(sample.SingerLine(settings) == "Special Week", "Singer name holds for at least five seconds");
            sample.SingerCycleElapsed = 5;
            Check(sample.SingerLine(settings) == "Silence Suzuka", "Next complete name follows the hold");
            sample.SingerCycleElapsed = 15;
            Check(sample.SingerLine(settings) == "Special Week", "Singer cycle repeats after the final name");
            settings.ConcertCycleSingerNames = false;
            var payload = Json.Decode<Dictionary<string, object>>(Json.Encode(playing.ToActivity(settings, 1)));
            var timestamps = (Dictionary<string, object>)payload["timestamps"];
            Check(Convert.ToInt32(payload["type"]) == 2 && payload["details"].ToString() == "VOLTAGE" && payload["name"].ToString() == "VOLTAGE" && payload["state"].ToString() == "▶ Umamusume • Live Theater" && ((Dictionary<string, object>)payload["assets"])["large_text"].ToString() == playing.Concert.SingerLine(settings), "Clean Listening card retains the Live Theater label and singer album line without composer text");
            Check(((Dictionary<string, object>)payload["assets"])["small_text"].ToString().Replace('\u00a0', ' ').Trim().StartsWith("Special Week") && ((Dictionary<string, object>)payload["assets"])["small_text"].ToString().Contains("CV:"), "Main singer portrait hover includes character, outfit and voice actor");
            sample.PortraitIndex = 1;
            var rotatedAssets = (Dictionary<string, object>)Json.Decode<Dictionary<string, object>>(Json.Encode(sample.ToActivity(settings, 1)))["assets"];
            Check(rotatedAssets["small_text"].ToString().Replace('\u00a0', ' ').Trim().StartsWith("Silence Suzuka") && rotatedAssets["small_image"].ToString().Contains("cw=320"), "Portrait rotation selects next main singer with a consistent frameless crop");
            sample.PortraitIndex = 3;
            rotatedAssets = (Dictionary<string, object>)Json.Decode<Dictionary<string, object>>(Json.Encode(sample.ToActivity(settings, 1)))["assets"];
            Check(rotatedAssets["small_text"].ToString().Replace('\u00a0', ' ').Trim().StartsWith("Special Week"), "Portrait rotation wraps within main performers");
            sample.PortraitIndex = 0;
            Check(timestamps.Count == 2 && Convert.ToInt64(timestamps["end"]) - Convert.ToInt64(timestamps["start"]) == 154, "Playback countdown uses song duration instead of game-session duration");
            sample.Paused = true;
            var paused = tracker.Observe(sample, DateTimeOffset.UtcNow);
            payload = Json.Decode<Dictionary<string, object>>(Json.Encode(paused.ToActivity(settings, 12345)));
            var pausedTimestamps = (Dictionary<string, object>)payload["timestamps"];
            Check(Convert.ToInt32(payload["type"]) == 0 && pausedTimestamps.Count == 1 && Convert.ToInt64(pausedTimestamps["start"]) == 12345 && payload["details"].ToString().Contains("00:39 / 02:34"), "Pause shows game-session elapsed time while preserving frozen song position");
            sample.PortraitIndex = 1;
            var rotatedPaused = Json.Decode<Dictionary<string, object>>(Json.Encode(paused.ToActivity(settings, 12345)));
            Check(Convert.ToInt64(((Dictionary<string, object>)rotatedPaused["timestamps"])["start"]) == 12345 && ((Dictionary<string, object>)rotatedPaused["assets"])["small_text"].ToString().Replace('\u00a0', ' ').Trim().StartsWith("Silence Suzuka"), "Paused portrait rotation keeps the original game-launch timestamp");
            sample.PortraitIndex = 0;
            sample.Position = 110;
            payload = Json.Decode<Dictionary<string, object>>(Json.Encode(tracker.Observe(sample, DateTimeOffset.UtcNow).ToActivity(settings, 1)));
            Check(payload["details"].ToString().Contains("01:50 / 02:34"), "Seeking while paused updates frozen time");
            sample.Paused = false;
            payload = Json.Decode<Dictionary<string, object>>(Json.Encode(tracker.Observe(sample, DateTimeOffset.UtcNow).ToActivity(settings, 1)));
            Check(((Dictionary<string, object>)payload["timestamps"]).Count == 2, "Resume restores song timestamps");
            string stageDetails = ConcertDetails.CharacterTooltip(1025, 5, "Manhattan Cafe", settings).Replace('\u00a0', ' ');
            Check(stageDetails.Contains("[Winter Uniform]") && stageDetails.Contains("CV: Yui Ogura") && !stageDetails.Contains("Racing outfit"), "Non-racing portrait uses the actual uniform name and matching CV instead of inventing a racing costume");
            string altDetails = ConcertDetails.CharacterTooltip(1025, 102526, "Manhattan Cafe", settings);
            Check(altDetails.Contains("[") && Encoding.UTF8.GetByteCount(altDetails) <= 128 && altDetails.Contains("CV:"), "Alternate racing costume resolves its card title while preserving the CV within the hover budget");
            var stageDuplicates = new ConcertSnapshot { MusicId = 1176, CastIds = new[] { 1022, 1022, 1025 }, DressIds = new[] { 2, 5, 7 } };
            var sharedStage = new ConcertSnapshot { MusicId = 1176, CastIds = new[] { 1022, 1022, 1022 }, DressIds = new[] { 2, 5, 2 } };
            var sharedAssets = (Dictionary<string, object>)((Dictionary<string, object>)sharedStage.ToActivity(settings, 1))["assets"];
            string sharedHover = sharedAssets["small_text"].ToString().Replace('\u00a0', ' ');
            sharedStage.PortraitIndex = 1;
            var winterAssets = (Dictionary<string, object>)((Dictionary<string, object>)sharedStage.ToActivity(settings, 1))["assets"];
            Check(sharedStage.PortraitSlots(settings).Length == 2 && sharedHover.Contains("Summer Uniform") && !sharedHover.Contains("Winter") && winterAssets["small_text"].ToString().Replace('\u00a0', ' ').Contains("Winter Uniform") && winterAssets["small_image"].ToString() == sharedAssets["small_image"].ToString(), "Same student image has separate summer/winter hover pages, with identical duplicate dresses collapsed");
            Check(ConcertDetails.CharacterTooltip(1143, OutfitCatalog.DressForCard(114301), "Victoire Pisa", settings).Contains("Lueur"), "New-character concert costume titles use the complete fallback catalog");
            var costumeLabels = SongCatalog.Resource<Dictionary<string, Dictionary<string, string>>>("concert-details.json")["14"];
            foreach (var pair in costumeLabels) {
                int dress; if (!int.TryParse(pair.Key, out dress)) continue;
                int character = dress >= 900000 ? dress - 900000 : dress >= 100000 && dress < 200000 ? dress / 100 : 1001;
                string hover = ConcertDetails.CharacterTooltip(character, dress, ConcertDetails.CharacterName(character, settings), settings);
                if (Encoding.UTF8.GetByteCount(hover) > 128 || !hover.Contains("[") || !hover.Contains("]") || hover.Contains("  ")) throw new Exception("Invalid catalog costume hover: " + dress);
            }
            Check(true, "All bundled costume labels retain brackets and fit the byte budget without padding");
            foreach (var song in SongCatalog.Resource<List<SongEntry>>("songs.json")) {
                var concert = new ConcertSnapshot { MusicId = song.Id, CastIds = new[] { 1001, 1002, 1003 } };
                var card = (Dictionary<string, object>)concert.ToActivity(settings, 1);
                string hover = ((Dictionary<string, object>)card["assets"])["large_text"].ToString();
                if (Encoding.UTF8.GetByteCount(hover) > 128 || hover != concert.Singers || hover.Contains("Composer:")) throw new Exception("Invalid clean concert cover: " + song.Id);
            }
            Check(true, "All bundled songs keep the clean singer cover/album line within the text budget");
            Check(stageDuplicates.PortraitSlots(settings).SequenceEqual(new[] { 0, 1, 2 }), "Different stage dresses remain separate pages even when their student image is shared");
            var rotation = new ConcertPortraitRotation(); var rotateNow = new DateTime(2026, 10, 26, 0, 0, 0, DateTimeKind.Utc);
            rotation.Observe(stageDuplicates, settings, rotateNow); rotation.Observe(stageDuplicates, settings, rotateNow.AddSeconds(10));
            Check(stageDuplicates.PortraitIndex == 0, "Portrait rotation waits for a Discord publication before starting its hold");
            rotation.Published(stageDuplicates, settings, rotateNow.AddSeconds(10)); rotation.Observe(stageDuplicates, settings, rotateNow.AddSeconds(13));
            Check(stageDuplicates.PortraitIndex == 2, "Published student portrait advances to the next distinct character after three seconds");
            rotation.Reset(); rotation.Observe(sharedStage, settings, rotateNow); rotation.Published(sharedStage, settings, rotateNow);
            rotation.Observe(sharedStage, settings, rotateNow.AddSeconds(3)); Check(sharedStage.PortraitIndex == 1, "A different costume sharing the same image advances after three acknowledged seconds");
            rotation.Published(sharedStage, settings, rotateNow.AddSeconds(3)); rotation.Observe(sharedStage, settings, rotateNow.AddSeconds(5));
            Check(sharedStage.PortraitIndex == 1, "Same-image costume page gets its full hold after publication");
            rotation.Observe(sharedStage, settings, rotateNow.AddSeconds(6)); Check(sharedStage.PortraitIndex == 0, "Same-image costume pages keep cycling instead of stalling on an unchanged image key");
            var varied = new ConcertSnapshot { MusicId = 1176, CastIds = new[] { 1022, 1022, 1025 }, DressIds = new[] { 102201, 102202, 102501 } };
            rotation.Reset(); rotation.Observe(varied, settings, rotateNow); rotation.Published(varied, settings, rotateNow); rotation.Published(varied, settings, rotateNow.AddSeconds(1)); rotation.Observe(varied, settings, rotateNow.AddSeconds(3));
            Check(varied.PortraitIndex == 2, "Incidental metadata acknowledgments do not skip portraits and another character separates racing variants");
            rotation.Published(varied, settings, rotateNow.AddSeconds(3)); rotation.Observe(varied, settings, rotateNow.AddSeconds(6));
            Check(varied.PortraitIndex == 1, "The remaining alternate costume is still shown after the other character");
            settings.ConcertArtwork["1176"] = "concert_1176";
            payload = Json.Decode<Dictionary<string, object>>(Json.Encode(sample.ToActivity(settings, 123)));
            Check(((Dictionary<string, object>)payload["assets"])["large_image"].ToString() == "concert_1176", "Custom diagonal artwork key overrides existing song icon");
            settings.ApplicationId = "1563519446503522344";
            string original = OriginalConcertCovers.Url(1195);
            Check(original != null && original.StartsWith("https://wsrv.nl/?url=") && original.Contains("Ryouran_Enbu.jpg") && SongCatalog.ImageKey(1195, settings) == original, "A song without a registered custom asset falls back to its actual public original cover for the user's application");
            settings.ConcertPreferOriginalCover = true;
            var originalPayload = Json.Decode<Dictionary<string, object>>(Json.Encode(sample.ToActivity(settings, 123)));
            var originalAssets = (Dictionary<string, object>)originalPayload["assets"];
            Check(originalAssets["large_image"].ToString() == OriginalConcertCovers.Url(1176) && originalAssets["large_image"].ToString() != "concert_1176", "Original-cover setting overrides an existing diagonal mapping in the real concert payload");
            Check(originalAssets["large_text"].Equals(((Dictionary<string, object>)payload["assets"])["large_text"]) && originalAssets["small_text"].Equals(((Dictionary<string, object>)payload["assets"])["small_text"]) && Json.Encode(originalPayload["timestamps"]) == Json.Encode(payload["timestamps"]), "Artwork preference preserves singer text, character hover, portrait and playback timing");
            Check(SongCatalog.ImageKey(999999, settings) == settings.LargeImage && SongCatalog.All.All(song => OriginalConcertCovers.Url(song.Id) != null), "Every bundled song has an original cover and an unknown song safely retains the app icon");
            Check(!OriginalConcertCovers.ValidUrl("file:///sample.png") && !OriginalConcertCovers.ValidUrl("https://unrelated.invalid/cover.png") && !OriginalConcertCovers.ValidUrl("https://umamusu.wiki.evil.invalid/w/images/a/ab/cover.png"), "Original-cover metadata rejects local paths and unrelated hosts");
            string coverJson = Json.Encode(new { query = new { pages = new Dictionary<string, object> { { "1", new { revisions = new[] { new { slots = new { main = new Dictionary<string, object> { { "*", "{{Song\n| title_jp = 繚乱縁舞\n| cover = Ryouran Enbu.jpg\n}}" } } } } } } } } } });
            Check(OriginalConcertCovers.CoverFile(coverJson, SongCatalog.Find(1195)) == "File:Ryouran Enbu.jpg" && OriginalConcertCovers.CoverFile(coverJson.Replace("繚乱縁舞", "Different song"), SongCatalog.Find(1195)) == null, "Automatic cover discovery verifies the song infobox identity before selecting its cover");
            string imageJson = Json.Encode(new { query = new { pages = new Dictionary<string, object> { { "1", new { title = "File:Ryouran Enbu.jpg", imageinfo = new[] { new { url = "https://umamusu.wiki/w/images/4/49/Ryouran_Enbu.jpg", width = 4000, height = 4000 } } } } } } });
            Check(OriginalConcertCovers.ImageUrl(imageJson, "File:Ryouran Enbu.jpg") != null && OriginalConcertCovers.ImageUrl(imageJson, "File:Other.jpg") == null && OriginalConcertCovers.ImageUrl(imageJson.Replace("https://umamusu.wiki/", "https://unrelated.invalid/"), "File:Ryouran Enbu.jpg") == null, "Cover image lookup requires the exact requested file, a square image and a trusted artwork origin");
            settings.ConcertPreferOriginalCover = false;
            Check(ConcertReader.Parse(packet, 999, tick) == null, "Rejects telemetry from another game instance");
            Check(ConcertReader.Parse(packet, 1234, tick + 11000) == null, "Rejects stale telemetry rather than inventing playback");
            set(8, BitConverter.GetBytes(3)); Check(ConcertReader.Parse(packet, 1234, tick) == null, "Rejects incomplete native write"); set(8, BitConverter.GetBytes(4));
            set(40, BitConverter.GetBytes(double.NaN)); Check(ConcertReader.Parse(packet, 1234, tick) == null, "Rejects invalid native floating-point values");
            using (var special = new Bitmap(32, 32))
            using (var cover = new Bitmap(32, 32))
            {
                using (var g = Graphics.FromImage(special)) g.Clear(Color.Magenta);
                using (var g = Graphics.FromImage(cover)) g.Clear(Color.Blue);
                using (var image = ConcertArtworkBuilder.Compose(special, cover))
                    Check(image.GetPixel(20, 20).R > 240 && image.GetPixel(490, 490).B > 240 && image.GetPixel(490, 490).R < 10, "Diagonal artwork places Special Week upper-left and cover lower-right");
            }
        }

        private static void RaceTests()
        {
            var track = new RaceSnapshot { Preparing = true, Course = 10810, Distance = 3000, Season = 3, Weather = 1, GroundCondition = 1 };
            string trackText = RaceTrackProfile.Tooltip(track).Replace('\u00a0', ' ');
            Check(trackText == "Kyoto - Turf - 3000m (Long) - Right turn - Autumn - Sunny - Firm", "Preparation profile separates the season from the weather and ground pair");
            track.Weather = 2;
            Check(RaceTrackProfile.Tooltip(track).Replace('\u00a0', ' ').Contains(" - Autumn - Cloudy - Firm"), "Preparing Room races show the Cloudy label after Autumn with the missing separator");
            track.Weather = 1;
            var trackPayload = (Dictionary<string, object>)track.ToActivity(new Settings(), 1);
            Check(((Dictionary<string, object>)trackPayload["assets"])["large_text"].ToString().Replace('\u00a0', ' ') == trackText, "Complete preparation profile reaches the activity without clipping the ground condition");
            track.Course = 0; track.Program = 684721; track.ProgramType = 2; track.Distance = 2000; track.Season = track.Weather = track.GroundCondition = 0;
            string masterTrack = RaceTrackProfile.Tooltip(track);
            Check(masterTrack.Contains("Ooi - Dirt - 2000m (Medium) - Right turn") && !masterTrack.Contains("Sunny") && !masterTrack.Contains("Firm") && !masterTrack.Contains("Autumn"), "Typed race-instance lookup resolves the course while unavailable conditions remain absent");
            track.Course = 10810; track.Distance = 2400;
            Check(RaceTrackProfile.Tooltip(track) == "2400m (Medium)", "Conflicting course distance cannot display a stale venue or turn direction");
            var extended = new byte[100];
            Action<int, int> ep = (offset, value) => Buffer.BlockCopy(BitConverter.GetBytes(value), 0, extended, offset, 4);
            ep(0, 0x52414331); ep(4, 1); ep(8, 3); ep(16, 10810); ep(20, 3000); ep(80, 0x54524b31); ep(84, 1); ep(88, 3); ep(92, 2); ep(96, 4);
            var decodedTrack = RaceSnapshot.Parse(extended);
            Check(decodedTrack.Season == 3 && decodedTrack.Weather == 2 && decodedTrack.GroundCondition == 4 && RaceTrackProfile.Tooltip(decodedTrack).Contains("Cloudy\u00a0-\u00a0Heavy"), "Versioned track extension decodes canonical values alongside the original race packet");
            ep(88, 99); ep(92, -1); ep(96, 100);
            decodedTrack = RaceSnapshot.Parse(extended);
            Check(decodedTrack.Season == 0 && decodedTrack.Weather == 0 && decodedTrack.GroundCondition == 0, "Unknown condition values never become guessed season, weather or ground labels");
            ep(80, 0); ep(88, 3); ep(92, 2); ep(96, 4);
            Check(RaceSnapshot.Parse(extended).Season == 0 && RaceSnapshot.Parse(extended.Take(80).ToArray()).Weather == 0, "Old DLL packets and unrelated reserved bytes do not fabricate track conditions");
            var profileCatalog = SongCatalog.Resource<RaceProfileCatalog>("race-profiles.json");
            var examples = new List<object>(); bool completeProfiles = true;
            foreach (var item in profileCatalog.Courses) {
                var fixture = new RaceSnapshot { Preparing = true, Course = int.Parse(item.Key), Distance = item.Value.Distance, Season = 3, Weather = 2, GroundCondition = 4 };
                string output = RaceTrackProfile.Tooltip(fixture);
                completeProfiles &= Encoding.UTF8.GetByteCount(output) <= 128 && output.EndsWith("Cloudy\u00a0-\u00a0Heavy");
                examples.Add(new { course = item.Key, utf8Bytes = Encoding.UTF8.GetByteCount(output), text = output });
            }
            Check(completeProfiles && examples.Count > 0, "Every bundled course profile retains complete weather and ground within the UTF-8 limit");
            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "track-profile-examples.json"), Json.Encode(examples), new UTF8Encoding(false));
            var bytes = new byte[76];
            Action<int, int> put = (offset, value) => Buffer.BlockCopy(BitConverter.GetBytes(value), 0, bytes, offset, 4);
            put(0, unchecked((int)0x52414331)); put(4, 1); put(8, 1); put(12, 102301); put(20, 2400);
            Buffer.BlockCopy(BitConverter.GetBytes(1200f), 0, bytes, 24, 4); put(28, 3); put(68, 9); put(72, 1);
            for (int i = 0; i < 3; i++) { put(32 + i * 4, 1001 + i); put(44 + i * 4, 1 + i * 3); put(56 + i * 4, 1 + i * 3); }
            var sample = RaceSnapshot.Parse(bytes);
            Check(sample != null && sample.Details == "Live — CM • 1200/2400 m • 50%" && sample.Line == "1st • 4th • 7th", "Live race shows leader distance and current team positions in two rows");
            var careerRace = new RaceSnapshot { Results = true, Kind = 4, Program = 102301, ProgramType = 1, Distance = 3000, Team = new[] { new RaceEntry { CharacterId = 1001, Gate = 1, Position = 4 } } };
            var rivalBytes = new byte[2188];
            Action<int, int> rp = (offset, value) => Buffer.BlockCopy(BitConverter.GetBytes(value), 0, rivalBytes, offset, 4);
            Action<int, string, int, int, int, bool, int, int> rivalRow = (slot, trainer, id, rank, gate, marked, score, skills) => {
                int offset = 28 + slot * 120; rp(offset, id); rp(offset + 4, rank); rp(offset + 8, gate); rp(offset + 12, (marked ? 1 : 0) | 6); rp(offset + 16, score); rp(offset + 20, skills);
                var trainerBytes = Encoding.UTF8.GetBytes(trainer); Buffer.BlockCopy(trainerBytes, 0, rivalBytes, offset + 24, Math.Min(96, trainerBytes.Length));
            };
            rp(0, unchecked((int)0x32564952)); rp(4, 2); rp(8, 1); rp(12, 3); rp(16, careerRace.Program); rp(20, careerRace.Kind); rp(24, careerRace.ProgramType);
            rivalRow(0, "Trainer A", 1002, 2, 2, true, 83726, 81); rivalRow(1, "Trainer B", 1003, 3, 3, false, 71777, 55); rivalRow(2, "Trainer C", 1004, 5, 5, false, 65000, 49);
            Check(RaceSnapshot.ApplyRivals(rivalBytes, careerRace) && careerRace.Rivals.Length == 3 && careerRace.Rivals[0].MarkedRival && careerRace.Rivals[0].TrainerName == "Trainer A" && careerRace.Rivals[0].LegacyScore == 83726 && careerRace.Rivals[0].LegacySkills == 81, "Career rival packet carries each trainer-owned horse's own score and acquired skills");
            var rivalV3 = new byte[2332]; Action<int, int> rp3 = (offset, value) => Buffer.BlockCopy(BitConverter.GetBytes(value), 0, rivalV3, offset, 4);
            Action<int, string, int, int, int, int, int, float> rivalRow3 = (slot, trainer, id, rank, gate, flags, marginKind, gap) => {
                int offset = 28 + slot * 128; rp3(offset, id); rp3(offset + 4, rank); rp3(offset + 8, gate); rp3(offset + 12, flags); rp3(offset + 16, 83726); rp3(offset + 20, 81);
                byte[] trainerBytes = Encoding.UTF8.GetBytes(trainer); Buffer.BlockCopy(trainerBytes, 0, rivalV3, offset + 24, Math.Min(96, trainerBytes.Length));
                rp3(offset + 120, marginKind); Buffer.BlockCopy(BitConverter.GetBytes(gap), 0, rivalV3, offset + 124, 4);
            };
            rp3(0, unchecked((int)0x33564952)); rp3(4, 3); rp3(8, 1); rp3(12, 3); rp3(16, careerRace.Program); rp3(20, careerRace.Kind); rp3(24, careerRace.ProgramType);
            rivalRow3(0, "Winner Trainer", 1002, 1, 2, 15, 5, 2.75f); rivalRow3(1, "Third Trainer", 1003, 3, 3, 14, 5, 4.5f); rivalRow3(2, "No Margin Trainer", 1004, 6, 5, 6, 0, 0);
            var v3Race = new RaceSnapshot { Results = true, Kind = 4, Program = careerRace.Program, ProgramType = careerRace.ProgramType, Team = new[] { new RaceEntry { CharacterId = 1001, Gate = 1, Position = 4 } } };
            Check(RaceSnapshot.ApplyRivals(rivalV3, v3Race) && v3Race.Rivals[0].OfficialMarginKind == 5 && v3Race.Rivals[0].OfficialGapLengths == 2.75f && v3Race.Rivals[1].OfficialGapLengths == 4.5f && v3Race.Rivals[2].OfficialMarginKind == 0, "RIV3 preserves direct winner margins and complete numeric-chain gaps while leaving unavailable values absent");
            var invalidV3 = (byte[])rivalV3.Clone(); Buffer.BlockCopy(BitConverter.GetBytes(5), 0, invalidV3, 28 + 2 * 128 + 120, 4); Buffer.BlockCopy(BitConverter.GetBytes(float.NaN), 0, invalidV3, 28 + 2 * 128 + 124, 4);
            var invalidRace = new RaceSnapshot { Results = true, Kind = 4, Program = careerRace.Program, ProgramType = careerRace.ProgramType, Team = new[] { new RaceEntry { CharacterId = 1001, Gate = 1, Position = 4 } } };
            Check(!RaceSnapshot.ApplyRivals(invalidV3, invalidRace), "RIV3 rejects a nonfinite margin instead of exposing it on a Rival hover");
            var roomRace = new RaceSnapshot { Results = true, Kind = 2, Program = careerRace.Program, ProgramType = careerRace.ProgramType, Distance = 3000, Team = new[] { new RaceEntry { CharacterId = 1001, Gate = 1, Position = 4 } } };
            var roomRivals = (byte[])rivalBytes.Clone(); Buffer.BlockCopy(BitConverter.GetBytes(roomRace.Kind), 0, roomRivals, 20, 4);
            Check(RaceSnapshot.ApplyRivals(roomRivals, roomRace) && roomRace.Rivals.Length == 3 && roomRace.RivalTooltip == null, "Room results keep their standings layout while retaining trainer-owned opponent telemetry");
            careerRace.Team[0].Metrics = new RaceMetrics { Flags = 14, Finish = 24.2f, Peak = 24.8f, Seconds = 121.3f };
            var rivalActivity = (Dictionary<string, object>)careerRace.ToActivity(new Settings { ApplicationId = "954453106765225995" }, 12345);
            var rivalAssets = (Dictionary<string, object>)rivalActivity["assets"];
            string rivalHover = rivalAssets["large_text"].ToString().Replace('\u00a0', ' ');
            Check(rivalActivity["state"].ToString() == "Special Week — 4th" && rivalActivity["details"].ToString().EndsWith("Rivals") && rivalHover.StartsWith("Trainer A • Silence Suzuka — 2nd") && rivalHover.Contains("LG12") && rivalHover.Contains("83,726 • 18% toward LG13") && rivalHover.Contains("81 acquired skills"), "Rival hover shows trainer, placement and the same rank gauge, score progress and acquired-skill count as Legacy");
            Check(rivalAssets["large_image"].ToString() == "chara_1002", "Rival portrait uses the registered character artwork for the selected Discord application");
            string rivalSmallText = rivalAssets["small_text"].ToString().Replace('\u00a0', ' ');
            Check(rivalSmallText.Contains("Special Week") && rivalSmallText.Contains("Finish:"), "Smaller game icon preserves the player's result metrics");
            Check(Encoding.UTF8.GetByteCount(rivalAssets["large_text"].ToString()) <= 128, "Rival Legacy tooltip stays within Discord's byte limit without discarding its data rows");
            var rivalTracker = new RaceTracker(); var rivalNow = new DateTime(2026, 11, 14, 0, 0, 0, DateTimeKind.Utc);
            rivalTracker.Observe(careerRace, rivalNow); rivalTracker.Published(careerRace, rivalNow);
            Check(rivalTracker.Observe(careerRace, rivalNow.AddSeconds(2.99)).Race.RivalPage == 0 && rivalTracker.Observe(careerRace, rivalNow.AddSeconds(3)).Race.RivalPage == 1 && careerRace.Line == "Special Week — 4th", "Rival hover pages advance independently while preserving the own-horse result line");
            var npcOnly = new byte[2188]; Buffer.BlockCopy(rivalBytes, 0, npcOnly, 0, npcOnly.Length); Array.Clear(npcOnly, 28 + 2 * 120 + 24, 96);
            Check(!RaceSnapshot.ApplyRivals(npcOnly, careerRace), "NPC or trainer-less entries never enter the rivals display");
            var wrongRace = (byte[])rivalBytes.Clone(); Buffer.BlockCopy(BitConverter.GetBytes(102302), 0, wrongRace, 16, 4);
            Check(!RaceSnapshot.ApplyRivals(wrongRace, careerRace), "Rival data from another race cannot attach to a current result");
            put(56, 8); put(60, 5); put(64, 6);
            var unsortedTeam = RaceSnapshot.Parse(bytes);
            Check(unsortedTeam.Team[0].CharacterId == 1001 && unsortedTeam.Team[0].Gate == 8 && unsortedTeam.Line == "1st • 4th • 7th", "Companion preserves bridge team order instead of sorting positions by gate");
            put(56, 5); put(60, 8); put(64, 4);
            var middleAnchor = RaceSnapshot.Parse(bytes);
            Check(middleAnchor.Team[0].CharacterId == 1001 && middleAnchor.Team[1].CharacterId == 1002 && middleAnchor.Line == "1st • 4th • 7th", "Live positions preserve native HUD slots even when gates are in a different order");
            put(8, 2); var orderedResults = RaceSnapshot.Parse(bytes);
            Check(orderedResults.Team[1].CharacterId == 1002 && orderedResults.Team[1].Position == 4 && orderedResults.Team[2].Position == 7, "Result cycling keeps the same Uma-slot order without changing correct placements"); put(8, 1);
            for (int i = 0; i < 3; i++) put(56 + i * 4, 1 + i * 3);
            string[] modeNames = { "Race", "CM", "Room", "LoH", "Career", "Master Challenge" };
            for (int mode = 0; mode < modeNames.Length; mode++) {
                put(72, mode); put(8, 1); Check(RaceSnapshot.Parse(bytes).Details.StartsWith("Live — " + (mode == 5 ? "MC" : modeNames[mode]) + " •"), "Live heading uses the short label for mode " + modeNames[mode]);
                put(8, 3); Check(RaceSnapshot.Parse(bytes).Details.StartsWith("Preparing — " + modeNames[mode] + " •"), "Pre-race heading identifies mode " + modeNames[mode]);
                put(8, 2); Check(RaceSnapshot.Parse(bytes).Details.StartsWith("Results — " + modeNames[mode] + " •"), "Results heading identifies mode " + modeNames[mode]);
            }
            put(8, 1);
            put(72, 1);
            var tracker = new RaceTracker(); var now = new DateTime(2026, 10, 25, 0, 0, 0, DateTimeKind.Utc);
            string paddockFixture = File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "bridge", "fixtures", "paddock-cm-live.txt"));
            var paddockSeed = new RaceSnapshot { Preparing = true, Program = 600502, ProgramType = 2, Kind = 1, Distance = 2200, Team = new[] { new RaceEntry { CharacterId = 1022, Gate = 9 } } };
            var realPaddock = PaddockInspectionReader.Parse(paddockFixture, paddockSeed);
            Check(realPaddock != null && realPaddock.Team.Select(e => e.CharacterId).SequenceEqual(new[] { 1022, 1102, 1097 }) && realPaddock.Team.Select(e => e.Gate).SequenceEqual(new[] { 9, 2, 4 }), "Captured live paddock resolves three real owned HorseData records without nested duplicates or opponents");
            Check(PaddockInspectionReader.Parse(paddockFixture.Replace("FIELD _isRaceStart : Boolean = false", "FIELD _isRaceStart : Boolean = true"), paddockSeed) == null, "The paddock compatibility reader stops when the race starts");
            string finishFixture = File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "bridge", "fixtures", "result-cm-live.txt"));
            realPaddock.Preparing = false;
            var actualFinish = RaceResultInspectionReader.Parse(finishFixture, realPaddock);
            Check(actualFinish != null && actualFinish.Results && actualFinish.Team.Select(e => e.Position).SequenceEqual(new[] { 2, 3, 6 }) && actualFinish.Team.Select(e => e.Gate).SequenceEqual(new[] { 9, 2, 4 }), "Active CM result panel switches to final placements while preserving the seed HUD order");
            Check(RaceResultInspectionReader.Parse(finishFixture.Replace("VALUE 600502", "VALUE 600503"), realPaddock) == null && RaceResultInspectionReader.Parse(finishFixture.Replace("\r\n", "\n").Replace(RaceResultInspectionReader.Active + "\nVALUE 1", RaceResultInspectionReader.Active + "\nVALUE 0"), realPaddock) == null, "Result compatibility rejects another race and an inactive result panel");
            actualFinish.Team[0].Position = 1;
            var crowned = (Dictionary<string, object>)actualFinish.ToActivity(new Settings(), 12345);
            Check(actualFinish.Line.EndsWith("1st 👑") && !((Dictionary<string, object>)crowned["assets"])["large_text"].ToString().Contains("👑"), "The result winner crown belongs to the main card and never the hover tooltip");
            var live = tracker.Observe(sample, now);
            var payload = Json.Decode<Dictionary<string, object>>(Json.Encode(live.ToActivity(new Settings(), 12345)));
            Check(((Dictionary<string, object>)payload["assets"])["large_image"].ToString() == "umaicon" && ((Dictionary<string, object>)payload["assets"])["large_text"].ToString().Contains("T.\u00a0Teio\u00a07th"), "Race tooltip keeps shortened names and positions together");
            Check(RaceCatalog.Name(102301) == "Arima Kinen", "Race title resolves Arima Kinen from the local game catalog");
            Check(RaceCatalog.Name(683021, 2) == "JBC Classic Level 3", "Master Challenge JBC Classic retains its race title and level");
            Check(RaceCatalog.Name(684721, 2) == "Teio Sho Level 3" && RaceCatalog.CourseLabel(0, 684721, 2000, 2) == "Ooi • Dirt • 2000 m", "Previously missing Master Challenge instance resolves from the installed game catalog");
            put(8, 2); sample = RaceSnapshot.Parse(bytes);
            var result = tracker.Observe(sample, now);
            Check(result.Details == "Results — CM • Arima Kinen" && result.State == "Special Week — 1st 👑", "Race finish switches to the result card and first complete name");
            tracker.Published(now);
            Check(tracker.Observe(RaceSnapshot.Parse(bytes), now.AddSeconds(2.99)).State == "Special Week — 1st 👑", "Result name holds for three seconds after acknowledgment");
            Check(tracker.Observe(RaceSnapshot.Parse(bytes), now.AddSeconds(3)).State == "Silence Suzuka — 4th", "Only the result-name row advances at three seconds");
            Check(tracker.Observe(RaceSnapshot.Parse(bytes), now.AddSeconds(10)).State == "Silence Suzuka — 4th", "Delayed Discord acknowledgments never skip names");
            tracker.Published(now.AddSeconds(10));
            var third = tracker.Observe(RaceSnapshot.Parse(bytes), now.AddSeconds(13));
            Check(third.State == "Tokai Teio — 7th" && third.Details == result.Details, "Third result retains the same static race heading");
            var thirdPayload = Json.Decode<Dictionary<string, object>>(Json.Encode(third.ToActivity(new Settings(), 12345)));
            Check(Convert.ToInt64(((Dictionary<string, object>)thirdPayload["timestamps"])["start"]) == 12345, "Result cycling preserves the game-launch timestamp");
            var skipped = new RaceTracker().Observe(RaceSnapshot.Parse(bytes), now);
            Check(skipped.State == "Special Week — 1st 👑" && skipped.Race.Results, "Skipping directly to results requires no preceding race-progress event");
            Check(tracker.Observe(null, now) == null, "Leaving results clears the result cycle");
            Check(RaceSnapshot.Ordinal(11) == "11th" && RaceSnapshot.Ordinal(12) == "12th" && RaceSnapshot.Ordinal(13) == "13th" && RaceSnapshot.Ordinal(21) == "21st", "Placement suffixes include 11th through 13th");
            put(28, 4); Check(RaceSnapshot.Parse(bytes) == null, "Race parser rejects more than three owned CM entrants"); put(28, 3);
            put(44, 0); Check(RaceSnapshot.Parse(bytes) == null, "Unknown placements never appear as real positions"); put(44, 1);
            put(8, 3);
            for (int i = 0; i < 3; i++) put(44 + i * 4, 0);
            for (int count = 1; count <= 3; count++)
            {
                put(28, count); var preparing = RaceSnapshot.Parse(bytes);
                Check(preparing.Preparing && preparing.Team.Length == count && preparing.Line == "Gate 1 • Special Week" && !preparing.Line.Contains("0th"), "Preparing shows gate and full name for a " + count + "-Uma team without fabricated ranks");
                var gateCycle = new RaceTracker(); gateCycle.Observe(RaceSnapshot.Parse(bytes), now); gateCycle.Published(now);
                var nextGate = gateCycle.Observe(RaceSnapshot.Parse(bytes), now.AddSeconds(3));
                Check(nextGate.State == (count == 1 ? "Gate 1 • Special Week" : "Gate 4 • Silence Suzuka"), "Preparing gate cycle uses exactly " + count + " registered entrants after three seconds");
                put(8, 1); for (int i = 0; i < count; i++) put(44 + i * 4, i + 1);
                var liveTeam = RaceSnapshot.Parse(bytes);
                string expectedLine = count == 1 ? "Special Week — 1st" : count == 2 ? "S. Week 1st • S. Suzuka 2nd" : "1st • 2nd • 3rd";
                Check(liveTeam.Line == expectedLine, "Live " + count + "-Uma layout uses full name, short names or positions as requested");
                put(8, 2); for (int i = 0; i < count; i++) put(44 + i * 4, i + 1);
                var smallResults = new RaceTracker(); var initial = smallResults.Observe(RaceSnapshot.Parse(bytes), now); smallResults.Published(now);
                var following = smallResults.Observe(RaceSnapshot.Parse(bytes), now.AddSeconds(3));
                Check(following.Race.Team.Length == count && following.State.Contains(count == 1 ? "Special Week" : "Silence Suzuka"), "Results cycle only through " + count + " registered entrants");
                put(8, 3); for (int i = 0; i < 3; i++) put(44 + i * 4, 0);
            }
            put(4, 0); Check(RaceSnapshot.Parse(bytes) == null, "Inactive race telemetry cannot retain a stale result card");
            var metricRace = new RaceSnapshot { Program = 102301, ProgramType = 1, Kind = 2, Distance = 2400, Team = new[] { new RaceEntry { CharacterId = 1001, Gate = 1, Position = 1 }, new RaceEntry { CharacterId = 1002, Gate = 4, Position = 4 }, new RaceEntry { CharacterId = 1003, Gate = 7, Position = 7 } } };
            var metricBytes = new byte[152];
            Action<int, int> mi = (o, n) => Buffer.BlockCopy(BitConverter.GetBytes(n), 0, metricBytes, o, 4);
            Action<int, float> mf = (o, n) => Buffer.BlockCopy(BitConverter.GetBytes(n), 0, metricBytes, o, 4);
            mi(0, 0x524d5431); mi(4, 1); mi(8, 102301); mi(12, 1); mi(16, 3); mi(20, 1);
            for (int i = 0; i < 3; i++) { int slot = (i + 2) % 3, o = 32 + i * 40; mi(o, 1001 + slot); mi(o + 4, 1 + slot * 3); mi(o + 8, 19); mf(o + 16, 24 + slot); mf(o + 20, 26 + slot); mf(o + 36, slot * 2); }
            Check(RaceMetrics.Apply(metricBytes, metricRace) && metricRace.Team[0].Metrics.Current == 24 && metricRace.Team[2].Metrics.Current == 26, "Speed rows join by character and gate while preserving the game's three-runner HUD order");
            Check(!metricRace.Team[0].Metrics.Tooltip(metricRace.Team[0], false).Replace('\u00a0', ' ').Contains("over 2nd"), "Old bridge winner zero-to-first data cannot masquerade as a measured lead over second");
            Check(new RaceMetrics { Flags = 32, GapLengths = .02f }.Tooltip(metricRace.Team[0], false).Replace('\u00a0', ' ').Contains("<0.1 L over 2nd"), "A tiny measured live lead never rounds to a false zero or an official result label");
            var speedTracker = new RaceTracker(); speedTracker.Observe(metricRace, now); speedTracker.Published(now);
            string firstLiveLine = metricRace.Line; speedTracker.Observe(metricRace, now.AddSeconds(3));
            var metricPayload = (Dictionary<string, object>)metricRace.ToActivity(new Settings(), 1);
            string metricTooltip = ((Dictionary<string, object>)metricPayload["assets"])["large_text"].ToString().Replace('\u00a0', ' ').Replace('\u202f', ' ').Replace("\u2060", "");
            Check(metricRace.Page == 1 && metricRace.Line == firstLiveLine && metricTooltip.Contains("S. Suzuka") && metricTooltip.Contains("Current: 25.0 | Peak: 27.0 m/s"), "Live hover rotates every three acknowledged seconds while all three visible positions stay fixed");
            mi(20, 2); metricRace.Results = true; metricRace.Page = 0;
            for (int i = 0; i < 3; i++) { int o = 32 + i * 40; mi(o + 8, 94); mf(o + 16, 0); mf(o + 24, 24); mf(o + 28, 132.4f); mf(o + 32, .42f); }
            mi(80, 110); mf(108, 1.7f);
            Check(RaceMetrics.Apply(metricBytes, metricRace), "Result speed packet matches final phase and identity");
            metricTooltip = ((Dictionary<string, object>)((Dictionary<string, object>)metricRace.ToActivity(new Settings(), 1))["assets"])["large_text"].ToString().Replace('\u00a0', ' ');
            Check(metricRace.Line.EndsWith("1st 👑") && metricTooltip.Contains("Special Week — 1st") && metricTooltip.Contains("Finish: 24.0") && metricTooltip.Contains("Won by 1.7 lengths") && !metricTooltip.Contains("Current:") && !metricTooltip.Contains("behind first") && !metricTooltip.Contains("👑"), "Results use measured winning margin, preserve the main-board crown and omit a false winner loss");
            foreach (var pair in new[] { new { Code = 1, Label = "nose" }, new { Code = 2, Label = "head" }, new { Code = 3, Label = "neck" } }) {
                var close = new RaceMetrics { Flags = 96, Margin = pair.Code, GapLengths = .03f };
                Check(close.Tooltip(metricRace.Team[0], true).Replace('\u00a0', ' ').Contains("Won by a " + pair.Label + "!") && close.Tooltip(new RaceEntry { CharacterId = 1002, Position = 2 }, true).Replace('\u00a0', ' ').Contains("Lost by a " + pair.Label), "Official " + pair.Label + " label describes the winner and runner-up without converting a guessed length threshold");
            }
            metricRace.Team[1].Position = 3; metricRace.Team[2].Position = 2;
            Check(metricRace.PodiumSweep && new RaceMetrics { Flags = 96, Margin = 4 }.Tooltip(metricRace.Team[0], true, metricRace.PodiumSweep).Replace('\u00a0', ' ').Contains("Special Week — 1st The Rest Nowhere") && new RaceMetrics().Tooltip(metricRace.Team[1], true, metricRace.PodiumSweep).Replace('\u00a0', ' ').StartsWith(RaceSnapshot.SweepHeading + " "), "A registered 1-3-2 team triggers the sweep and recognized large-margin winner wording");
            metricRace.Team[2].Position = 3; Check(!metricRace.PodiumSweep, "Tied ranks cannot produce a false podium sweep");
            foreach (var character in CharacterCatalog.Entries) {
                int id; if (!int.TryParse(character.Id, out id)) continue;
                var packed = new RaceMetrics { Flags = 110, Margin = 4, Finish = 26.3f, Peak = 27.4f, Seconds = 132.5f }.Tooltip(new RaceEntry { CharacterId = id, Position = 1 }, true, true);
                if (Encoding.UTF8.GetByteCount(packed) > 128 || !packed.Replace('\u00a0', ' ').StartsWith(RaceSnapshot.SweepHeading + " ")) throw new Exception("Result tooltip exceeds its budget: " + character.Name);
            }
            Check(true, "Large-margin winner plus podium sweep fits the tooltip byte limit for every catalogued full name");
            metricRace.Team[1].Position = 4; metricRace.Team[2].Position = 7;
            mi(8, 800017); Check(!RaceMetrics.Apply(metricBytes, metricRace), "Metrics from another race cannot attach to the current card"); mi(8, 102301);
            mf(48, float.NaN); Check(!RaceMetrics.Apply(metricBytes, metricRace), "Invalid speed samples are rejected without corrupting the race card"); mf(48, 0);
            mi(20, 1); Check(!RaceMetrics.Apply(metricBytes, metricRace), "Live metrics cannot leak onto a completed result card");
            Check(new RaceMetrics { Flags = 8, Seconds = 179.96f }.Tooltip(metricRace.Team[0], true).Contains("3:00.0"), "Displayed finish time carries rounded seconds into the next minute");
            var runnerUp = new RaceEntry { CharacterId = 1022, Position = 2 };
            string unverifiedMargin = new RaceMetrics { Flags = 30, GapLengths = 5.4f, GapSeconds = .7f }.Tooltip(runnerUp, true).Replace('\u00a0', ' ');
            Check(!unverifiedMargin.Contains(" L") && !unverifiedMargin.Contains("≈") && unverifiedMargin.Contains("+0.70s to first"), "An older bridge's 5.4 L estimate is withheld entirely while the verified time gap remains available");
            string officialFraction = new RaceMetrics { Flags = 94, GapLengths = 2.75f }.Tooltip(runnerUp, true).Replace('\u00a0', ' ');
            Check(officialFraction.Contains("2.75 L behind first") && !officialFraction.Contains("≈"), "Official quarter-length results retain their exact value instead of rounding to a tenth");
            string frozenFinish = new RaceMetrics { Flags = 6, Finish = 26.3f, Peak = 27.5f, Current = 6 }.Tooltip(runnerUp, false).Replace('\u202f', ' ').Replace('\u00a0', ' ').Replace("\u2060", "");
            Check(frozenFinish.Contains("Finish: 26.3") && !frozenFinish.Contains("Current:"), "A finished runner uses captured finish speed while the live race waits for the rest of the field");
        }

        private static async Task ProtocolTest()
        {
            string name = "umapyon-test-" + Guid.NewGuid().ToString("N");
            using (var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous))
            using (var client = new DiscordIpc())
            {
                var serverTask = Task.Run(async () =>
                {
                    await server.WaitForConnectionAsync();
                    var handshake = await DiscordIpc.ReadFrame(server);
                    Check(handshake.Opcode == 0 && Encoding.UTF8.GetString(handshake.Data).Contains("954453106765225995"), "Discord handshake opcode and application ID");
                    await WriteFragmented(server, 1, Json.Encode(new { cmd = "DISPATCH", evt = "READY", data = new { } }));
                    await WriteFragmented(server, 3, "ping-test");
                    bool pong = false, activity = false;
                    string nonce = null;
                    while (!pong || !activity)
                    {
                        var frame = await DiscordIpc.ReadFrame(server);
                        if (frame.Opcode == 4) { Check(Encoding.UTF8.GetString(frame.Data) == "ping-test", "Discord ping/pong"); pong = true; continue; }
                        var payload = Json.Decode<Dictionary<string, object>>(Encoding.UTF8.GetString(frame.Data));
                        Check(frame.Opcode == 1 && (string)payload["cmd"] == "SET_ACTIVITY", "Discord activity command");
                        nonce = (string)payload["nonce"];
                        var args = (Dictionary<string, object>)payload["args"];
                        Check(Convert.ToInt32(args["pid"]) == 1234 && args["activity"] != null, "Discord activity uses companion process ID");
                        await WriteFragmented(server, 1, Json.Encode(new { cmd = "SET_ACTIVITY", evt = (string)null, nonce, data = new { } }));
                        activity = true;
                    }
                    var clear = await DiscordIpc.ReadFrame(server);
                    var clearPayload = Json.Decode<Dictionary<string, object>>(Encoding.UTF8.GetString(clear.Data));
                    Check(((Dictionary<string, object>)clearPayload["args"])["activity"] == null, "Clears presence explicitly");
                    await WriteFragmented(server, 1, Json.Encode(new { cmd = "SET_ACTIVITY", evt = (string)null, nonce = (string)clearPayload["nonce"], data = new { } }));
                    var rejected = await DiscordIpc.ReadFrame(server);
                    var rejectedPayload = Json.Decode<Dictionary<string, object>>(Encoding.UTF8.GetString(rejected.Data));
                    await WriteFragmented(server, 1, Json.Encode(new { cmd = "SET_ACTIVITY", evt = "ERROR", nonce = (string)rejectedPayload["nonce"], data = new { code = 4000, message = "test rejection" } }));
                });
                await client.Connect("954453106765225995", name);
                Check(client.IsConnected, "Waits for Discord READY before connected");
                await client.SetActivity(new ActivityState().ToActivity(new Settings(), 12345), 1234);
                await client.SetActivity(null, 1234);
                bool error = false;
                try { await client.SetActivity(new { details = "rejection test" }, 1234); } catch (IOException ex) { error = ex.Message.Contains("test rejection"); }
                Check(error, "Surfaces Discord activity rejections instead of reporting success");
                await serverTask;
                client.Dispose();
                Check(!client.IsConnected, "Disconnect closes IPC");
            }
        }
        private static async Task WriteFragmented(Stream stream, int opcode, string json)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            byte[] frame = BitConverter.GetBytes(opcode).Concat(BitConverter.GetBytes(bytes.Length)).Concat(bytes).ToArray();
            // Split headers and bodies to exercise real byte-stream framing.
            for (int offset = 0; offset < frame.Length; offset += 7) await stream.WriteAsync(frame, offset, Math.Min(7, frame.Length - offset));
            await stream.FlushAsync();
        }
        private static async Task OcrTest()
        {
            var reader = new ScreenReader();
            reader.Initialize("auto");
            Console.WriteLine(reader.Status);
            Check(reader.Status.StartsWith("Windows OCR:"), "Built-in Windows OCR initializes on this PC");
            using (var image = new Bitmap(700, 600))
            {
                using (var graphics = Graphics.FromImage(image))
                using (var font = new Font("Segoe UI", 25))
                {
                    graphics.Clear(Color.White);
                    graphics.DrawString("Special Week\nSpeed Stamina Power\nGuts Wisdom\nEnergy Mood\nNext Goal", font, Brushes.Black, 20, 20);
                }
                string text = await reader.Read(image, "auto");
                var result = TextClassifier.Classify(text);
                Check(result != null && result.Kind == "Training", "Real Windows OCR recognizes generated training fixture");
            }
        }
    }
}
