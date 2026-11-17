using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace UmapyonPresence
{
    internal sealed class WelcomeDialog : Form
    {
        internal const string WelcomeSheet = "local-welcome-frames.png";
        internal const string GoodbyeSheet = "local-goodbye-frames.png";
        internal const string LocalArtworkMarker = "LOCAL-TEST-ONLY.txt";

        internal static bool ShowWelcome(IWin32Window owner, string gameFolder)
        {
            using (var dialog = new WelcomeDialog(false, GameInstallationDiscovery.Valid(gameFolder) ? gameFolder : null))
                return dialog.ShowDialog(owner) == DialogResult.OK;
        }

        internal static bool ConfirmUninstall(IWin32Window owner)
        {
            using (var dialog = new WelcomeDialog(true, null))
                return dialog.ShowDialog(owner) == DialogResult.OK;
        }

        private readonly Label heading, intro, detail, note;
        private readonly Button cancel, proceed;
        private readonly DialogArtwork artwork;
        private readonly Panel footer;

        internal WelcomeDialog(bool uninstall, string gameFolder, string assetFolder = null, bool? animationsEnabled = null)
        {
            Text = uninstall ? "Full uninstall" : "Welcome to UmaPresence";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            ShowInTaskbar = false;
            MinimizeBox = MaximizeBox = false;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = DesktopDesign.Surface;
            Font = DesktopDesign.Body();
            heading = Copy(uninstall ? "Uninstall UmaPresence?" : "Connect your game");
            heading.Font = DesktopDesign.Heading(15);
            intro = Copy(uninstall
                ? "This removes UmaPresence, its local settings, caches, and game integration. If a game loader was replaced, it will be restored."
                : "Show what you’re doing in Umamusume on Discord. Start by setting up the connection to your game.");
            detail = Copy(uninstall ? "Close Umamusume before uninstalling." : gameFolder == null
                ? "Game folder needed\r\nUse Browse… in Game integration to find your JP DMM install."
                : "Game folder detected\r\n" + gameFolder);
            detail.BackColor = DesktopDesign.Background;
            detail.Padding = new Padding(DesktopDesign.Px(this, 10));
            note = Copy(uninstall ? "We’ll miss you at the track!" : "");
            note.ForeColor = DesktopDesign.Muted;
            note.Visible = uninstall;
            artwork = new DialogArtwork(uninstall ? GoodbyeSheet : WelcomeSheet, assetFolder, animationsEnabled);
            cancel = SettingsUi.Button(uninstall ? "Cancel" : "Skip for now", () => { DialogResult = DialogResult.Cancel; });
            proceed = SettingsUi.Button(uninstall ? "Uninstall" : "Set up game integration", () => { DialogResult = DialogResult.OK; });
            cancel.DialogResult = DialogResult.Cancel;
            proceed.DialogResult = DialogResult.OK;
            ((DesktopButton)(uninstall ? cancel : proceed)).Primary = true;
            footer = new Panel { BackColor = DesktopDesign.Background };
            footer.Controls.AddRange(new Control[] { proceed, cancel });
            Controls.AddRange(new Control[] { heading, intro, detail, note, artwork, footer });
            CancelButton = cancel;
            AcceptButton = uninstall ? cancel : proceed;
            Shown += (sender, args) => {
                ActiveControl = uninstall ? cancel : proceed;
                artwork.Play();
            };
        }

        private static Label Copy(string text)
        {
            return new Label { Text = text, UseMnemonic = false, ForeColor = DesktopDesign.Ink };
        }

        protected override void OnLoad(EventArgs e)
        {
            Rectangle available = Screen.FromControl(this).WorkingArea;
            FitToScreen(available.Width, available.Height - (Height - ClientSize.Height));
            base.OnLoad(e);
        }

        internal void FitToScreen(int availableWidth, int availableHeight = int.MaxValue)
        {
            int Px(int value) => DesktopDesign.Px(this, value);
            int width = Math.Min(Px(780), availableWidth - Px(24));
            int pad = Px(28), gutter = Px(26), artWidth = Px(230);
            bool sideBySide = width >= Px(620);
            int copyWidth = width - 2 * pad - (sideBySide ? artWidth + gutter : 0);
            int y = pad;
            foreach (Label label in new[] { heading, intro, detail, note }) {
                if (label.Text.Length == 0) continue;
                int height = MeasureCopy(label, copyWidth);
                label.SetBounds(pad, y, copyWidth, height);
                y += height + Px(12);
            }
            int artHeight = Px(sideBySide ? 300 : 148);
            int artTop = sideBySide ? pad : y;
            artwork.SetBounds(sideBySide ? pad + copyWidth + gutter : pad, artTop, sideBySide ? artWidth : copyWidth, artHeight);
            int footerTop = Math.Max(y - Px(12), artwork.Bottom) + pad;
            Size proceedSize = proceed.GetPreferredSize(Size.Empty), cancelSize = cancel.GetPreferredSize(Size.Empty);
            int buttonHeight = Math.Max(proceedSize.Height, cancelSize.Height);
            bool stackButtons = proceedSize.Width + cancelSize.Width + Px(8) > width - 2 * pad;
            cancel.SetBounds(width - pad - cancelSize.Width, Px(12) + (stackButtons ? buttonHeight + Px(8) : 0), cancelSize.Width, buttonHeight);
            proceed.SetBounds(stackButtons ? width - pad - proceedSize.Width : cancel.Left - Px(8) - proceedSize.Width,
                Px(12), proceedSize.Width, buttonHeight);
            footer.SetBounds(0, footerTop, width, cancel.Bottom + Px(12));
            int clientHeight = Math.Min(footer.Bottom, availableHeight - Px(16));
            AutoScroll = footer.Bottom > clientHeight;
            AutoScrollMinSize = AutoScroll ? new Size(0, footer.Bottom) : Size.Empty;
            ClientSize = new Size(width + (AutoScroll ? SystemInformation.VerticalScrollBarWidth : 0), clientHeight);
        }

        internal static int MeasureCopy(Label label, int width)
        {
            return TextRenderer.MeasureText(label.Text, label.Font, new Size(width - label.Padding.Horizontal, int.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl).Height + label.Padding.Vertical + 2;
        }

        private sealed class DialogArtwork : Control
        {
            [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
            private static extern bool SystemParametersInfo(uint action, uint parameter, out int value, uint flags);

            private readonly Bitmap artwork;
            private readonly bool animated;
            private readonly bool canPlay;
            private readonly Timer timer = new Timer { Interval = 20 };
            private readonly Stopwatch clock = new Stopwatch();
            private const int MotionDuration = 2400, LoopDuration = 2750;

            internal DialogArtwork(string sheet, string folder, bool? animationsEnabled)
            {
                DoubleBuffered = true;
                folder = folder ?? AppDomain.CurrentDomain.BaseDirectory;
                if (File.Exists(Path.Combine(folder, LocalArtworkMarker)))
                    artwork = LoadSheet(Path.Combine(folder, sheet));
                animated = artwork != null;
                if (!animated) {
                    using (var stream = typeof(WelcomeDialog).Assembly.GetManifestResourceStream("umapyon.png"))
                    using (var image = Image.FromStream(stream)) artwork = new Bitmap(image);
                } else {
                    canPlay = animationsEnabled ?? (SystemParametersInfo(0x1042, 0, out int enabled, 0) && enabled != 0);
                    timer.Tick += (sender, args) => Invalidate();
                }
            }

            internal void Play()
            {
                if (!canPlay) return;
                clock.Start();
                timer.Start();
                Invalidate();
            }

            private static Bitmap LoadSheet(string path)
            {
                try {
                    using (var image = Image.FromFile(path)) {
                        if (image.Width % 4 != 0 || image.Height % 2 != 0 || image.Width > 4096 || image.Height > 4096) return null;
                        return new Bitmap(image);
                    }
                } catch (IOException) { return null; }
                catch (UnauthorizedAccessException) { return null; }
                catch (ArgumentException) { return null; }
                catch (OutOfMemoryException) { return null; }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                int w = animated ? artwork.Width / 4 : artwork.Width;
                int h = animated ? artwork.Height / 2 : artwork.Height;
                int pad = DesktopDesign.Px(this, 14);
                float scale = Math.Min((float)(Width - 2 * pad) / w, (float)(Height - 2 * pad) / h) * (animated ? 1f : 0.52f);
                if (scale <= 0) return;
                double t = canPlay && clock.IsRunning ? Math.Min(1, (clock.ElapsedMilliseconds % LoopDuration) / (double)MotionDuration) : 1;
                int frame = animated ? Math.Min(7, (int)(t * 8)) : 0;
                var destination = new RectangleF((Width - w * scale) / 2, (Height - h * scale) / 2, w * scale, h * scale);
                e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                e.Graphics.DrawImage(artwork, destination, new RectangleF(frame % 4 * w, frame / 4 * h, w, h), GraphicsUnit.Pixel);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) { timer.Dispose(); artwork.Dispose(); }
                base.Dispose(disposing);
            }
        }
    }
}
