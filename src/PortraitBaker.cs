using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace UmapyonPresence
{
    internal static class PortraitBaker
    {
        private static readonly Dictionary<int, DateTime> next = new Dictionary<int, DateTime>();
        internal static string Folder { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "generated-portraits"); } }
        internal static Task Prepare(int card, Settings settings)
        {
            DateTime check;
            if (next.TryGetValue(card, out check) && check > DateTime.UtcNow) return Task.FromResult(0);
            next[card] = DateTime.UtcNow.AddHours(1);
            return Task.Run(async () => {
                try
                {
                    int dress = OutfitCatalog.DressForCard(card); if (dress == 0) return;
                    Directory.CreateDirectory(Folder);
                    string file = Path.Combine(Folder, "portrait_" + card + "_" + dress + "_frame_v2.png");
                    if (File.Exists(file)) return;
                    await OutfitCatalog.RefreshMissing(new ConcertSnapshot { CastIds = new[] { card / 100 }, DressIds = new[] { dress }, MainPerformerCount = 1 }, settings);
                    string source = OutfitCatalog.BakeSource(card, settings); if (string.IsNullOrEmpty(source)) return;
                    ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                    byte[] bytes;
                    using (var web = new WebClient()) { web.Headers[HttpRequestHeader.UserAgent] = "UmaPresence/0.1"; bytes = await web.DownloadDataTaskAsync(source); }
                    if (bytes.Length > 8 * 1024 * 1024) return;
                    using (var stream = new MemoryStream(bytes)) using (var input = Image.FromStream(stream)) using (var framed = Compose(input)) framed.Save(file, ImageFormat.Png);
                    File.WriteAllText(Path.ChangeExtension(file, ".json"), Json.Encode(new { CardId = card, DressId = dress, AssetName = "portrait_" + card, Source = source, Image = file, ReadyForUpload = true }), new UTF8Encoding(false));
                    Log.Write("Framed costume portrait prepared: " + file);
                }
                catch (Exception ex) { Log.Write("Optional portrait bake deferred: " + ex.Message); }
            });
        }
        private static GraphicsPath Round(RectangleF box, float radius)
        {
            var path = new GraphicsPath(); float d = radius * 2;
            path.AddArc(box.Left, box.Top, d, d, 180, 90); path.AddArc(box.Right - d, box.Top, d, d, 270, 90);
            path.AddArc(box.Right - d, box.Bottom - d, d, d, 0, 90); path.AddArc(box.Left, box.Bottom - d, d, d, 90, 90); path.CloseFigure(); return path;
        }
        internal static Bitmap Compose(Image portrait)
        {
            var output = new Bitmap(512, 512, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(output))
            {
                g.Clear(Color.Transparent); g.SmoothingMode = SmoothingMode.AntiAlias; g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.ScaleTransform(2, 2);
                using (var frame = Round(new RectangleF(2, 2, 252, 252), 23))
                using (var fill = new LinearGradientBrush(new Rectangle(0, 0, 256, 256), Color.FromArgb(245, 244, 250), Color.FromArgb(168, 164, 181), 60))
                using (var edge = new Pen(Color.FromArgb(139, 135, 154), 2)) { g.FillPath(fill, frame); g.DrawPath(edge, frame); }
                using (var inside = Round(new RectangleF(12, 12, 232, 232), 14))
                {
                    g.SetClip(inside);
                    using (var background = new SolidBrush(Color.FromArgb(239, 236, 246))) g.FillRectangle(background, 12, 12, 232, 232);
                    using (var stripe = new Pen(Color.FromArgb(75, Color.White), 12)) for (int n = -256; n < 512; n += 44) g.DrawLine(stripe, n, 256, n + 256, 0);
                    g.DrawImage(portrait, new Rectangle(12, 12, 232, 232)); g.ResetClip();
                    using (var edge = new Pen(Color.FromArgb(118, 113, 135), 2)) g.DrawPath(edge, inside);
                }
            }
            return output;
        }
    }
}
