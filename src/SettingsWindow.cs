using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace UmapyonPresence
{
    internal class SettingsWindow : Form
    {
        private readonly Settings settings;
        private readonly Func<string> diagnostic;
        private readonly Func<int> gamePid;
        private readonly Action changed;
        private readonly SettingsActions actions;
        private Image brandArtwork;
        private readonly Panel content = new Panel { Dock = DockStyle.Fill };
        private readonly Dictionary<string, Control> pages = new Dictionary<string, Control>();
        private readonly Dictionary<string, Button> navigation = new Dictionary<string, Button>();
        private readonly Label breadcrumb = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false, ForeColor = DesktopDesign.Muted, Font = DesktopDesign.Heading(8.5f) };
        private readonly DesktopStatusGrid signals = new DesktopStatusGrid();
        private readonly Label overviewStatus = SettingsUi.Text(""), overviewIntegration = SettingsUi.Text("");
        private readonly TextBox sources = SettingsUi.Report(180), diagnostics = SettingsUi.Report(280), feedback = SettingsUi.Report(160);
        private readonly Label connectionSummary = SettingsUi.Text("Click Check connection to see the current status.");
        private readonly ComboBox songs = new ComboBox { Font=DesktopDesign.Body(), DropDownStyle = ComboBoxStyle.DropDownList, Width = 620, Tag = "wide" };
        private readonly TextBox asset = new TextBox { Font=DesktopDesign.Body(), Width = 260 };
        private readonly CheckBox confirmed = SettingsUi.Check("I uploaded this asset to this Discord application", false);
        private readonly Panel notice = new Panel { Dock = DockStyle.Bottom, Height = 92, BackColor = Color.FromArgb(225, 241, 233), Visible = false, Padding = new Padding(18, 10, 8, 10), AccessibleName = "Save confirmation" };
        private readonly Label noticeText = new Label { Font=DesktopDesign.Body(), Dock = DockStyle.Fill, ForeColor = Color.FromArgb(24, 78, 52), TextAlign = ContentAlignment.MiddleLeft };
        private readonly Timer noticeTimer = new Timer { Interval = 14000 }, refresh = new Timer { Interval = 1500 };
        private SongEntry[] songItems;
        internal string SelectedPage { get; private set; }
        internal string SavedMessage { get { return noticeText.Text; } }
        internal IEnumerable<string> PageNames { get { return pages.Keys; } }
        internal SettingsWindow(Settings settings, Func<string> diagnostic, Func<int> gamePid, Action changed, Func<Task> generateCover, SettingsActions actions = null)
        {
            this.settings = settings; this.diagnostic = diagnostic; this.gamePid = gamePid; this.changed = changed; this.actions = actions ?? new SettingsActions();
            SuspendLayout();
            Text = "UmaPresence"; StartPosition = FormStartPosition.CenterScreen;
            AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
            Font = DesktopDesign.Body(); Size = new Size(1140, 820); MinimumSize = new Size(920, 640); BackColor = DesktopDesign.Background;
            using (var stream = typeof(Program).Assembly.GetManifestResourceStream("umapyon.ico")) if (stream != null) using (var icon = new Icon(stream)) Icon = (Icon)icon.Clone();
            using (var stream = typeof(Program).Assembly.GetManifestResourceStream("special-week.png")) if(stream != null)using(var image = Image.FromStream(stream))brandArtwork = new Bitmap(image,new Size(96,96));
            if (brandArtwork == null)
                using (var stream = typeof(Program).Assembly.GetManifestResourceStream("umapyon.png"))
                    if (stream != null) using (var image = Image.FromStream(stream)) brandArtwork = new Bitmap(image);
            var toolbar = new Panel { Dock = DockStyle.Top, Height = 56, Padding = new Padding(26, 9, 26, 9), BackColor = Color.White };
            var help = SettingsUi.Button("Help & support", () => Navigate("Help & diagnostics")); help.Dock = DockStyle.Right;
            toolbar.Controls.Add(breadcrumb); toolbar.Controls.Add(help);
            toolbar.Paint += (s,e) => { using (var pen = new Pen(DesktopDesign.Border)) e.Graphics.DrawLine(pen, 0, toolbar.Height-1, toolbar.Width, toolbar.Height-1); };
            var workspace = new Panel { Dock = DockStyle.Fill, BackColor = DesktopDesign.Background }; workspace.Controls.Add(content); workspace.Controls.Add(toolbar);
            var sideHost = new Panel { Dock = DockStyle.Left, Width = 232, BackColor = DesktopDesign.Sidebar };
            var brand = new Panel { Dock = DockStyle.Top, Height = 78 };
            brand.Paint += (s,e) => {
                int p = DesktopDesign.Px(brand, 18), tile = DesktopDesign.Px(brand, 42);
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (var path = DesktopDesign.Round(new RectangleF(p,p,tile,tile),DesktopDesign.Px(brand,10))) {
                    var state=e.Graphics.Save();e.Graphics.SetClip(path);
                    if(brandArtwork!=null)e.Graphics.DrawImage(brandArtwork,new Rectangle(p,p,tile,tile));
                    else using(var brush=new SolidBrush(DesktopDesign.Accent))e.Graphics.FillPath(brush,path);
                    e.Graphics.Restore(state);
                }
                using (var font = DesktopDesign.Heading(13)) TextRenderer.DrawText(e.Graphics,"UmaPresence",font,new Point(p+tile+DesktopDesign.Px(brand,10),p+DesktopDesign.Px(brand,9)),Color.White);
            };
            var sidebar = new FlowLayoutPanel { AutoScroll = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(12, 4, 12, 12), BackColor = DesktopDesign.Sidebar };
            sidebar.SizeChanged += (s,e) => { foreach (Control button in sidebar.Controls) button.Width = Math.Max(100, sidebar.ClientSize.Width - sidebar.Padding.Horizontal); };
            sideHost.Controls.Add(sidebar); sideHost.Controls.Add(brand);
            var dismiss = SettingsUi.Button("Dismiss", () => { noticeTimer.Stop(); notice.Visible = false; }); dismiss.Dock = DockStyle.Right;
            notice.Controls.Add(noticeText); notice.Controls.Add(dismiss);
            Controls.Add(workspace); Controls.Add(sideHost); Controls.Add(notice);
            var overview = SettingsUi.Page("Overview", "Your game, Discord and integration at a glance.");
            overview.Controls.Add(signals);
            overview.Controls.Add(SettingsUi.Section("Game integration")); overview.Controls.Add(overviewIntegration);
            overview.Controls.Add(SettingsUi.Row(SettingsUi.Button("Change integration", () => Navigate("Game integration")), SettingsUi.Button("Concert settings", () => Navigate("Concert"))));
            overview.Controls.Add(SettingsUi.Section("Session & launch"));
            var pause = SettingsUi.Button("Pause / resume presence", () => { this.actions.Pause(!this.actions.Paused()); RefreshOverview(); }); overview.Controls.Add(pause);
            overview.Controls.Add(SettingsUi.Text("Pausing presence clears the Discord activity; it does not pause the game."));
            var minimized = SettingsUi.Check("Start minimized to the tray", settings.StartMinimized); overview.Controls.Add(minimized);
            var startup = SettingsUi.Check("Run when I sign in to Windows", this.actions.Startup()); overview.Controls.Add(startup);
            var saveLaunch = SettingsUi.Button("Save changes", () => Run(() => {
                bool old = settings.StartMinimized; bool oldStartup = this.actions.Startup();
                try { settings.StartMinimized = minimized.Checked; settings.Save(); this.actions.SetStartup(startup.Checked); }
                catch { settings.StartMinimized = old; try { settings.Save(); this.actions.SetStartup(oldStartup); } catch (Exception rollback) { Log.Write("Startup preference rollback: " + rollback.Message); } throw; }
            }, "Saved. Launch preferences apply the next time UmaPresence starts."));
            AddPage("Overview", SettingsUi.StickySave(overview, saveLaunch), sidebar);
            AddPage("Presence", BuildPresence(), sidebar);
            AddPage("Concert", new ConcertSettingsPage(settings, gamePid, changed, Saved, this.actions.CurrentSong, this.actions.RuntimePreferences), sidebar);
            AddPage("Game integration", new IntegrationSettingsPage(settings, gamePid, changed, Saved, ShowIntegrationHelp), sidebar);
            AddPage("Data & artwork", BuildData(generateCover), sidebar);
            AddPage("Help & diagnostics", BuildHelp(), sidebar);
            noticeTimer.Tick += (s, e) => { noticeTimer.Stop(); notice.Visible = false; };
            refresh.Tick += (s, e) => { if (Visible && SelectedPage == "Overview") RefreshOverview(); }; refresh.Start();
            VisibleChanged += (s, e) => { refresh.Enabled = Visible; if (!Visible) noticeTimer.Stop(); };
            FormClosing += (s, e) => { if (e.CloseReason != CloseReason.UserClosing || actions == null) return; e.Cancel = true; Hide(); if (!settings.TrayCloseHintShown) { settings.TrayCloseHintShown = true; try { settings.Save(); } catch (Exception ex) { Log.Write(ex.Message); } MessageBox.Show("UmaPresence is still running in the tray. Click its icon to reopen, or right-click → Quit to stop it.", "Running in the tray", MessageBoxButtons.OK, MessageBoxIcon.Information); } };
            LoadSongs(); ResumeLayout(true); Navigate("Overview");
            Shown += (s,e) => { if (Location.X < -10000 || Location.Y < -10000) return; var area = Screen.FromControl(this).WorkingArea; if (Width > area.Width - 24 || Height > area.Height - 24) { MinimumSize = new Size(Math.Min(MinimumSize.Width, area.Width-24), Math.Min(MinimumSize.Height, area.Height-24)); Size = new Size(Math.Min(Width,area.Width-24), Math.Min(Height,area.Height-24)); CenterToScreen(); } };
        }
        private void AddPage(string name, Control page, FlowLayoutPanel sidebar)
        {
            PreparePage(page);
            page.Dock = DockStyle.Fill; page.Visible = false; pages.Add(name, page); content.Controls.Add(page);
            var button = new DesktopNavigation { Text = name == "Help & diagnostics" ? "Help & support" : name, Width = sidebar.Width - sidebar.Padding.Horizontal, Height = 46, Symbol = navigation.Count, Margin = new Padding(0,0,0,6), ForeColor = Color.White, BackColor = DesktopDesign.Sidebar, AccessibleName = name };
            button.Click += (s, e) => Navigate(name); navigation.Add(name, button); sidebar.Controls.Add(button);
        }
        private static void PreparePage(Control page)
        {
            var stack = page as DesktopStack;
            if (stack != null && !stack.Card) stack.Organize();
            foreach (Control child in page.Controls.Cast<Control>().ToArray()) PreparePage(child);
        }
        internal void Navigate(string name)
        {
            if (!pages.ContainsKey(name)) return;
            foreach (var entry in pages) entry.Value.Visible = entry.Key == name;
            pages[name].BringToFront(); SelectedPage = name;
            foreach (var entry in navigation) { ((DesktopNavigation)entry.Value).Selected = entry.Key == name; entry.Value.Invalidate(); entry.Value.AccessibleDescription = entry.Key == name ? "Selected page" : "Open " + entry.Key; }
            breadcrumb.Text = name == "Help & diagnostics" ? "Help & support" : name;
            pages[name].PerformLayout();
            if (name == "Overview") RefreshOverview();
            if (name == "Data & artwork") { sources.Text = DataMaintenance.Sources(settings); ShowArtwork(); }
        }
        internal void Saved(string text)
        {
            noticeText.Text = text;
            int buttonWidth = notice.Controls.OfType<Button>().First().Width;
            int height = TextRenderer.MeasureText(text, noticeText.Font, new Size(Math.Max(180, ClientSize.Width - buttonWidth - notice.Padding.Horizontal - 20), 0), TextFormatFlags.WordBreak).Height;
            notice.Height = Math.Max(DesktopDesign.Px(notice,64), height + notice.Padding.Vertical + DesktopDesign.Px(notice,8)); notice.Visible = true; noticeTimer.Stop(); noticeTimer.Start();
        }
        private void Run(Action action, string success = null)
        {
            try { action(); changed(); if (success != null) Saved(success); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "UmaPresence", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
        private void RefreshOverview()
        {
            string text = actions.Overview();
            string[] lines = text.Split(new[] { "\r\n" }, StringSplitOptions.None);
            Func<string,string> value = key => { string line = lines.FirstOrDefault(item => item.StartsWith(key + ":")); return line == null ? "Waiting" : line.Substring(key.Length+1).Trim(); };
            signals.UpdateValues(new[] { value("Game"), actions.Paused() ? "Presence paused" : value("Discord"), value("Activity") });
            string integration = string.Join("\r\n", IntegrationProfiles.Status(settings, gamePid()).Split(new[] { "\r\n" }, StringSplitOptions.None).WhereLine()); if (overviewIntegration.Text != integration) overviewIntegration.Text = integration;
        }
        private Control BuildPresence()
        {
            var page = SettingsUi.Page("Presence", "Choose how your activity and character artwork appear on Discord.");
            CheckBox characters = SettingsUi.Check("Show character portraits", settings.ShowCharacter), rotate = SettingsUi.Check("Rotate concert portraits", settings.ConcertRotatePortraits), originals = SettingsUi.Check("Prefer original song covers", settings.ConcertPreferOriginalCover), lookup = SettingsUi.Check("Look up missing racing portraits online", settings.AutoRefreshOutfitPortraits), ocr = SettingsUi.Check("Use screen text as a detection fallback", settings.OcrDetection);
            page.Controls.Add(SettingsUi.Section("Artwork & rotation")); page.Controls.Add(characters); page.Controls.Add(rotate);
            page.Controls.Add(SettingsUi.Text("Seconds to show each concert portrait")); var seconds = new NumericUpDown { Font=DesktopDesign.Body(), Minimum = 3, Maximum = 60, Value = settings.ConcertPortraitIntervalSeconds, Width = 90 }; page.Controls.Add(seconds);
            page.Controls.Add(originals);
            page.Controls.Add(SettingsUi.Text("Keep this checked for original concert covers. To use a custom cover, register it in Data & artwork → Song covers, then clear this box and save. Songs without a custom cover still use the original where available."));
            page.Controls.Add(lookup);
            page.Controls.Add(SettingsUi.Section("Activity detection")); page.Controls.Add(ocr); page.Controls.Add(SettingsUi.Text("Screen text is processed locally. Native game data remains the preferred source."));
            var savePresence = SettingsUi.Button("Save changes", () => Run(() => {
                var before = Json.Decode<Settings>(Json.Encode(settings));
                settings.ShowCharacter = characters.Checked; settings.ConcertRotatePortraits = rotate.Checked; settings.ConcertPortraitIntervalSeconds = (int)seconds.Value; settings.ConcertPreferOriginalCover = originals.Checked; settings.AutoRefreshOutfitPortraits = lookup.Checked; settings.OcrDetection = ocr.Checked;
                try { settings.Save(); } catch { settings.ShowCharacter = before.ShowCharacter; settings.ConcertRotatePortraits = before.ConcertRotatePortraits; settings.ConcertPortraitIntervalSeconds = before.ConcertPortraitIntervalSeconds; settings.ConcertPreferOriginalCover = before.ConcertPreferOriginalCover; settings.AutoRefreshOutfitPortraits = before.AutoRefreshOutfitPortraits; settings.OcrDetection = before.OcrDetection; throw; }
            }, "Saved. Presence preferences apply now; Discord may take a moment to display the update."));
            page.Controls.Add(SettingsUi.Section("Manual override"));
            var activity = SettingsUi.Choices(new[] { "Auto", "Home", "Training", "Race", "Story", "Live", "Gacha" }, actions.Activity()); page.Controls.Add(activity);
            page.Controls.Add(SettingsUi.Row(SettingsUi.Button("Use selected activity", () => Run(() => actions.SelectActivity(activity.SelectedItem.ToString()), "Activity changed for this session. Takes effect now.")), SettingsUi.Button("Choose character…", actions.ChooseCharacter)));
            page.Controls.Add(SettingsUi.Text("Automatic is recommended. Manual activity resets when the game closes."));
            return SettingsUi.StickySave(page, savePresence);
        }
        private Control BuildData(Func<Task> generateCover)
        {
            var page = SettingsUi.Page("Data & artwork", "Refresh metadata after updates and manage Discord song covers.");
            page.Controls.Add(SettingsUi.Section("Game & translation data"));
            page.Controls.Add(SettingsUi.Text("These actions read installed files. They do not download game updates or change the accepted tooltip layouts."));
            page.Controls.Add(SettingsUi.Row(SettingsUi.Button("Refresh game data", () => Run(() => { DataMaintenance.RefreshGame(settings); LoadSongs(); sources.Text = DataMaintenance.Sources(settings); }, "Game data refreshed. Updated metadata applies now.")), SettingsUi.Button("Reload translations", () => Run(() => {
                string result = DataMaintenance.RefreshTranslations(settings);
                LoadSongs();
                sources.Text = DataMaintenance.Sources(settings);
                Saved(result);
            }))));
            page.Controls.Add(sources);
            page.Controls.Add(SettingsUi.Section("Song covers"));
            page.Controls.Add(SettingsUi.Text("Custom covers require upload access to the Discord application opened by Open Discord assets. Upload your PNG there, select the matching song here, and register the uploaded asset name."));
            page.Controls.Add(songs);
            songs.SelectedIndexChanged += (s, e) => { confirmed.Checked = false; ShowArtwork(); };
            Button generate = null; generate = SettingsUi.Button("Generate current cover", async () => { generate.Enabled = false; try { await generateCover(); changed(); ShowArtwork(); Saved("Cover generated. Upload it to Discord, then register the asset below."); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "Cover generation", MessageBoxButtons.OK, MessageBoxIcon.Error); } finally { if (!generate.IsDisposed) generate.Enabled = true; } });
            page.Controls.Add(SettingsUi.Row(generate, SettingsUi.Button("Open cover folder", () => Run(() => { string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "generated-artwork"); Directory.CreateDirectory(path); Process.Start("explorer.exe", "\"" + path + "\""); })), SettingsUi.Button("Save cover as…", () => Run(() => {
                string path = SelectedCover(); if (!File.Exists(path)) throw new IOException("Generate this song's cover first.");
                using (var save = new SaveFileDialog { Filter = "PNG image|*.png", FileName = Path.GetFileName(path) }) if (save.ShowDialog(this) == DialogResult.OK && !string.Equals(Path.GetFullPath(save.FileName), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)) File.Copy(path, save.FileName, true);
            }))));
            page.Controls.Add(feedback); page.Controls.Add(SettingsUi.Text("Discord asset name")); page.Controls.Add(asset); page.Controls.Add(confirmed);
            page.Controls.Add(SettingsUi.Row(SettingsUi.Button("Open Discord assets", () => Run(() => Process.Start("https://discord.com/developers/applications/" + settings.ApplicationId + "/rich-presence/assets"))), SettingsUi.Button("Register uploaded asset", () => Run(() => { if (!confirmed.Checked) throw new InvalidOperationException("Upload the PNG to Discord first, then check the confirmation box."); DataMaintenance.RegisterAsset(settings, SelectedSong().Id, asset.Text.Trim()); LoadSongs(); },
                settings.ConcertPreferOriginalCover
                    ? "Cover registered. To use it, clear Prefer original song covers in Presence and save."
                    : "Cover registered. Discord may take a moment to show the updated cover."))));
            return page;
        }
        private Control BuildHelp()
        {
            var page = SettingsUi.Page("Help & support", "Check your connection or choose a setup guide below.");
            page.Controls.Add(SettingsUi.Section("Connection check"));
            page.Controls.Add(connectionSummary);
            page.Controls.Add(SettingsUi.Row(SettingsUi.Button("Check connection", () => HelpAction(CheckConnection)), SettingsUi.Button("Open Game integration", () => Navigate("Game integration")),
                SettingsUi.Button("Open app folder", () => HelpAction(() => {
                    using (Process.Start(new ProcessStartInfo(AppDomain.CurrentDomain.BaseDirectory) { UseShellExecute = true })) { }
                }))));
            helpGuide = new DesktopHelp();
            page.Controls.Add(helpGuide);
            page.Controls.Add(SettingsUi.Section("Support report"));
            page.Controls.Add(SettingsUi.Text("View the technical details before sharing. Reports may include local paths and activity details."));
            diagnostics.Visible = false; diagnostics.Tag = "hidden";
            var reportActions = SettingsUi.Row(SettingsUi.Button("Copy report", () => HelpAction(() => { Clipboard.SetText(diagnostics.Text); Saved("Report copied."); })), SettingsUi.Button("Save report…", () => HelpAction(() => {
                using (var save = new SaveFileDialog { Filter = "Text report|*.txt", FileName = "UmaPresence-diagnostics-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt" }) {
                    if (save.ShowDialog(this) != DialogResult.OK) return;
                    File.WriteAllText(save.FileName, diagnostics.Text, new UTF8Encoding(false));
                    Saved("Report saved.");
                }
            })));
            reportActions.Visible = false; reportActions.Tag = "hidden";
            Button showReport = null;
            showReport = SettingsUi.Button("View support report", () => HelpAction(() => {
                bool show = diagnostics.Tag as string == "hidden";
                if (show) diagnostics.Text = Report();
                diagnostics.Visible = reportActions.Visible = show;
                diagnostics.Tag = show ? "wide" : "hidden";
                reportActions.Tag = show ? null : "hidden";
                showReport.Text = show ? "Hide support report" : "View support report";
                page.PerformLayout();
            }));
            page.Controls.Add(showReport);
            page.Controls.Add(diagnostics);
            page.Controls.Add(reportActions);
            return page;
        }
        private DesktopHelp helpGuide;
        private void ShowIntegrationHelp(int topic)
        {
            Navigate("Help & diagnostics");
            helpGuide.SelectedTopic = topic;
        }
        private void HelpAction(Action action)
        {
            try { action(); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Help & support", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
        private void CheckConnection()
        {
            bool running = gamePid() != 0;
            string overview = actions.Overview();
            string bridge = running ? actions.BridgeStatus() : "Waiting for game";
            string next = !running ? "Launch Umamusume through DMM, then check again."
                : actions.Paused() ? "Resume presence from Overview."
                : overview.Contains("discord_rpc is enabled") ? "Disable Hachimi’s built-in Discord activity, then restart the game."
                : bridge == "Disabled in settings" ? "The companion’s native bridge option is disabled. Check your settings.json configuration."
                : bridge.StartsWith("Bridge not loaded", StringComparison.Ordinal) ? "Close the game and apply your mode under Game integration. Then relaunch through DMM."
                : bridge.StartsWith("Bridge stopped", StringComparison.Ordinal) ? "Check that your game build is supported before reinstalling."
                : overview.Contains("Waiting for Discord desktop") ? "Open Discord desktop and turn on activity sharing."
                : overview.Contains("Presence acknowledged by Discord") ? "Discord received the activity. If it is hidden, check Discord’s activity sharing settings."
                : "Check again after the game and Discord finish starting.";
            connectionSummary.Text = overview + "\r\nBridge: " + bridge + "\r\n" + next + "\r\nChecked at " + DateTime.Now.ToString("HH:mm:ss");
            connectionSummary.Parent.PerformLayout();
            if (diagnostics.Tag as string != "hidden") diagnostics.Text = Report();
        }
        private SongEntry SelectedSong() { if (songs.SelectedIndex < 0 || songItems == null) throw new InvalidOperationException("Select a song first."); return songItems[songs.SelectedIndex]; }
        private string SelectedCover() { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "generated-artwork", "concert_" + SelectedSong().Id + ".png"); }
        private void LoadSongs()
        {
            int selected = songs.SelectedIndex < 0 || songItems == null ? 0 : SelectedSong().Id; songItems = SongCatalog.All.OrderBy(song => song.Id).ToArray(); songs.Items.Clear();
            foreach (var song in songItems) songs.Items.Add(song.Id + " — " + song.Title + (settings.ConcertArtwork != null && settings.ConcertArtwork.ContainsKey(song.Id.ToString()) ? " [registered]" : ""));
            if (songItems.Length > 0) songs.SelectedIndex = Math.Max(0, Array.FindIndex(songItems, song => song.Id == selected));
        }
        private void ShowArtwork()
        {
            if (songs.SelectedIndex < 0) return; var song = SelectedSong(); string configured; asset.Text = settings.ConcertArtwork != null && settings.ConcertArtwork.TryGetValue(song.Id.ToString(), out configured) ? configured : "concert_" + song.Id;
            feedback.Text = "Song: " + song.Title + "\r\nCover: " + SelectedCover() + "\r\n" + (File.Exists(SelectedCover()) ? "Local PNG ready" : "Local cover not generated") + "\r\nOriginal cover: " + OriginalConcertCovers.Source(song.Id) + "\r\nSuggested asset name: concert_" + song.Id;
        }
        private string Report() { return diagnostic() + "\r\n\r\n" + DataMaintenance.BridgeStatus(settings, gamePid()) + "\r\n\r\n" + DataMaintenance.Sources(settings) + "\r\n\r\nReports include local paths and current presence details. Review before sharing."; }
        protected override void Dispose(bool disposing) { if (disposing) { noticeTimer.Dispose(); refresh.Dispose(); if(brandArtwork!=null){brandArtwork.Dispose();brandArtwork=null;} } base.Dispose(disposing); }
    }
}
