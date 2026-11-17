using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using System.Windows.Forms.Layout;

namespace UmapyonPresence
{
    internal static class DesktopDesign
    {
        internal static readonly Color Background = Color.FromArgb(245, 246, 250), Surface = Color.White,
            Sidebar = Color.FromArgb(27, 30, 43), Accent = Color.FromArgb(106, 79, 218), Border = Color.FromArgb(228, 230, 238),
            NavigationSelected = Color.FromArgb(63, 52, 99), NavigationMarker = Color.FromArgb(180, 158, 255),
            Ink = Color.FromArgb(35, 39, 55), Muted = Color.FromArgb(92, 101, 119);
        private static readonly float scale = ReadScale();
        internal static readonly string TextFamily = FindTextFamily();
        private static string FindTextFamily()
        {
            using (var fonts = new System.Drawing.Text.InstalledFontCollection()) {
                var names=new HashSet<string>(fonts.Families.Select(font=>font.Name));
                return new[]{"Noto Sans JP","Yu Gothic UI","Segoe UI"}.First(names.Contains);
            }
        }
        internal static Font Body(float size=9.5f) { return new Font(TextFamily,size,FontStyle.Regular); }
        internal static Font Heading(float size)
        {
            using (var family = new FontFamily(TextFamily))
                return new Font(family, size, family.IsStyleAvailable(FontStyle.Bold) ? FontStyle.Bold : FontStyle.Regular);
        }
        private static float ReadScale() { using (var graphics = Graphics.FromHwnd(IntPtr.Zero)) return graphics.DpiX / 96f; }
        internal static int Px(Control control, float value) { return (int)Math.Round(value * scale); }
        internal static Color ParentColor(Control control) { for (var parent = control.Parent; parent != null; parent = parent.Parent) if (parent.BackColor.A == 255) return parent.BackColor; return Background; }
        internal static GraphicsPath Round(RectangleF bounds, float radius)
        {
            float d = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height)); var path = new GraphicsPath();
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90); path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90); path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90); path.CloseFigure(); return path;
        }
    }

    // Each column owns its layout pass. No shared resize guard or persistent
    // label MaximumSize: width is derived from the current viewport every pass.
    internal class DesktopStack : FlowLayoutPanel
    {
        private readonly ColumnEngine engine = new ColumnEngine();
        internal bool Card, Organized;
        internal DesktopStack(bool card = false)
        {
            Card = card; AutoScroll = !card; BackColor = card ? Color.White : DesktopDesign.Background;
            Padding = Padding.Empty; Margin = Padding.Empty;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
        }
        public override LayoutEngine LayoutEngine { get { return engine; } }
        internal int Measure(int width, bool position)
        {
            int pad = DesktopDesign.Px(this, Card ? 20 : 26), y = pad, inner = Math.Max(80, width - pad * 2);
            foreach (Control child in Controls) {
                if (!child.Visible && IsHandleCreated && FindForm() != null && FindForm().Visible) continue;
                // A hidden advanced section must stay out of the preferred size.
                if (child.Tag as string == "hidden") continue;
                int available = Math.Max(60, inner), height, childWidth = available;
                var label = child as Label; var stack = child as DesktopStack;
                if (label != null) {
                    height = TextRenderer.MeasureText(label.Text.Length == 0 ? " " : label.Text, label.Font, new Size(available, 100000), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl).Height + 2;
                } else if (stack != null) height = stack.Measure(available, false);
                else if (child is DesktopToggle) height = ((DesktopToggle)child).MeasureHeight(available);
                else if (child is DesktopStatusGrid || child is DesktopHelp) height = child.GetPreferredSize(new Size(available, 0)).Height;
                else if (child is FlowLayoutPanel) {
                    var row = (FlowLayoutPanel)child; row.MaximumSize = new Size(available, 0);
                    height = row.GetPreferredSize(new Size(available, 0)).Height;
                } else {
                    Size preferred = child.GetPreferredSize(new Size(available, 0));
                    height = child is TextBox && ((TextBox)child).Multiline ? child.Height : Math.Max(child.MinimumSize.Height, preferred.Height);
                    if (!(child.Tag as string == "wide")) childWidth = Math.Min(available, child is Button ? preferred.Width : DesktopDesign.Px(this, child is NumericUpDown ? 90 : 300));
                }
                if (position) { child.AutoSize = false; child.SetBounds(pad + (AutoScroll ? AutoScrollPosition.X : 0), y + (AutoScroll ? AutoScrollPosition.Y : 0), childWidth, height); if (stack != null) stack.PerformLayout(); }
                int gap = child.Name == "PageDescription" ? 22 : child.Name == "PageHeading" ? 6 : stack != null ? 16 : child is CheckBox ? 4 : 12;
                y += height + DesktopDesign.Px(this, gap);
            }
            return y + pad;
        }
        public override Size GetPreferredSize(Size proposedSize) { return new Size(proposedSize.Width > 0 ? proposedSize.Width : Width, Measure(proposedSize.Width > 0 ? proposedSize.Width : Width, false)); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); if (!Card || Width < 4 || Height < 4) return;
            e.Graphics.Clear(DesktopDesign.ParentColor(this));
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = DesktopDesign.Round(new RectangleF(.5f, .5f, Width - 1, Height - 1), DesktopDesign.Px(this, 10)))
            { using (var brush = new SolidBrush(Color.White)) e.Graphics.FillPath(brush,path); using (var pen = new Pen(DesktopDesign.Border)) e.Graphics.DrawPath(pen, path); }
        }
        internal void Organize()
        {
            if (Organized || Card) return; Organized = true;
            var original = Controls.Cast<Control>().ToArray(); DesktopStack card = null;
            SuspendLayout(); Controls.Clear();
            foreach (Control child in original) {
                if (child.Tag as string == "section") { card = new DesktopStack(true); Controls.Add(card); child.Margin = new Padding(0, 0, 0, 14); }
                if (card == null) Controls.Add(child); else card.Controls.Add(child);
            }
            ResumeLayout(true);
        }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); PerformLayout(); }
        protected override void OnScroll(ScrollEventArgs e) { base.OnScroll(e); Invalidate(true); }
        private sealed class ColumnEngine : LayoutEngine
        {
            private bool busy;
            private int preferredHeight = -1;
            public override bool Layout(object container, LayoutEventArgs args)
            {
                if (busy) return false; busy = true;
                try {
                    var stack = (DesktopStack)container;
                    int width = Math.Max(100, stack.ClientSize.Width - (stack.AutoScroll ? SystemInformation.VerticalScrollBarWidth : 0));
                    int height = stack.Measure(width, true);
                    bool preferredChanged = height != preferredHeight; preferredHeight = height;
                    if (stack.AutoScroll) stack.AutoScrollMinSize = new Size(0, height);
                    else if (preferredChanged) return true;
                } finally { busy = false; }
                return false;
            }
        }
    }

    internal sealed class DesktopRow : FlowLayoutPanel
    {
        private readonly RowEngine engine = new RowEngine();
        internal DesktopRow() { Margin = Padding.Empty; Padding = Padding.Empty; BackColor = Color.Transparent; }
        public override LayoutEngine LayoutEngine { get { return engine; } }
        internal int Measure(int width, bool position)
        {
            int x = 0, y = 0, height = 0, gap = DesktopDesign.Px(this, 8);
            foreach (Control child in Controls) {
                Size size = child.GetPreferredSize(new Size(width,0)); size.Width = Math.Min(width, size.Width);
                if (x > 0 && x + size.Width > width) { x = 0; y += height + gap; height = 0; }
                if (position) child.SetBounds(x,y,size.Width,size.Height);
                x += size.Width + gap; height = Math.Max(height,size.Height);
            }
            return y + height;
        }
        public override Size GetPreferredSize(Size proposedSize) { return new Size(proposedSize.Width, Measure(Math.Max(80,proposedSize.Width),false)); }
        private sealed class RowEngine : LayoutEngine { private bool busy; public override bool Layout(object owner, LayoutEventArgs args) { if(busy)return false; busy=true; try { var row=(DesktopRow)owner;row.Measure(Math.Max(80,row.ClientSize.Width),true); } finally {busy=false;}return false; } }
    }

    internal sealed class DesktopButton : Button
    {
        internal bool Primary; private bool hover;
        internal DesktopButton() { Font=DesktopDesign.Body(); FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; UseVisualStyleBackColor = false; SetStyle(ControlStyles.OptimizedDoubleBuffer, true); }
        public override Size GetPreferredSize(Size proposedSize)
        { var text = TextRenderer.MeasureText(Text, Font); return new Size(Math.Max(DesktopDesign.Px(this, 100), text.Width + DesktopDesign.Px(this, 32)), Math.Max(DesktopDesign.Px(this, 38), text.Height + DesktopDesign.Px(this, 16))); }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(DesktopDesign.ParentColor(this)); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rectangle = new RectangleF(.5f, .5f, Width - 1, Height - 1);
            using (var path = DesktopDesign.Round(rectangle, DesktopDesign.Px(this, 7))) {
                using (var brush = new SolidBrush(Primary ? (hover ? Color.FromArgb(88, 63, 195) : DesktopDesign.Accent) : (hover ? Color.FromArgb(239, 237, 251) : Color.White))) e.Graphics.FillPath(brush, path);
                if (!Primary) using (var pen = new Pen(DesktopDesign.Border)) e.Graphics.DrawPath(pen, path);
            }
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, !Enabled ? SystemColors.GrayText : Primary ? Color.White : DesktopDesign.Ink, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -5, -5));
        }
    }
    internal sealed class DesktopToggle : CheckBox
    {
        internal DesktopToggle() { Font=DesktopDesign.Body(); AutoSize = false; SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true); }
        internal int MeasureHeight(int width) { return Math.Max(DesktopDesign.Px(this, 40), TextRenderer.MeasureText(Text, Font, new Size(Math.Max(80, width - DesktopDesign.Px(this, 76)), 10000), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height + DesktopDesign.Px(this, 16)); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent.BackColor); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int w = DesktopDesign.Px(this, 40), h = DesktopDesign.Px(this, 23), x = Width - w - 2, y = (Height - h) / 2;
            using (var path = DesktopDesign.Round(new RectangleF(x, y, w, h), h / 2f)) using (var fill = new SolidBrush(Checked ? DesktopDesign.Accent : Color.FromArgb(213, 217, 229))) e.Graphics.FillPath(fill, path);
            using (var brush = new SolidBrush(Color.White)) e.Graphics.FillEllipse(brush, Checked ? x + w - h + 3 : x + 3, y + 3, h - 6, h - 6);
            TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(0, 0, Math.Max(0, x - 16), Height), DesktopDesign.Ink, TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -2, -2));
        }
    }
    internal sealed class DesktopNavigation : Button
    {
        private bool selected;
        internal bool Selected { get { return selected; } set { if (selected == value) return; selected = value; Invalidate(); } }
        internal int Symbol; private bool hover;
        internal DesktopNavigation() { Font=DesktopDesign.Body(10); FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; UseMnemonic = false; SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(DesktopDesign.Sidebar); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            if (Selected || hover) using (var path = DesktopDesign.Round(new RectangleF(.5f,.5f,Width-1,Height-1), DesktopDesign.Px(this,10))) {
                using (var brush = new SolidBrush(Selected ? DesktopDesign.NavigationSelected : Color.FromArgb(39,43,60))) e.Graphics.FillPath(brush,path);
            }
            if(Selected)using(var brush=new SolidBrush(DesktopDesign.NavigationMarker))
                e.Graphics.FillRectangle(brush, DesktopDesign.Px(this,3), DesktopDesign.Px(this,10), DesktopDesign.Px(this,4), Height-DesktopDesign.Px(this,20));
            Color ink = Selected ? Color.White : Color.FromArgb(190,197,216);
            int unit = DesktopDesign.Px(this, 20), left = DesktopDesign.Px(this, 15), top = (Height - unit) / 2;
            using (var pen = new Pen(ink, Math.Max(1.4f, DeviceDpi / 70f))) {
                if (Symbol == 0) { e.Graphics.DrawArc(pen,left,top,unit,unit,0,180);e.Graphics.DrawLine(pen,left,top+unit/2,left,top);e.Graphics.DrawLine(pen,left+unit,top+unit/2,left+unit,top);e.Graphics.DrawLine(pen,left,top,left+unit/4,top);e.Graphics.DrawLine(pen,left+unit*3/4,top,left+unit,top); }
                else if (Symbol == 1) { e.Graphics.DrawEllipse(pen,left+unit/4,top+unit/3,unit/2,unit*2/3);e.Graphics.DrawLines(pen,new[]{new Point(left+unit/4,top+unit/2),new Point(left+unit/5,top),new Point(left+unit/2,top+unit/3),new Point(left+unit*4/5,top),new Point(left+unit*3/4,top+unit/2)}); }
                else if (Symbol == 2) { e.Graphics.DrawLine(pen, left + unit / 3, top + unit, left + unit / 3, top); e.Graphics.DrawLine(pen, left + unit / 3, top, left + unit, top + unit / 4); e.Graphics.DrawEllipse(pen, left, top + unit * 2 / 3, unit / 3, unit / 3); }
                else if (Symbol == 3) { e.Graphics.DrawEllipse(pen, left, top, unit, unit); e.Graphics.DrawEllipse(pen, left + unit / 3, top + unit / 3, unit / 3, unit / 3); }
                else if (Symbol == 4) { e.Graphics.DrawRectangle(pen, left, top + unit / 4, unit, unit * 3 / 4); e.Graphics.DrawLine(pen, left, top, left + unit / 2, top); e.Graphics.DrawLine(pen, left, top, left, top + unit / 4); }
                else { e.Graphics.DrawEllipse(pen, left, top, unit, unit); TextRenderer.DrawText(e.Graphics, "?", Font, new Rectangle(left, top - 1, unit, unit + 2), ink, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter); }
            }
            TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(DesktopDesign.Px(this, 48), 0, Width - DesktopDesign.Px(this, 54), Height), ink, TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -4, -4));
        }
    }
    internal sealed class DesktopStatusGrid : Control
    {
        private readonly string[] names = { "GAME", "DISCORD", "ACTIVITY" }; private string[] values = { "Not running", "Waiting", "Waiting for game" };
        internal DesktopStatusGrid() { Tag = "wide"; DoubleBuffered = true; Margin = new Padding(0, 4, 0, 20); }
        internal void UpdateValues(string[] next) { if (!values.SequenceEqual(next)) { values = next; Invalidate(); } }
        public override Size GetPreferredSize(Size proposedSize) { return new Size(proposedSize.Width, DesktopDesign.Px(this, 126)); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(DesktopDesign.Background); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; int gap = DesktopDesign.Px(this, 12), w = (Width - 2 * gap) / 3;
            using (var small = DesktopDesign.Heading(8)) using (var value = DesktopDesign.Heading(11))
            for (int i = 0; i < 3; i++) {
                var box = new Rectangle(i * (w + gap), 0, w - 1, Height - 1);
                using (var path = DesktopDesign.Round(box, DesktopDesign.Px(this, 10))) { using (var brush = new SolidBrush(Color.White)) e.Graphics.FillPath(brush, path); using (var pen = new Pen(DesktopDesign.Border)) e.Graphics.DrawPath(pen, path); }
                int p = DesktopDesign.Px(this, 16);
                TextRenderer.DrawText(e.Graphics, names[i], small, new Rectangle(box.X + p, p, w - 2 * p, DesktopDesign.Px(this, 20)), DesktopDesign.Muted, TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(e.Graphics, values[i], value, new Rectangle(box.X + p, DesktopDesign.Px(this, 49), w - 2 * p, Height - DesktopDesign.Px(this, 55)), DesktopDesign.Ink, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            }
        }
    }

    internal sealed class DesktopHelp : UserControl
    {
        private readonly ListBox topics; private readonly ComboBox compact; private readonly DesktopStack guide;
        private readonly int topicRailWidth;
        private readonly Font guideHeading = DesktopDesign.Heading(11), stepHeading = DesktopDesign.Heading(9.5f);
        private readonly Label[] guideLabels = new Label[9];
        private int selectedTopic = -1;
        private bool changingTopic;
        internal const int RemovalTopic = 5, UpdateTopic = 6;
        internal int SelectedTopic { get { return selectedTopic; } set { SelectTopic(value); } }
        internal Label[] GuideLabels { get { return (Label[])guideLabels.Clone(); } }
        internal DesktopHelp()
        {
            AutoScaleMode = AutoScaleMode.None; Tag = "wide"; BackColor = DesktopDesign.Background;
            topics = new ListBox { BorderStyle = BorderStyle.None, BackColor = DesktopDesign.Background, IntegralHeight = false, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 42, Font = DesktopDesign.Body() };
            topics.Items.AddRange(new[] { "Install UmaPresence", "Switch integration", "Concert controls", "Data & artwork", "Troubleshooting", "Uninstall or reinstall", "After an update" });
            topicRailWidth = Math.Max(DesktopDesign.Px(this, 170), topics.Items.Cast<string>()
                .Max(topic => TextRenderer.MeasureText(topic, topics.Font).Width) + DesktopDesign.Px(this, 26));
            compact = new ComboBox { Font=DesktopDesign.Body(), DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, Visible = false };
            compact.Items.AddRange(topics.Items.Cast<object>().ToArray());
            compact.SelectedIndexChanged += (s,e) => { if (!changingTopic) SelectTopic(compact.SelectedIndex); };
            topics.DrawItem += (s, e) => { if (e.Index < 0) return; using (var brush = new SolidBrush((e.State & DrawItemState.Selected) != 0 ? Color.FromArgb(235, 230, 251) : DesktopDesign.Background)) e.Graphics.FillRectangle(brush, e.Bounds); TextRenderer.DrawText(e.Graphics, topics.Items[e.Index].ToString(), e.Font, Rectangle.Inflate(e.Bounds, -10, 0), DesktopDesign.Ink, TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis); };
            guide = new DesktopStack(true);
            for (int i = 0; i < guideLabels.Length; i++) {
                var label = SettingsUi.Text(""); label.Name = "HelpTopicText" + i;
                if (i == 0) { label.Font = guideHeading; label.ForeColor = DesktopDesign.Ink; }
                else if (i % 2 == 1) { label.Font = stepHeading; label.ForeColor = DesktopDesign.Ink; }
                guideLabels[i] = label; guide.Controls.Add(label);
            }
            Controls.Add(guide); Controls.Add(topics); Controls.Add(compact);
            topics.SelectedIndexChanged += (s,e) => { if (!changingTopic) SelectTopic(topics.SelectedIndex); };
            SelectTopic(0);
        }
        private void SelectTopic(int index)
        {
            if (changingTopic || index < 0 || index >= topics.Items.Count || selectedTopic == index) return;
            string[][] entries = {
                new[] { "Install UmaPresence", "Extract the ZIP|Extract all files into a new writable folder, then run UmaPresence.exe. Open Game integration and review the detected JP DMM folder. Choose Browse… if needed.", "Standalone|Close Umamusume. Use Download official helper or Select downloaded helper… to verify Cellar. Then click Install Standalone.", "Hachimi Edge|With Hachimi Edge already installed and Umamusume closed, select Hachimi and click Apply switch. Turn off Hachimi’s built-in Discord activity so UmaPresence can publish.", "Check the connection|Launch Umamusume through DMM and open Discord with activity sharing enabled. In UmaPresence, check Overview for Bridge connected and the current activity." },
                new[] { "Switch integration", "Close the game|Wait for Umamusume to quit before applying a switch. Leave UmaPresence open.", "Choose your next mode|Under Game integration, choose Hachimi or Standalone and click Apply switch. Use Install Standalone to set up Standalone when needed.", "Launch through DMM|After the success message, relaunch the game. Check the configured mode and bridge status in Overview.", "Recover an interrupted switch|In Game integration, open Advanced recovery and choose Recover interrupted switch. If a loader is reported as unverified, keep the error message for troubleshooting." },
                new[] { "Control your concert", "Seek bar|In Standalone mode, choose whether the seek bar appears while paused, always, or never.", "Windows controls|Enable Windows media controls from Concert. Discord playback progress is separate.", "Hachimi controls|In Hachimi mode, use Hachimi’s own concert controls.", "Feature availability|The Concert page shows which controls the loaded integration supports." },
                new[] { "Keep your data current", "After a game update|Click Refresh game data to reload names and other game data.", "After a translation update|Click Reload translations to refresh labels. UmaPresence uses its built-in labels when it has no working translation.", "Add a song cover|Play or pause the song, generate its cover, upload the PNG through Discord assets, then register its exact asset name.", "Bridge updates|Use Game integration → Check compatibility for updates that affect how UmaPresence connects to the game." },
                new[] { "Find the next useful check", "No Discord activity?|Check Overview, Discord activity sharing and whether presence is paused.", "Bridge not connected?|Go to Game integration and click Check compatibility. Follow the message there before changing modes.", "No seek bar?|Check the Concert visibility setting and whether the loaded integration supports seeking.", "Report a problem|Use the diagnostic tools below. Remove personal paths, trainer or account details, and roster information before sharing a report." },
                new[] { "Uninstall or reinstall", "Close Umamusume first|Leave UmaPresence open and go to Game integration.", "Full uninstall|Removes UmaPresence, its settings and its game integration. An active Hachimi loader and language pack stay in place. If removal was interrupted, click Full uninstall again.", "Return to stock DMM|Changes how the game starts. Hachimi is inactive on the next launch, and its saved files are kept. UmaPresence stays installed.", "Reinstall from the ZIP|Keep the original ZIP. Extract it into a new folder and run UmaPresence.exe." },
                new[] { "After an update", "Hachimi updated|If Discord activity still works, keep using Hachimi. If the bridge hasn’t connected, open Game integration and click Check compatibility.", "DMM updated|Open Game integration and click Check compatibility. If detailed activity is unavailable, Discord can still show Playing Umamusume. Full uninstall remains available.", "A Standalone update is ready|Close the game, click Install Standalone update, and launch through DMM to check that activity comes back.", "Only the language pack changed|Go to Data & artwork and click Reload translations." },
            };
            // Keep native label handles stable. Rebuilding controls while an
            // ancestor scrolls can leave copied old text in the guide surface.
            var page = Parent as DesktopStack;
            changingTopic = true; page?.SuspendLayout(); SuspendLayout(); guide.SuspendLayout();
            try {
                selectedTopic = index; topics.SelectedIndex = index; compact.SelectedIndex = index;
                var entry = entries[index]; guideLabels[0].Text = entry[0];
                for (int i = 1; i < entry.Length; i++) {
                    var pair = entry[i].Split('|'); guideLabels[i * 2 - 1].Text = pair[0]; guideLabels[i * 2].Text = pair[1];
                }
            } finally {
                guide.ResumeLayout(false); ResumeLayout(false); page?.ResumeLayout(false); changingTopic = false;
            }
            page?.PerformLayout(); PerformLayout(); guide.PerformLayout();
            if (page != null) { page.AutoScrollPosition = Point.Empty; page.Invalidate(true); }
            guide.Invalidate(true); Invalidate(true);
        }
        public override Size GetPreferredSize(Size proposedSize)
        {
            int rail = topicRailWidth, gap = DesktopDesign.Px(this, 18);
            if (proposedSize.Width < CompactThreshold(rail, gap)) return new Size(proposedSize.Width, guide.Measure(proposedSize.Width,false) + DesktopDesign.Px(this,48));
            return new Size(proposedSize.Width, Math.Max(topics.Items.Count * DesktopDesign.Px(this, 42), guide.Measure(Math.Max(200, proposedSize.Width - rail - gap), false)));
        }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e); if (topics == null) return; int rail = topicRailWidth, gap = DesktopDesign.Px(this, 18);
            bool narrow = Width < CompactThreshold(rail, gap); compact.Visible = narrow; topics.Visible = !narrow;
            if (narrow) { compact.SetBounds(0,0,Width,compact.PreferredHeight); int top = DesktopDesign.Px(this,48); guide.SetBounds(0,top,Width,Math.Max(50,Height-top)); }
            else { topics.ItemHeight = DesktopDesign.Px(this, 42); topics.SetBounds(0, 0, rail, Height); guide.SetBounds(rail + gap, 0, Math.Max(100, Width - rail - gap), Height); }
            guide.PerformLayout();
        }
        private int CompactThreshold(int rail, int gap) => Math.Max(DesktopDesign.Px(this, 620), rail + gap + DesktopDesign.Px(this, 390));
        protected override void Dispose(bool disposing) { base.Dispose(disposing); if (disposing) { guideHeading.Dispose(); stepHeading.Dispose(); } }
    }
}
