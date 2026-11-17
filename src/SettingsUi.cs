using System;
using System.Drawing;
using System.Windows.Forms;

namespace UmapyonPresence
{
    internal sealed class SettingsActions
    {
        internal Func<string> Overview = () => "Game: Not connected\r\nDiscord: Not connected\r\nActivity: Waiting for game";
        internal Func<string> BridgeStatus = () => "Waiting for game";
        internal Func<string> Activity = () => "Auto";
        internal Action<string> SelectActivity = value => {};
        internal Action ChooseCharacter = () => {};
        internal Func<bool> Paused = () => false;
        internal Action<bool> Pause = value => {};
        internal Func<bool> Startup = () => false;
        internal Action<bool> SetStartup = value => {};
        internal Func<int> CurrentSong = () => 0;
        internal Func<string> RuntimePreferences = () => "Waiting for the game.";
    }

    internal static class SettingsUi
    {
        internal static readonly Color Ink = DesktopDesign.Ink, Muted = DesktopDesign.Muted;
        internal static FlowLayoutPanel Page(string title, string description)
        {
            var page = new DesktopStack { Dock = DockStyle.Fill };
            var heading = Text(title); heading.Font = DesktopDesign.Heading(18); heading.ForeColor = Ink; heading.Margin = new Padding(0, 0, 0, 6); heading.Name = "PageHeading";
            var intro = Text(description); intro.Margin = new Padding(0, 0, 0, 24); intro.Name = "PageDescription";
            page.Controls.Add(heading); page.Controls.Add(intro); return page;
        }
        internal static Control StickySave(FlowLayoutPanel page, Button save)
        {
            var host = new UserControl { Dock = DockStyle.Fill, BackColor = DesktopDesign.Background, AutoScaleMode = AutoScaleMode.None };
            var footer = new Panel { Dock = DockStyle.Bottom, Height = 68, Padding = new Padding(26, 12, 26, 12), BackColor = Color.White };
            footer.Paint += (s,e) => { using (var pen = new Pen(DesktopDesign.Border)) e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0); };
            var note = new Label { Font=DesktopDesign.Body(9), Dock = DockStyle.Fill, AutoSize = false, UseMnemonic = false, Text = "Save your preferences when you're ready.", TextAlign = ContentAlignment.MiddleLeft, ForeColor = Muted };
            save.Dock = DockStyle.Right; footer.Controls.Add(note); footer.Controls.Add(save); host.Controls.Add(page); host.Controls.Add(footer); return host;
        }
        internal static Label Text(string value) { return new Label { Font=DesktopDesign.Body(), UseMnemonic = false, Text = value, AutoSize = false, ForeColor = Muted, Margin = new Padding(0, 0, 0, 12) }; }
        internal static Label Section(string value) { var label = Text(value); label.Tag = "section"; label.Font = DesktopDesign.Heading(11); label.ForeColor = Ink; return label; }
        internal static Button Button(string text, Action action)
        {
            var button = new DesktopButton { Text = text, AutoSize = true, Primary = text == "Save changes" || text == "Apply switch" || text == "Generate current cover", Margin = new Padding(0, 2, 8, 8), Padding = new Padding(12, 6, 12, 6) };
            button.Size = button.GetPreferredSize(Size.Empty); button.Click += (s,e) => action(); return button;
        }
        internal static FlowLayoutPanel Row(params Control[] controls) { var row = new DesktopRow(); row.Controls.AddRange(controls); return row; }
        internal static CheckBox Check(string text, bool value) { return new DesktopToggle { Text = text, Checked = value, Tag = "wide", Margin = new Padding(0, 0, 0, 6), ForeColor = Ink }; }
        internal static ComboBox Choices(string[] values, string selected, int width = 280) { var box = new ComboBox { Font=DesktopDesign.Body(), DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(248,249,252), Width = width, Margin = new Padding(0, 2, 0, 12) }; box.Items.AddRange(values); box.SelectedItem = selected; if (box.SelectedIndex < 0) box.SelectedIndex = 0; return box; }
        internal static TextBox Report(int height) { return new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Height = height, Width = 620, Tag = "wide", BackColor = Color.FromArgb(247,248,252), ForeColor = Muted, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Consolas", 9) }; }
        internal static string ConcertEffect(Settings before, Settings after)
        {
            string text = "Saved. Playback controls update now when the active integration supports them.";
            if (before.ConcertAllowDuplicates != after.ConcertAllowDuplicates) text += " Duplicate casting applies on the formation screen.";
            bool arrangement = before.ConcertAudioMode == "Solo" || before.ConcertAudioMode == "Duet" || after.ConcertAudioMode == "Solo" || after.ConcertAudioMode == "Duet";
            if (before.ConcertAudioMode != after.ConcertAudioMode || before.ConcertVoice1 != after.ConcertVoice1 || before.ConcertVoice2 != after.ConcertVoice2)
                text += arrangement ? " Load the concert again to apply the vocal arrangement." : " Vocal balance updates during playback when supported.";
            return text;
        }
    }
}
