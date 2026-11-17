using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace UmapyonPresence
{
    // Optional, non-activating companion overlay. No injected graphics renderer,
    // per-frame hook, bundled font or audio decoding. Hidden outside Live Theater.
    internal sealed class ConcertSeekOverlay : Form
    {
        private readonly SeekSlider slider = new SeekSlider { Dock = DockStyle.Fill };
        private readonly Label elapsed = TimeLabel(DockStyle.Left, Color.White);
        private readonly Label total = TimeLabel(DockStyle.Right, Color.FromArgb(212, 223, 238));
        private int pid, song;
        private double duration;
        private double seekStart, seekEnd;
        private bool dragging, busy, updating, canSeek;
        private readonly System.Threading.SemaphoreSlim commandGate = new System.Threading.SemaphoreSlim(1, 1);
        private DateTime keyboardSeek;
        internal string Status { get; private set; } = "Hidden";
        internal string LastCommand { get; private set; }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams { get { var p = base.CreateParams; p.ExStyle |= 0x08000000 | 0x00000080; return p; } }
        internal ConcertSeekOverlay()
        {
            Text = "UmaPresence concert controls"; FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            Height = DesktopDesign.Px(this, 32); Width = DesktopDesign.Px(this, 540);
            BackColor = Color.FromArgb(23, 36, 56); Font = DesktopDesign.Body(9);
            Padding = new Padding(DesktopDesign.Px(this, 8), DesktopDesign.Px(this, 5), DesktopDesign.Px(this, 8), DesktopDesign.Px(this, 5));
            elapsed.Width = total.Width = DesktopDesign.Px(this, 48);
            Controls.Add(slider); Controls.Add(elapsed); Controls.Add(total);
            slider.CanStartDrag = () => canSeek && !busy;
            slider.MouseDown += async (s, e) => { if (e.Button != MouseButtons.Left) return; dragging = true; keyboardSeek = DateTime.MinValue; await Execute(4, 0); };
            slider.MouseUp += async (s, e) => { if (e.Button != MouseButtons.Left || !dragging) return; dragging = false; await Execute(5, SeekTarget()); };
            slider.MouseCaptureChanged += async (s, e) => { if (slider.Capture || !dragging) return; dragging = false; await Execute(7, 0); };
            slider.ValueChanged += (s, e) => { if (!updating && canSeek) { ShowTimes(SeekTarget()); if (!dragging) keyboardSeek = DateTime.UtcNow.AddMilliseconds(200); } };
        }
        private static Label TimeLabel(DockStyle side, Color color)
        {
            return new Label { Dock = side, Text = "0:00", TextAlign = ContentAlignment.MiddleCenter, ForeColor = color };
        }
        private void ShowTimes(double position)
        {
            elapsed.Text = ConcertSnapshot.TimeText(position);
            total.Text = ConcertSnapshot.TimeText(duration);
        }
        private double SeekTarget() { return Math.Max(seekStart, Math.Min(seekEnd, duration * slider.Value / SeekSlider.Maximum)); }
        private async Task Execute(int command, double position)
        {
            int expectedPid = pid, expectedSong = song;
            if (expectedPid < 1 || expectedSong < 1) return;
            await commandGate.WaitAsync(); busy = true;
            try { if (command != 7 && (pid != expectedPid || song != expectedSong)) return; int result = await StandaloneRuntime.Send(expectedPid, command, expectedSong, position); LastCommand = result == 0 ? "Control acknowledged" : "Control rejected: " + result; }
            catch (Exception ex) { LastCommand = ex.Message; }
            finally { busy = false; commandGate.Release(); }
        }
        internal static string HiddenReason(string mode, bool foreground, StandaloneRuntimeStatus runtime, ConcertSnapshot concert, bool dragging)
        {
            if (mode == "Off") return "Hidden by Concert settings";
            if (runtime?.OwnershipConflict == true) return "Hidden because another integration owns concert controls";
            if (runtime?.Ready != true || (runtime.Capabilities & RuntimeCapability.Telemetry) == 0) return "Waiting for verified Standalone runtime";
            if (concert == null || !concert.Active || concert.Status == 2 || concert.Duration <= 0) return "Waiting for live concert telemetry";
            if (!foreground) return "Hidden while the game is out of focus";
            if (mode != "Always" && !concert.Paused && !dragging) return "Hidden during playback in paused-only mode";
            return null;
        }

        internal void Observe(Settings settings, GameSession game, StandaloneRuntimeStatus runtime, ConcertSnapshot concert)
        {
            if (pid != game?.ProcessId || song != concert?.MusicId) {
                CancelInteraction();
                LastCommand = null;
            }
            double end = Math.Min(runtime?.SeekEnd ?? 0, concert?.Duration ?? 0);
            bool seekable = runtime?.CanSeek == true && end > runtime.SeekStart;
            if (!seekable) CancelInteraction();
            string hidden = HiddenReason(settings.StandaloneSeekBarMode, game?.IsForeground == true, runtime, concert, dragging);
            if (hidden != null) { HideControls(); Status = hidden; return; }
            Native.Rect client; var origin = new Native.Point();
            if (!Native.GetClientRect(game.Window, out client) || !Native.ClientToScreen(game.Window, ref origin)) {
                HideControls();
                Status = "Waiting for the game window";
                return;
            }
            pid = game.ProcessId; song = concert.MusicId; duration = concert.Duration;
            canSeek = seekable;
            slider.SetSeekable(canSeek);
            seekStart = runtime.SeekStart; seekEnd = end;
            if (canSeek) slider.SetSeekRange(seekStart / duration, seekEnd / duration);
            if (canSeek && !dragging && !busy && keyboardSeek != DateTime.MinValue && DateTime.UtcNow >= keyboardSeek) { keyboardSeek = DateTime.MinValue; var ignored = Execute(3, SeekTarget()); }
            if (!canSeek || (!dragging && !busy && keyboardSeek == DateTime.MinValue)) {
                updating = true;
                slider.Value = (int)(Math.Min(concert.Position, duration) / duration * SeekSlider.Maximum);
                updating = false;
                ShowTimes(concert.Position);
            }
            var bounds = OverlayBounds(new Rectangle(origin.X, origin.Y, client.Right - client.Left, client.Bottom - client.Top),
                Height, DesktopDesign.Px(this, 600), DesktopDesign.Px(this, 12), DesktopDesign.Px(this, 64));
            if (bounds.IsEmpty) { HideControls(); Status = "Waiting for usable game window bounds"; return; }
            Bounds = bounds;
            if (!Visible) { Show(); Native.ShowWindow(Handle, 4); }
            Status = canSeek ? "Visible with seeking" : "Visible with progress only";
        }
        internal static Rectangle OverlayBounds(Rectangle client, int height, int maxWidth, int sideMargin, int bottomOffset)
        {
            int width = Math.Min(maxWidth, client.Width - 2 * sideMargin);
            if (width <= 0 || client.Height < height) return Rectangle.Empty;
            return new Rectangle(client.X + (client.Width - width) / 2,
                Math.Max(client.Top, client.Bottom - height - bottomOffset), width, height);
        }
        private void CancelInteraction()
        {
            if (dragging) { dragging = false; var cancel = Execute(7, 0); }
            slider.Capture = false;
            keyboardSeek = DateTime.MinValue;
        }
        internal void HideControls()
        {
            CancelInteraction();
            if (Visible) Hide();
            canSeek = false;
            pid = song = 0;
            Status = "Hidden";
        }

        private sealed class SeekSlider : Control
        {
            internal const int Maximum = 10000;
            private int value;
            private int seekMin, seekMax = Maximum;
            private bool hovered, seekable;
            internal Func<bool> CanStartDrag;
            internal event EventHandler ValueChanged;
            internal int Value
            {
                get { return value; }
                set
                {
                    int clamped = Math.Max(0, Math.Min(Maximum, value));
                    if (this.value == clamped) return;
                    this.value = clamped;
                    Invalidate();
                    ValueChanged?.Invoke(this, EventArgs.Empty);
                }
            }

            internal SeekSlider()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
                AccessibleName = "Concert progress";
                AccessibleRole = AccessibleRole.ProgressBar;
                TabStop = false;
            }

            internal void SetSeekable(bool value)
            {
                if (seekable == value) return;
                seekable = value;
                Cursor = value ? Cursors.Hand : Cursors.Default;
                TabStop = value;
                AccessibleName = value ? "Seek concert position" : "Concert progress";
                AccessibleRole = value ? AccessibleRole.Slider : AccessibleRole.ProgressBar;
                Invalidate();
            }

            private int Inset { get { return DesktopDesign.Px(this, 8); } }
            private int TrackWidth { get { return Math.Max(1, ClientSize.Width - 2 * Inset); } }
            internal void SetSeekRange(double start, double end)
            {
                seekMin = (int)Math.Ceiling(start * Maximum);
                seekMax = Math.Max(seekMin, (int)Math.Floor(end * Maximum));
            }
            private void MoveTo(int x) { SeekTo((int)Math.Round((x - Inset) * (double)Maximum / TrackWidth)); }
            private void SeekTo(int target) { Value = Math.Max(seekMin, Math.Min(seekMax, target)); }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                int y = Height / 2;
                int thumb = Inset + (int)Math.Round(Value * (double)TrackWidth / Maximum);
                int radius = DesktopDesign.Px(this, hovered || Capture ? 6 : 5);
                using (var track = new Pen(Color.FromArgb(65, 81, 106), DesktopDesign.Px(this, 4)))
                using (var fill = new Pen(Color.FromArgb(40, 127, 184), DesktopDesign.Px(this, 4))) {
                    track.StartCap = track.EndCap = fill.StartCap = fill.EndCap = LineCap.Round;
                    e.Graphics.DrawLine(track, Inset, y, Inset + TrackWidth, y);
                    if (Value > 0) e.Graphics.DrawLine(fill, Inset, y, thumb, y);
                }
                if (seekable) {
                    e.Graphics.FillEllipse(Brushes.White, thumb - radius, y - radius, radius * 2, radius * 2);
                    if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, ClientRectangle);
                }
            }

            protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }
            protected override void OnMouseDown(MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left || CanStartDrag?.Invoke() != true) { Capture = false; return; }
                Capture = true;
                base.OnMouseDown(e);
                MoveTo(e.X);
            }
            protected override void OnMouseMove(MouseEventArgs e) { if (Capture && e.Button == MouseButtons.Left) MoveTo(e.X); base.OnMouseMove(e); }
            protected override void OnMouseUp(MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left && Capture) MoveTo(e.X);
                base.OnMouseUp(e);
                Capture = false;
            }
            protected override bool IsInputKey(Keys keyData)
            {
                var key = keyData & Keys.KeyCode;
                return key == Keys.Left || key == Keys.Right || key == Keys.Home || key == Keys.End || base.IsInputKey(keyData);
            }
            protected override void OnKeyDown(KeyEventArgs e)
            {
                if (!seekable) { base.OnKeyDown(e); return; }
                if (e.KeyCode == Keys.Left) SeekTo(Value - 100);
                else if (e.KeyCode == Keys.Right) SeekTo(Value + 100);
                else if (e.KeyCode == Keys.Home) SeekTo(seekMin);
                else if (e.KeyCode == Keys.End) SeekTo(seekMax);
                else { base.OnKeyDown(e); return; }
                e.Handled = true;
            }
        }
    }
}
