using System;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace UmapyonPresence
{
    internal sealed class IntegrationSettingsPage : UserControl
    {
        private readonly Settings settings;
        private readonly Func<int> pid;
        private readonly Action changed;
        private readonly Action<string> saved;
        private readonly Action<int> openHelp;
        private readonly Timer refresh = new Timer { Interval = 1500 };
        private readonly Label status = SettingsUi.Text("");
        private readonly TextBox details = SettingsUi.Report(160);
        private readonly Button uninstall;
        private readonly Button checkCompatibility, installUpdate, restoreUpdate, updateHelp, stockDmm;
        private StandaloneUpdateOffer updateOffer;
        private string checkResult, updateError;
        private bool checking;
        private int helpTopic = DesktopHelp.UpdateTopic;
        private void Run(Action action, string message)
        {
            if (checking) { saved("Wait for the current update check or install to finish, then try again."); return; }
            try { action(); changed(); RefreshStatus(); if (message != null) saved(message); }
            catch (Exception ex) { updateError = ex.Message; RefreshStatus(); MessageBox.Show(this, ex.Message, "Game integration", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
        internal IntegrationSettingsPage(Settings settings, Func<int> pid, Action changed, Action<string> saved = null, Action<int> openHelp = null)
        {
            this.settings = settings; this.pid = pid; this.changed = changed; this.saved = saved ?? (text => MessageBox.Show(this, text, "Game integration")); Dock = DockStyle.Fill; AutoScaleMode = AutoScaleMode.None;
            this.openHelp = openHelp;
            var page = SettingsUi.Page("Game integration", "Choose which integration loads with the game. Switching requires closing and relaunching the game; your PC does not need a restart."); Controls.Add(page);
            page.Controls.Add(status);
            checkCompatibility = SettingsUi.Button("Check compatibility", async () => await CheckCompatibility());
            installUpdate = SettingsUi.Button("Install Standalone update", async () => await InstallUpdate());
            restoreUpdate = SettingsUi.Button("Restore previous Standalone", () => Run(() => {
                StandaloneUpdates.Restore(settings);
                checkResult = "Previous Standalone restored. Launch through DMM to check it.";
            }, null));
            updateHelp = SettingsUi.Button("Help with this status", () => this.openHelp?.Invoke(helpTopic));
            updateHelp.Visible = this.openHelp != null;
            installUpdate.Visible = restoreUpdate.Visible = false;
            page.Controls.Add(SettingsUi.Row(checkCompatibility, installUpdate, restoreUpdate, updateHelp));
            if (ReleasePolicy.PublicRelease) {
                page.Controls.Add(SettingsUi.Section("Official loader helper"));
                page.Controls.Add(SettingsUi.Text("Setup needs Cellar v2.0.0 from its official GitHub release. Download or select it below. Its checksum and Windows x64 format are verified before use."));
                Button download = null;
                download = SettingsUi.Button("Download official helper", async () => {
                    download.Enabled = false;
                    try { await LoaderHelper.Download(); this.saved("Official helper downloaded and verified. You can prepare profiles now."); }
                    catch (Exception error) { MessageBox.Show(this, error.Message, "Helper download failed"); }
                    finally { if (!download.IsDisposed) download.Enabled = true; }
                });
                page.Controls.Add(SettingsUi.Row(download, SettingsUi.Button("Select downloaded helper…", () => {
                    using (var file = new OpenFileDialog { Filter = "Cellar DLL|*.dll", Title = "Select official Cellar v2.0.0" }) {
                        if (file.ShowDialog(this) == DialogResult.OK) Run(() => LoaderHelper.Import(file.FileName), "Helper verified. You can prepare profiles now.");
                    }
                })));
            }
            page.Controls.Add(SettingsUi.Section("Integration mode"));
            var mode = SettingsUi.Choices(new[] { "Hachimi", "Standalone" }, settings.GameIntegrationMode); page.Controls.Add(mode);
            page.Controls.Add(SettingsUi.Text("This is the existing loader switch. Starting profile below tells you whether setup began from original DMM or Hachimi Edge; the selected mode is what UmaPresence will apply."));
            page.Controls.Add(SettingsUi.Button("Apply switch", () => Run(() => {
                if (GameInstallationDiscovery.IsGameRunning(settings.GameDirectory)) {
                    this.saved("Close Umamusume normally, then click Apply switch again.");
                    return;
                }
                if (mode.SelectedItem.ToString() == "Hachimi") IntegrationProfiles.InstallHachimi(settings);
                else IntegrationProfiles.InstallStandalone(settings);
                this.saved(settings.GameIntegrationMode + " configured with the UmaPresence bridge. Launch Umamusume through DMM.");
            }, null)));
            page.Controls.Add(SettingsUi.Section("Game folder"));
            page.Controls.Add(SettingsUi.Text("UmaPresence fills in the folder when it finds one installation. Use Browse if the path is blank or you want a different folder."));
            var folder = new TextBox { Font=DesktopDesign.Body(), Text = settings.GameDirectory, Width = 620, Tag = "wide", ReadOnly = true }; page.Controls.Add(folder);
            page.Controls.Add(SettingsUi.Row(
                SettingsUi.Button("Detect again", () => Run(() => {
                    var found = GameInstallationDiscovery.Find(settings);
                    if (found.Length != 1) throw new InvalidOperationException("A unique installation could not be found. Use Browse to choose your game folder.");
                    SaveFolder(found[0]); folder.Text = settings.GameDirectory;
                }, "Saved. Game folder updated. Refresh game data from Data & artwork if this is a different installation.")),
                SettingsUi.Button("Browse…", () => { using (var browse = new FolderBrowserDialog { Description = "Choose the folder containing umamusume.exe", SelectedPath = settings.GameDirectory }) if (browse.ShowDialog(this) == DialogResult.OK) Run(() => { SaveFolder(browse.SelectedPath); folder.Text = settings.GameDirectory; }, "Saved. Game folder updated. Refresh game data from Data & artwork if this is a different installation."); })));
            page.Controls.Add(SettingsUi.Button("Install Standalone", () => Run(() => {
                IntegrationProfiles.InstallStandalone(settings);
                mode.SelectedItem = settings.GameIntegrationMode;
            }, "Standalone installed and verified. Launch Umamusume through DMM to load the activity bridge.")));
            page.Controls.Add(SettingsUi.Text("After uninstall, use this same action for a fresh install. Existing Hachimi data is kept when profiles are prepared."));
            uninstall = SettingsUi.Button("Uninstall UmaPresence integration…", () => Confirm("Remove the UmaPresence bridge and restore the previous game loader?", () => IntegrationProfiles.UninstallCurrent(settings)));
            page.Controls.Add(uninstall);
            if (ReleasePolicy.PublicRelease) {
                page.Controls.Add(SettingsUi.Button("Full uninstall…", () => {
                    if (checking) { this.saved("Wait for the current update check or install to finish, then try again."); return; }
                    if (!WelcomeDialog.ConfirmUninstall(this)) return;
                    bool companionOnly = settings.RequiresFolderConfirmation;
                    if (companionOnly && MessageBox.Show(this, "The game folder is unknown. Remove only the companion and leave any game integration installed? To remove the integration too, confirm the game folder first.", "Remove companion only?", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                    try {
                        Program.SchedulePortableRemoval(settings, companionOnly);
                        Application.Exit();
                    } catch (Exception error) { MessageBox.Show(this, error.Message, "Full uninstall", MessageBoxButtons.OK, MessageBoxIcon.Error); }
                }));
            }
            var advanced = new DesktopStack(true) { Visible = false, Tag = "hidden" };
            page.Controls.Add(SettingsUi.Button("Advanced recovery…", () => { advanced.Visible = !advanced.Visible; advanced.Tag = advanced.Visible ? null : "hidden"; advanced.Parent.PerformLayout(); })); page.Controls.Add(advanced);
            advanced.Controls.Add(SettingsUi.Text("Recovery and removal change loader files. Close the game normally first."));
            advanced.Controls.Add(SettingsUi.Button("Recover interrupted switch", () => Run(() => IntegrationProfiles.Recover(settings), "Recovery finished. Check the configured mode below before launching.")));
            stockDmm = SettingsUi.Button("Return to stock DMM", () => Confirm("Start the game with its original loader? Hachimi will be inactive on the next launch. Its saved files will be kept.", () => {
                if (IntegrationProfiles.State(settings).Configured == "Standalone") IntegrationProfiles.ReturnToStockDmm(settings);
                else IntegrationProfiles.RestoreOriginal(settings);
                checkResult = null;
                updateOffer = null;
            }));
            advanced.Controls.Add(stockDmm);
            advanced.Controls.Add(details);
            refresh.Tick += (s, e) => { if (Visible && FindForm() != null && FindForm().Visible) RefreshStatus(); }; refresh.Start(); RefreshStatus();
            VisibleChanged += (s, e) => refresh.Enabled = Visible;
        }
        private void SaveFolder(string folder)
        {
            if (!GameInstallationDiscovery.Valid(folder)) throw new InvalidOperationException("Choose a valid JP DMM game folder.");
            string old = settings.GameDirectory;
            bool confirm = settings.RequiresFolderConfirmation;
            try
            {
                settings.GameDirectory = folder;
                settings.RequiresFolderConfirmation = false;
                settings.Save();
            }
            catch
            {
                settings.GameDirectory = old;
                settings.RequiresFolderConfirmation = confirm;
                throw;
            }
            checkResult = updateError = null;
            updateOffer = null;
        }
        private async Task CheckCompatibility()
        {
            if (checking) return;
            checking = true;
            updateOffer = null;
            updateError = null;
            checkResult = "Checking this game build…";
            RefreshStatus();
            try {
                var result = await StandaloneUpdates.CheckAsync(settings, pid());
                if (IsDisposed) return;
                updateOffer = result.Update;
                checkResult = result.Message;
            } catch (Exception ex) {
                updateError = ex.Message;
                checkResult = "The update check didn’t finish. Try Check compatibility again. See Advanced recovery for details.";
            } finally {
                checking = false;
                if (!IsDisposed) RefreshStatus();
            }
        }
        private async Task InstallUpdate()
        {
            if (checking || updateOffer == null) return;
            var offer = updateOffer;
            checking = true;
            updateError = null;
            checkResult = "Checking and installing the update…";
            RefreshStatus();
            try {
                await StandaloneUpdates.InstallAsync(settings, offer);
                if (IsDisposed) return;
                updateOffer = null;
                checkResult = null;
                changed();
            } catch (Exception ex) {
                updateOffer = null;
                updateError = ex.Message;
                checkResult = "The update didn’t finish. Close Umamusume and click Check compatibility to try again. See Advanced recovery for details.";
            } finally {
                checking = false;
                if (!IsDisposed) RefreshStatus();
            }
        }
        private void Confirm(string question, Action action) { if (MessageBox.Show(this, question, "Game integration", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK) Run(action, "Operation completed. Check the configured integration before launching the game."); }
        private void RefreshStatus()
        {
            int gamePid = pid();
            string text = IntegrationProfiles.Status(settings, gamePid);
            if (updateError != null) text += "\r\nLast operation: " + updateError;
            if (details.Text != text) details.Text = text;
            uninstall.Enabled = IntegrationProfiles.CanUninstallCurrent(settings);
            checkCompatibility.Enabled = !checking;
            installUpdate.Enabled = !checking && gamePid == 0;
            ShowAction(installUpdate, updateOffer != null);
            ShowAction(restoreUpdate, !checking && gamePid == 0 && StandaloneUpdates.CanRestore(settings));
            IntegrationState operation;
            try { operation = IntegrationProfiles.State(settings); }
            catch { operation = null; }
            stockDmm.Enabled = !checking && gamePid == 0 && operation?.Step == "Idle"
                && (operation.Configured == "Standalone" || operation.Configured == "Hachimi");
            var runtime = StandaloneRuntime.Read(gamePid);
            string summary = checkResult;
            if (string.IsNullOrEmpty(summary)) summary = StandaloneUpdates.Status(settings, gamePid);
            helpTopic = DesktopHelp.UpdateTopic;
            if (operation?.Step == "Removing") {
                summary = "Close Umamusume and click Full uninstall again. UmaPresence will finish removing its files.";
                helpTopic = DesktopHelp.RemovalTopic;
            } else if (operation == null) {
                summary = "UmaPresence can’t read the saved setup. See Advanced recovery for details.";
                helpTopic = DesktopHelp.RemovalTopic;
            } else if (updateOffer != null) {
                summary = "Standalone update " + updateOffer.Version + " is ready. Close Umamusume, click Install Standalone update, then launch through DMM. Tested: " + updateOffer.Features;
            } else if (!checking && runtime?.RequiresBasicPresence == true) {
                summary = "The game can still run. Discord can show Playing Umamusume. Click Check compatibility for an update. You can still uninstall.";
            } else if (string.IsNullOrEmpty(summary)) {
                if (runtime?.Ready == true)
                    summary = "Standalone is connected. Open Concert to see which controls are available.";
                else if (gamePid > 0 && (operation.Configured == "Hachimi" || settings.GameIntegrationMode == "Hachimi")) {
                    summary = IntegrationProfiles.HasHachimiSession(settings, gamePid)
                        ? "Hachimi is connected. You can keep using UmaPresence."
                        : "The game is running. If the bridge doesn’t connect, open Game integration and click Check compatibility.";
                } else summary = "Check your game folder, then choose Hachimi or Standalone. Close the game before changing modes.";
            }
            if (status.Text != summary) status.Text = summary;
        }
        private static void ShowAction(Button button, bool visible)
        {
            string tag = visible ? null : "hidden";
            if (Equals(button.Tag, tag)) return;
            button.Tag = tag;
            button.Visible = visible;
            button.Parent?.PerformLayout();
            button.Parent?.Parent?.PerformLayout();
        }
        protected override void Dispose(bool disposing) { if (disposing) refresh.Dispose(); base.Dispose(disposing); }
    }
    internal static class IntegrationStatusLines
    {
        internal static System.Collections.Generic.IEnumerable<string> WhereLine(this string[] lines)
        {
            foreach (string line in lines) if (line.StartsWith("Starting profile:") || line.StartsWith("Requested:") || line.StartsWith("Configured:") || line.StartsWith("Loaded:") || line.StartsWith("Requested mode is pending")) yield return line;
        }
    }
}
